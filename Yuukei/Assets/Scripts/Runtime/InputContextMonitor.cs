using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kirurobo;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yuukei.Runtime
{
    /// <summary>
    /// ユーザー入力（クリック・ドラッグ・ファイルドロップ等）を監視し、
    /// 正規イベント名でランタイムに通知するコンポーネント。
    /// アイドル検知や定期発火も担当する。
    /// </summary>
    public sealed class InputContextMonitor : MonoBehaviour
    {
        private const float DoubleClickWindowSeconds = 0.28f;
        private const float DragThresholdPixels = 14f;
        private const float IdleThresholdSeconds = 45f;
        private const float PeriodicTickSeconds = 30f;

        private readonly HashSet<int> _allowedDisplayIndices = new HashSet<int>();
        private IDesktopPlatformAdapter _desktopAdapter;
        private UniWindowController _windowController;
        private MascotRuntime _mascotRuntime;
        private bool _enabled = true;
        private bool _pointerDownOnMascot;
        private bool _dragging;
        private Vector2 _pointerDownPosition;
        private float _lastClickReleasedAt = -10f;
        private float _lastIdleEmitAt = -10f;
        private float _lastPeriodicTickAt = -10f;
        private float _sessionStartedAt;
        private CancellationTokenSource _singleClickDelay;

        public event Action<string, IReadOnlyDictionary<string, object>> EventRaised;
        public event Action<IReadOnlyCollection<int>> AllowedDisplaysChanged;

        public float BusyScore { get; private set; }

        /// <summary>デスクトップアダプタとウィンドウコントローラを受け取り、入力監視を初期化する。</summary>
        public void Initialize(IDesktopPlatformAdapter desktopAdapter, UniWindowController windowController, MascotRuntime mascotRuntime)
        {
            _desktopAdapter = desktopAdapter;
            _windowController = windowController;
            _mascotRuntime = mascotRuntime;
            _sessionStartedAt = Time.realtimeSinceStartup;

            _allowedDisplayIndices.Clear();
            foreach (var display in _desktopAdapter.GetDisplays())
            {
                _allowedDisplayIndices.Add(display.Index);
            }

            if (_windowController != null)
            {
                _windowController.OnDropFiles += OnDropFiles;
            }

            Debug.Log("[InputContextMonitor] 入力監視を初期化しました");
        }

        /// <summary>入力の有効・無効を切り替える。</summary>
        public void SetInputEnabled(bool enabled)
        {
            Debug.Log($"[InputContextMonitor] 入力状態変更: {(_enabled ? "有効" : "無効")} → {(enabled ? "有効" : "無効")}");
            _enabled = enabled;
            if (!enabled)
            {
                CancelPointerState(clearDragMotion: true);
            }
        }

        /// <summary>フルスクリーンアプリの有無を考慮して有効なディスプレイ一覧を再計算する。</summary>
        public IReadOnlyCollection<int> RecalculateAllowedDisplays()
        {
            _allowedDisplayIndices.Clear();
            var displays = _desktopAdapter.GetDisplays();
            var blockedForegroundDisplay = _desktopAdapter.GetForegroundDisplayIndex();
            var fullscreen = _desktopAdapter.IsForegroundWindowFullscreen();

            foreach (var display in displays)
            {
                if (fullscreen && display.Index == blockedForegroundDisplay)
                {
                    continue;
                }

                _allowedDisplayIndices.Add(display.Index);
            }

            AllowedDisplaysChanged?.Invoke(_allowedDisplayIndices);
            return _allowedDisplayIndices;
        }

        /// <summary>毎フレーム呼ばれる。入力状態の更新とアイドル・定期発火の判定を行う。</summary>
        public void Tick()
        {
            var idleSeconds = _desktopAdapter.GetGlobalIdleSeconds();
            BusyScore = 1f - Mathf.Clamp01(idleSeconds / 15f);

            RecalculateAllowedDisplays();
            if (!_enabled || Mouse.current == null)
            {
                return;
            }

            HandlePointer();
            EmitIdleIfNeeded(idleSeconds);
            EmitPeriodicTickIfNeeded();
        }

        /// <summary>マウスポインタの状態を処理する（クリック・ドラッグ判定）。</summary>
        private void HandlePointer()
        {
            var mouse = Mouse.current;
            var position = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _pointerDownPosition = position;
                _pointerDownOnMascot = _mascotRuntime.HitTestScreenPoint(position);
                _dragging = false;
            }

            if (_pointerDownOnMascot && mouse.leftButton.isPressed && !_dragging)
            {
                var dragDelta = position - _pointerDownPosition;
                if (dragDelta.magnitude >= DragThresholdPixels)
                {
                    _dragging = true;
                    _mascotRuntime.SetUserDragMotionActive(true);
                    EmitPointerEvent("character_drag_started", position);
                }
            }

            if (_pointerDownOnMascot && _dragging && mouse.leftButton.isPressed)
            {
                var dragDelta = position - _pointerDownPosition;
                _mascotRuntime.MoveByScreenDelta(new Vector2(dragDelta.x, -dragDelta.y));
                _pointerDownPosition = position;
            }

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (_pointerDownOnMascot && _dragging)
                {
                    _mascotRuntime.SetUserDragMotionActive(false);
                    EmitPointerEvent("character_drag_ended", position);
                }
                else if (_pointerDownOnMascot)
                {
                    HandleClickRelease(position);
                }

                CancelPointerState(clearDragMotion: false);
            }
        }

        /// <summary>クリック解放時のシングル／ダブルクリック判定を行う。</summary>
        private void HandleClickRelease(Vector2 position)
        {
            var now = Time.realtimeSinceStartup;
            if (now - _lastClickReleasedAt <= DoubleClickWindowSeconds)
            {
                CancelPendingClick();
                EmitPointerEvent("character_double_clicked", position);
                _lastClickReleasedAt = -10f;
                return;
            }

            _lastClickReleasedAt = now;
            ScheduleSingleClickAsync(position).Forget();
        }

        private async UniTaskVoid ScheduleSingleClickAsync(Vector2 position)
        {
            CancelPendingClick();
            _singleClickDelay = new CancellationTokenSource();
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(DoubleClickWindowSeconds), cancellationToken: _singleClickDelay.Token);
                EmitPointerEvent("character_clicked", position);
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>アイドル状態が閾値を超えていたらイベントを発火する。</summary>
        private void EmitIdleIfNeeded(float idleSeconds)
        {
            if (idleSeconds < IdleThresholdSeconds)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _lastIdleEmitAt < IdleThresholdSeconds)
            {
                return;
            }

            _lastIdleEmitAt = Time.realtimeSinceStartup;
            Emit("idle_reached", new Dictionary<string, object>
            {
                ["_event_idle_seconds"] = idleSeconds,
            });
        }

        /// <summary>定期発火の時間間隔が経過していたらイベントを発火する。</summary>
        private void EmitPeriodicTickIfNeeded()
        {
            if (Time.realtimeSinceStartup - _lastPeriodicTickAt < PeriodicTickSeconds)
            {
                return;
            }

            _lastPeriodicTickAt = Time.realtimeSinceStartup;
            Emit("periodic_tick", new Dictionary<string, object>
            {
                ["_event_timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds(),
                ["_event_session_elapsed_seconds"] = Time.realtimeSinceStartup - _sessionStartedAt,
            });
        }

        private void EmitPointerEvent(string canonicalName, Vector2 position)
        {
            Emit(canonicalName, new Dictionary<string, object>
            {
                ["_event_x"] = position.x,
                ["_event_y"] = position.y,
                ["_event_character_id"] = _mascotRuntime.CharacterId,
            });
        }

        /// <summary>ファイルがウィンドウにドロップされたときの処理。</summary>
        private void OnDropFiles(string[] paths)
        {
            if (!_enabled || paths == null)
            {
                return;
            }

            Debug.Log($"[InputContextMonitor] ファイルドロップ受信: {paths.Length} 件");
            foreach (var path in paths)
            {
                var fileName = Path.GetFileName(path) ?? string.Empty;
                var extension = Path.GetExtension(path) ?? string.Empty;
                Emit("file_dropped", new Dictionary<string, object>
                {
                    ["_event_file_name"] = fileName,
                    ["_event_file_extension"] = extension,
                    ["_event_file_kind"] = FileKindClassifier.Classify(path, Directory.Exists(path)),
                    ["_event_character_id"] = _mascotRuntime.CharacterId,
                });
            }
        }

        /// <summary>イベントを発火する。periodic_tick以外はログに記録する。</summary>
        private void Emit(string canonicalName, IReadOnlyDictionary<string, object> context)
        {
            if (canonicalName != "periodic_tick")
            {
                Debug.Log($"[InputContextMonitor] イベント発火: {canonicalName}");
            }

            EventRaised?.Invoke(canonicalName, context);
        }

        private void CancelPendingClick()
        {
            if (_singleClickDelay == null)
            {
                return;
            }

            _singleClickDelay.Cancel();
            _singleClickDelay.Dispose();
            _singleClickDelay = null;
        }

        private void CancelPointerState(bool clearDragMotion)
        {
            CancelPendingClick();
            if (clearDragMotion && _dragging)
            {
                _mascotRuntime?.CancelUserDragMotionImmediately();
            }

            _pointerDownOnMascot = false;
            _dragging = false;
        }

        private void OnDestroy()
        {
            CancelPointerState(clearDragMotion: true);
            if (_windowController != null)
            {
                _windowController.OnDropFiles -= OnDropFiles;
            }
        }
    }
}
