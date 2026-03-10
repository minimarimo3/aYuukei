using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class MascotRuntimeTests
    {
        [Test]
        public void DragPresentation_TakesPriorityOverManualOverride_AndReturnsAfterRelease()
        {
            var root = new GameObject("MascotRuntimeTests");

            try
            {
                var runtime = root.AddComponent<MascotRuntime>();
                runtime.ConfigureDragMotion(new DragMotionSettings
                {
                    Clip = new AnimationClip(),
                });

                Assert.That(runtime.DebugPresentationMode, Is.EqualTo(MascotMotionPresentationMode.Automatic));

                runtime.PlayMotion("wave");
                Assert.That(runtime.DebugPresentationMode, Is.EqualTo(MascotMotionPresentationMode.ManualOverride));

                runtime.SetUserDragMotionActive(true);
                Assert.That(runtime.DebugPresentationMode, Is.EqualTo(MascotMotionPresentationMode.DragOverride));

                runtime.SetUserDragMotionActive(false);
                Assert.That(runtime.DebugPresentationMode, Is.EqualTo(MascotMotionPresentationMode.ManualOverride));

                runtime.ClearMotionOverride();
                Assert.That(runtime.DebugPresentationMode, Is.EqualTo(MascotMotionPresentationMode.Automatic));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetInputEnabled_WhenDragging_ClearsDragRequestAndBlendWeight()
        {
            var root = new GameObject("InputContextMonitorTests");

            try
            {
                var runtime = root.AddComponent<MascotRuntime>();
                runtime.ConfigureDragMotion(new DragMotionSettings
                {
                    Clip = new AnimationClip(),
                });
                runtime.SetUserDragMotionActive(true);
                SetPrivateField(runtime, "_dragMotionBlendWeight", 0.72f);

                var monitor = root.AddComponent<InputContextMonitor>();
                monitor.Initialize(new TestDesktopAdapter(), null, runtime);
                SetPrivateField(monitor, "_dragging", true);
                SetPrivateField(monitor, "_pointerDownOnMascot", true);

                monitor.SetInputEnabled(false);

                Assert.That(runtime.DebugIsUserDragMotionRequested, Is.False);
                Assert.That(runtime.DebugDragMotionBlendWeight, Is.EqualTo(0f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            var field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(TTarget).FullName, fieldName);
            }

            field.SetValue(target, value);
        }

        private sealed class TestDesktopAdapter : IDesktopPlatformAdapter
        {
            public event Action<TrayCommand> TrayCommandRequested;
            public event Action<ShortcutAction> ShortcutTriggered;

            public void Initialize()
            {
            }

            public void Shutdown()
            {
            }

            public void Tick()
            {
            }

            public void ApplyShortcuts(ShortcutConfigData shortcutConfig)
            {
            }

            public void UpdateShellState(AppShellState state)
            {
            }

            public IReadOnlyDictionary<ShortcutAction, ShortcutRegistrationStatus> GetShortcutStatuses()
            {
                return new Dictionary<ShortcutAction, ShortcutRegistrationStatus>();
            }

            public RectInt GetVirtualDesktopBounds()
            {
                return new RectInt(0, 0, 1920, 1080);
            }

            public IReadOnlyList<DesktopDisplayInfo> GetDisplays()
            {
                return Array.Empty<DesktopDisplayInfo>();
            }

            public int GetForegroundDisplayIndex()
            {
                return 0;
            }

            public bool IsForegroundWindowFullscreen()
            {
                return false;
            }

            public float GetGlobalIdleSeconds()
            {
                return 0f;
            }

            public bool TryLoadSecret(string key, out string value)
            {
                value = string.Empty;
                return false;
            }

            public void SaveSecret(string key, string value)
            {
            }

            public void DeleteSecret(string key)
            {
            }

            public void OpenUrl(string url)
            {
            }
        }
    }
}
