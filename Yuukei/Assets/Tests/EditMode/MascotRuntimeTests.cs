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
            var cameraObject = new GameObject("MascotCamera");

            try
            {
                var runtime = CreateInitializedRuntime(root, cameraObject);
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
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetInputEnabled_WhenDragging_ClearsDragRequestAndBlendWeight()
        {
            var root = new GameObject("InputContextMonitorTests");
            var cameraObject = new GameObject("MascotCamera");

            try
            {
                var runtime = CreateInitializedRuntime(root, cameraObject);
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
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_UsesFixedSafePosition_AndDoesNotDrift()
        {
            var root = new GameObject("FloatingAnchorTests");
            var cameraObject = new GameObject("MascotCamera");

            try
            {
                var runtime = CreateInitializedRuntime(root, cameraObject);
                var displayBounds = new RectInt(0, 0, 1920, 1080);
                var displays = new[] { new DesktopDisplayInfo(0, displayBounds) };
                var allowedDisplays = new[] { 0 };

                runtime.SetDesktopContext(displayBounds, displays, allowedDisplays);
                var expected = new Vector2(1760f, 940f);
                Assert.That(runtime.DebugDesktopPosition.x, Is.EqualTo(expected.x).Within(0.001f));
                Assert.That(runtime.DebugDesktopPosition.y, Is.EqualTo(expected.y).Within(0.001f));

                for (var i = 0; i < 180; i++)
                {
                    runtime.SetDesktopContext(displayBounds, displays, allowedDisplays);
                    runtime.Tick(0.1f, 1f);
                }

                Assert.That(runtime.DebugDesktopPosition.x, Is.EqualTo(expected.x).Within(0.001f));
                Assert.That(runtime.DebugDesktopPosition.y, Is.EqualTo(expected.y).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MoveByScreenDelta_PreservesExplicitPositionAcrossTicks()
        {
            var root = new GameObject("DragAnchorTests");
            var cameraObject = new GameObject("MascotCamera");

            try
            {
                var runtime = CreateInitializedRuntime(root, cameraObject);
                var displayBounds = new RectInt(0, 0, 1920, 1080);
                var displays = new[] { new DesktopDisplayInfo(0, displayBounds) };
                var allowedDisplays = new[] { 0 };
                runtime.SetDesktopContext(displayBounds, displays, allowedDisplays);

                var delta = new Vector2(-120f, 64f);
                var expected = runtime.DebugDesktopPosition + delta;
                runtime.MoveByScreenDelta(delta);

                for (var i = 0; i < 60; i++)
                {
                    runtime.SetDesktopContext(displayBounds, displays, allowedDisplays);
                    runtime.Tick(0.1f, 0.8f);
                }

                Assert.That(runtime.DebugDesktopPosition.x, Is.EqualTo(expected.x).Within(0.001f));
                Assert.That(runtime.DebugDesktopPosition.y, Is.EqualTo(expected.y).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ApplyFloatingPose_UsesConfiguredWaveFormulaOnVisualRoot()
        {
            var root = new GameObject("FloatingPoseTests");
            var cameraObject = new GameObject("MascotCamera");

            try
            {
                var runtime = CreateInitializedRuntime(root, cameraObject);
                var settings = new GlideLocomotionSettings();
                runtime.ApplyGlideSettings(settings);
                runtime.SetDesktopContext(new RectInt(0, 0, 1920, 1080), new[] { new DesktopDisplayInfo(0, new RectInt(0, 0, 1920, 1080)) }, new[] { 0 });

                SetPrivateField(runtime, "_phase1", 0f);
                SetPrivateField(runtime, "_phase2", 0f);
                SetPrivateField(runtime, "_phase3", 0f);
                SetPrivateField(runtime, "_phaseTilt", 0f);
                SetPrivateField(runtime, "_floatTime", 0f);

                InvokePrivateMethod(runtime, "ApplyFloatingPose", 0.5f);

                var time = 0.5f;
                var expectedY = settings.FloatAmplitudeY * (
                    0.55f * Mathf.Sin(2f * Mathf.PI * settings.FloatFrequency1 * time) +
                    0.30f * Mathf.Sin(2f * Mathf.PI * settings.FloatFrequency2 * time) +
                    0.15f * Mathf.Sin(2f * Mathf.PI * settings.FloatFrequency3 * time));
                var expectedX = settings.FloatAmplitudeX *
                    Mathf.Sin(2f * Mathf.PI * settings.FloatFrequency3 * time + 1.2f);
                var expectedTilt = settings.TiltAmplitudeDeg *
                    Mathf.Sin(2f * Mathf.PI * settings.TiltFrequency * time);
                var actualTilt = NormalizeAngle(runtime.DebugVisualLocalRotation.eulerAngles.z);

                Assert.That(runtime.DebugVisualLocalPosition.x, Is.EqualTo(expectedX).Within(0.0001f));
                Assert.That(runtime.DebugVisualLocalPosition.y, Is.EqualTo(expectedY).Within(0.0001f));
                Assert.That(runtime.DebugVisualLocalPosition.z, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(actualTilt, Is.EqualTo(expectedTilt).Within(0.0001f));
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

        private static float NormalizeAngle(float angle)
        {
            if (angle > 180f)
            {
                angle -= 360f;
            }

            return angle;
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
