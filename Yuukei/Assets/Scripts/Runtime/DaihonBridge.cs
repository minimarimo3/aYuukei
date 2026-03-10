using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Daihon;
using UnityEngine;

namespace Yuukei.Runtime
{
    /// <summary>
    /// 台本（Daihon）スクリプトとランタイムを仲介するブリッジ。
    /// パッケージからスクリプトを読み込み、イベントをキューイングして順次実行する。
    /// </summary>
    public sealed class DaihonBridge
    {
        private sealed class PendingEvent
        {
            public PendingEvent(string canonicalName, IReadOnlyDictionary<string, object> context, CancellationToken cancellationToken)
            {
                CanonicalName = canonicalName;
                Context = context != null
                    ? new Dictionary<string, object>(context)
                    : new Dictionary<string, object>();
                CancellationToken = cancellationToken;
            }

            public string CanonicalName { get; }
            public IReadOnlyDictionary<string, object> Context { get; }
            public CancellationToken CancellationToken { get; }
            public UniTaskCompletionSource CompletionSource { get; } = new UniTaskCompletionSource();
        }

        private sealed class LoadedScript
        {
            public LoadedScript(string sourcePath, DaihonScriptRuntime.ScriptMetadata metadata)
            {
                SourcePath = sourcePath;
                Metadata = metadata;
            }

            public string SourcePath { get; }
            public DaihonScriptRuntime.ScriptMetadata Metadata { get; }
        }

        private sealed class BridgeActionHandler : IActionHandler
        {
            private readonly SpeechBubbleController _speechBubbleController;
            private readonly DaihonFunctionDispatcher _dispatcher;
            private readonly CancellationToken _cancellationToken;

            public BridgeActionHandler(SpeechBubbleController speechBubbleController, DaihonFunctionDispatcher dispatcher, CancellationToken cancellationToken)
            {
                _speechBubbleController = speechBubbleController;
                _dispatcher = dispatcher;
                _cancellationToken = cancellationToken;
            }

            public Task ShowDialogueAsync(string text)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                return _speechBubbleController.ShowDialogueAsync(text, _cancellationToken).AsTask();
            }

            public Task<DaihonValue> CallFunctionAsync(string functionName, IReadOnlyList<DaihonValue> positionalArgs, IReadOnlyDictionary<string, DaihonValue> namedArgs)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                return _dispatcher.InvokeAsync(functionName, positionalArgs, _cancellationToken).AsTask();
            }
        }

        private readonly AliasRegistry _aliasRegistry;
        private readonly YuukeiVariableStore _variableStore;
        private readonly DaihonScriptRuntime _runtime = new DaihonScriptRuntime();
        private readonly DaihonFunctionDispatcher _dispatcher;
        private readonly SpeechBubbleController _speechBubbleController;
        private readonly List<PendingEvent> _queue = new List<PendingEvent>();
        private readonly List<LoadedScript> _activeScripts = new List<LoadedScript>();

        private PendingEvent _coalescedPeriodicTick;
        private CancellationTokenSource _runtimeCancellation = new CancellationTokenSource();
        private bool _isProcessing;
        private bool _temporarilyDisabled;

        /// <summary>ブリッジを初期化し、関数ディスパッチャーを構築する。</summary>
        public DaihonBridge(
            AliasRegistry aliasRegistry,
            YuukeiVariableStore variableStore,
            SpeechBubbleController speechBubbleController,
            ChoiceOverlayController choiceOverlayController,
            MascotRuntime mascotRuntime)
        {
            _aliasRegistry = aliasRegistry;
            _variableStore = variableStore;
            _speechBubbleController = speechBubbleController;
            _dispatcher = new DaihonFunctionDispatcher(aliasRegistry, speechBubbleController, choiceOverlayController, mascotRuntime, variableStore);
            Debug.Log("[DaihonBridge] 初期化完了");
        }

        /// <summary>外部から関数を登録する。</summary>
        public void RegisterFunction(string canonicalName, CanonicalFunctionDelegate function)
        {
            Debug.Log($"[DaihonBridge] 関数を登録: {canonicalName}");
            _dispatcher.RegisterFunction(canonicalName, function);
        }

        public void RegisterFunctionAlias(string alias, string canonicalName)
        {
            _aliasRegistry.RegisterFunctionAlias(alias, canonicalName);
        }

        public void RegisterEventAlias(string alias, string canonicalName)
        {
            _aliasRegistry.RegisterEventAlias(alias, canonicalName);
        }

        public bool TryResolveEventName(string rawName, out string canonicalName)
        {
            return _aliasRegistry.TryResolveEventName(rawName, out canonicalName);
        }

        public bool TryResolveFunctionName(string rawName, out string canonicalName)
        {
            return _aliasRegistry.TryResolveFunctionName(rawName, out canonicalName);
        }

        /// <summary>アクティブパッケージのスクリプトを読み込み直す。</summary>
        public async UniTask ApplyActivePackageAsync(PackageContentSelection contentSelection, PackageAliasManifest aliases, CancellationToken cancellationToken)
        {
            Debug.Log("[DaihonBridge] パッケージ適用開始 — 既存状態をクリア");
            CancelAndClear();
            _aliasRegistry.ResetToBuiltins();
            _aliasRegistry.LoadPackageAliases(aliases);
            _activeScripts.Clear();

            if (contentSelection == null || contentSelection.DaihonPaths == null || contentSelection.DaihonPaths.Count == 0)
            {
                Debug.Log("[DaihonBridge] 読み込むスクリプトなし");
                return;
            }

            Debug.Log($"[DaihonBridge] {contentSelection.DaihonPaths.Count} 件のスクリプトパスを処理");
            foreach (var daihonPath in contentSelection.DaihonPaths)
            {
                if (string.IsNullOrWhiteSpace(daihonPath))
                {
                    continue;
                }

                if (!File.Exists(daihonPath))
                {
                    Debug.LogWarning($"[DaihonBridge] Skipping missing Daihon script '{daihonPath}'.");
                    continue;
                }

                try
                {
                    Debug.Log($"[DaihonBridge] スクリプト読み込み中: {daihonPath}");
                    var scriptText = await File.ReadAllTextAsync(daihonPath, cancellationToken);
                    var metadata = _runtime.Parse(scriptText);
                    if (metadata == null)
                    {
                        Debug.LogWarning($"[DaihonBridge] Skipping broken Daihon script '{daihonPath}'.");
                        continue;
                    }

                    _activeScripts.Add(new LoadedScript(daihonPath, metadata));
                    Debug.Log($"[DaihonBridge] スクリプト読み込み成功: {daihonPath}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[DaihonBridge] Failed to load Daihon script '{daihonPath}'. Skipping. {exception.Message}");
                }
            }

            Debug.Log($"[DaihonBridge] パッケージ適用完了 — {_activeScripts.Count} 件のスクリプトが有効");
        }

        /// <summary>イベントを発火し、キューに追加する。</summary>
        public UniTask RaiseEventAsync(string eventName, IReadOnlyDictionary<string, object> context, CancellationToken cancellationToken)
        {
            if (_temporarilyDisabled || _activeScripts.Count == 0)
            {
                Debug.Log($"[DaihonBridge] イベント '{eventName}' をスキップ（disabled={_temporarilyDisabled}, scripts={_activeScripts.Count}）");
                return UniTask.CompletedTask;
            }

            if (!_aliasRegistry.TryResolveEventName(eventName, out var canonicalName))
            {
                Debug.Log($"[DaihonBridge] イベント '{eventName}' は未解決 — スキップ");
                return UniTask.CompletedTask;
            }

            var pendingEvent = new PendingEvent(canonicalName, context, cancellationToken);
            if (string.Equals(canonicalName, "periodic_tick", StringComparison.Ordinal))
            {
                _coalescedPeriodicTick?.CompletionSource.TrySetCanceled();
                _coalescedPeriodicTick = pendingEvent;
            }
            else
            {
                Debug.Log($"[DaihonBridge] イベント '{canonicalName}' をキューに追加（キュー長: {_queue.Count + 1}）");
                _queue.Add(pendingEvent);
            }

            StartProcessingLoop();
            return pendingEvent.CompletionSource.Task;
        }

        /// <summary>一時的にイベント処理を無効化/有効化する。</summary>
        public void SetTemporarilyDisabled(bool disabled)
        {
            Debug.Log($"[DaihonBridge] 一時無効化状態を変更: {_temporarilyDisabled} → {disabled}");
            _temporarilyDisabled = disabled;
            if (disabled)
            {
                CancelAndClear();
            }
        }

        /// <summary>実行中の処理をキャンセルし、キューを全消去する。</summary>
        public void CancelAndClear()
        {
            Debug.Log($"[DaihonBridge] キャンセル＆クリア実行（キュー残: {_queue.Count}）");
            _runtimeCancellation.Cancel();
            _runtimeCancellation.Dispose();
            _runtimeCancellation = new CancellationTokenSource();

            foreach (var pendingEvent in _queue)
            {
                pendingEvent.CompletionSource.TrySetCanceled();
            }

            _queue.Clear();
            _coalescedPeriodicTick?.CompletionSource.TrySetCanceled();
            _coalescedPeriodicTick = null;
            _variableStore.ResetTransientState();
            _speechBubbleController.Hide();
        }

        private void StartProcessingLoop()
        {
            if (_isProcessing)
            {
                return;
            }

            ProcessQueueAsync().Forget();
        }

        /// <summary>キュー内のイベントを順次処理するループ。</summary>
        private async UniTaskVoid ProcessQueueAsync()
        {
            Debug.Log("[DaihonBridge] キュー処理ループ開始");
            _isProcessing = true;
            try
            {
                while (true)
                {
                    PendingEvent next = null;
                    if (_queue.Count > 0)
                    {
                        next = _queue[0];
                        _queue.RemoveAt(0);
                    }
                    else if (_coalescedPeriodicTick != null)
                    {
                        next = _coalescedPeriodicTick;
                        _coalescedPeriodicTick = null;
                    }

                    if (next == null)
                    {
                        break;
                    }

                    await ExecutePendingEventAsync(next);
                }
            }
            finally
            {
                _isProcessing = false;
                Debug.Log("[DaihonBridge] キュー処理ループ終了");
            }
        }

        /// <summary>キューから取り出した1件のイベントを全スクリプトに対して実行する。</summary>
        private async UniTask ExecutePendingEventAsync(PendingEvent pendingEvent)
        {
            Debug.Log($"[DaihonBridge] イベント実行開始: '{pendingEvent.CanonicalName}'（対象スクリプト数: {_activeScripts.Count}）");
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_runtimeCancellation.Token, pendingEvent.CancellationToken);
            try
            {
                linkedSource.Token.ThrowIfCancellationRequested();
                var actionHandler = new BridgeActionHandler(_speechBubbleController, _dispatcher, linkedSource.Token);
                foreach (var script in _activeScripts)
                {
                    linkedSource.Token.ThrowIfCancellationRequested();
                    _variableStore.InjectEventContext(pendingEvent.Context);
                    try
                    {
                        try
                        {
                            await _runtime.RunEventAsync(script.Metadata, pendingEvent.CanonicalName, _aliasRegistry, actionHandler, _variableStore, linkedSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            Debug.LogError($"[DaihonBridge] Script '{script.SourcePath}' failed while executing '{pendingEvent.CanonicalName}'. Skipping remaining work in that script. {exception}");
                        }
                    }
                    finally
                    {
                        _variableStore.ResetTransientState();
                    }
                }

                Debug.Log($"[DaihonBridge] イベント実行完了: '{pendingEvent.CanonicalName}'");
                pendingEvent.CompletionSource.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[DaihonBridge] イベント実行キャンセル: '{pendingEvent.CanonicalName}'");
                pendingEvent.CompletionSource.TrySetCanceled();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DaihonBridge] Runtime error while executing '{pendingEvent.CanonicalName}': {exception}");
                pendingEvent.CompletionSource.TrySetException(exception);
            }
        }
    }
}
