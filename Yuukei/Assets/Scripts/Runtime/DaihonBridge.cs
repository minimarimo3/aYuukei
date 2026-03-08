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
    public sealed class DaihonBridge
    {
        private sealed class PendingEvent
        {
            public PendingEvent(string canonicalName, IReadOnlyDictionary<string, object> context, CancellationToken cancellationToken)
            {
                CanonicalName = canonicalName;
                Context = context;
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
        }

        public void RegisterFunction(string canonicalName, CanonicalFunctionDelegate function)
        {
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

        public async UniTask ApplyActivePackageAsync(PackageContentSelection contentSelection, PackageAliasManifest aliases, CancellationToken cancellationToken)
        {
            CancelAndClear();
            _aliasRegistry.ResetToBuiltins();
            _aliasRegistry.LoadPackageAliases(aliases);
            _activeScripts.Clear();

            if (contentSelection == null || contentSelection.DaihonPaths == null || contentSelection.DaihonPaths.Count == 0)
            {
                return;
            }

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
                    var scriptText = await File.ReadAllTextAsync(daihonPath, cancellationToken);
                    var metadata = _runtime.Parse(scriptText);
                    if (metadata == null)
                    {
                        Debug.LogWarning($"[DaihonBridge] Skipping broken Daihon script '{daihonPath}'.");
                        continue;
                    }

                    _activeScripts.Add(new LoadedScript(daihonPath, metadata));
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
        }

        public UniTask RaiseEventAsync(string eventName, IReadOnlyDictionary<string, object> context, CancellationToken cancellationToken)
        {
            if (_temporarilyDisabled || _activeScripts.Count == 0)
            {
                return UniTask.CompletedTask;
            }

            if (!_aliasRegistry.TryResolveEventName(eventName, out var canonicalName))
            {
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
                _queue.Add(pendingEvent);
            }

            StartProcessingLoop();
            return pendingEvent.CompletionSource.Task;
        }

        public void SetTemporarilyDisabled(bool disabled)
        {
            _temporarilyDisabled = disabled;
            if (disabled)
            {
                CancelAndClear();
            }
        }

        public void CancelAndClear()
        {
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

        private async UniTaskVoid ProcessQueueAsync()
        {
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
            }
        }

        private async UniTask ExecutePendingEventAsync(PendingEvent pendingEvent)
        {
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
                        await _runtime.RunEventAsync(script.Metadata, pendingEvent.CanonicalName, _aliasRegistry, actionHandler, _variableStore, linkedSource.Token);
                    }
                    finally
                    {
                        _variableStore.ResetTransientState();
                    }
                }

                pendingEvent.CompletionSource.TrySetResult();
            }
            catch (OperationCanceledException)
            {
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
