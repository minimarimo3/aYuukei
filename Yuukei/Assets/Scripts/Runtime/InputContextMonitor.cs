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
        }

        public void SetInputEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled)
            {
                CancelPendingClick();
            }
        }

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

                if (display.Index == blockedForegroundDisplay)
                {
                    continue;
                }

                _allowedDisplayIndices.Add(display.Index);
            }

            AllowedDisplaysChanged?.Invoke(_allowedDisplayIndices);
            return _allowedDisplayIndices;
        }

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
                    EmitPointerEvent("character_drag_ended", position);
                }
                else if (_pointerDownOnMascot)
                {
                    HandleClickRelease(position);
                }

                _pointerDownOnMascot = false;
                _dragging = false;
            }
        }

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

        private void OnDropFiles(string[] paths)
        {
            if (!_enabled || paths == null)
            {
                return;
            }

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

        private void Emit(string canonicalName, IReadOnlyDictionary<string, object> context)
        {
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

        private void OnDestroy()
        {
            CancelPendingClick();
            if (_windowController != null)
            {
                _windowController.OnDropFiles -= OnDropFiles;
            }
        }
    }
}
