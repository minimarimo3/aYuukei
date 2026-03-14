using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class InputContextMonitorTests
    {
        [UnityTest]
        public IEnumerator SingleClick_RemainsScheduledAfterPointerStateReset_AndIncludesBodyPartContext()
        {
            var root = new GameObject("InputContextMonitorSingleClickTests");
            var cameraObject = new GameObject("MascotCamera");

            try
            {
                var runtime = CreateInitializedRuntime(root, cameraObject);
                var monitor = root.AddComponent<InputContextMonitor>();
                monitor.Initialize(new TestDesktopAdapter(), null, runtime, null);

                var events = new List<(string CanonicalName, IReadOnlyDictionary<string, object> Context)>();
                monitor.EventRaised += (canonicalName, context) => events.Add((canonicalName, context));

                SetPrivateField(monitor, "_pointerDownOnMascot", true);
                SetPrivateField(monitor, "_pointerDownBodyPart", "belly");
                InvokePrivateMethod(monitor, "HandleClickRelease", new Vector2(320f, 240f));
                InvokePrivateMethod(monitor, "CancelPointerState", false, false);

                yield return new WaitForSecondsRealtime(0.35f);

                Assert.That(events.Count, Is.EqualTo(1));
                Assert.That(events[0].CanonicalName, Is.EqualTo("character_clicked"));
                Assert.That(events[0].Context["_event_body_part"], Is.EqualTo("belly"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator DoubleClick_CancelsPendingSingleClick()
        {
            var root = new GameObject("InputContextMonitorDoubleClickTests");
            var cameraObject = new GameObject("MascotCamera");

            try
            {
                var runtime = CreateInitializedRuntime(root, cameraObject);
                var monitor = root.AddComponent<InputContextMonitor>();
                monitor.Initialize(new TestDesktopAdapter(), null, runtime, null);

                var events = new List<string>();
                monitor.EventRaised += (canonicalName, _) => events.Add(canonicalName);

                SetPrivateField(monitor, "_pointerDownOnMascot", true);
                SetPrivateField(monitor, "_pointerDownBodyPart", "head");
                InvokePrivateMethod(monitor, "HandleClickRelease", new Vector2(300f, 220f));
                InvokePrivateMethod(monitor, "CancelPointerState", false, false);

                yield return new WaitForSecondsRealtime(0.10f);

                SetPrivateField(monitor, "_pointerDownOnMascot", true);
                SetPrivateField(monitor, "_pointerDownBodyPart", "head");
                InvokePrivateMethod(monitor, "HandleClickRelease", new Vector2(302f, 224f));
                InvokePrivateMethod(monitor, "CancelPointerState", false, false);

                yield return new WaitForSecondsRealtime(0.35f);

                Assert.That(events, Is.EquivalentTo(new[] { "character_double_clicked" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static MascotRuntime CreateInitializedRuntime(GameObject root, GameObject cameraObject)
        {
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            var runtime = root.AddComponent<MascotRuntime>();
            runtime.ApplyGlideSettings(new GlideLocomotionSettings());
            runtime.Initialize(camera);
            return runtime;
        }

        private static void InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }

            method.Invoke(target, args);
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
            public event Action<TrayCommand> TrayCommandRequested
            {
                add { }
                remove { }
            }

            public event Action<ShortcutAction> ShortcutTriggered
            {
                add { }
                remove { }
            }

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
