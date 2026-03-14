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
                SetPrivateField(runtime, "_dragDeltaThisFrame", new Vector2(32f, -18f));
                SetPrivateField(runtime, "_dragSecondaryHorizontal", 0.35f);
                SetPrivateField(runtime, "_dragSecondaryHang", 0.48f);
                SetPrivateField(runtime, "_dragSecondaryHorizontalVelocity", -0.27f);
                SetPrivateField(runtime, "_dragSecondaryHangVelocity", 0.19f);
                SetPrivateField(runtime, "_dragSecondaryPassiveTime", 1.3f);

                var monitor = root.AddComponent<InputContextMonitor>();
                monitor.Initialize(new TestDesktopAdapter(), null, runtime, null);
                SetPrivateField(monitor, "_dragging", true);
                SetPrivateField(monitor, "_pointerDownOnMascot", true);

                monitor.SetInputEnabled(false);

                Assert.That(runtime.DebugIsUserDragMotionRequested, Is.False);
                Assert.That(runtime.DebugDragMotionBlendWeight, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, Vector2>(runtime, "_dragDeltaThisFrame"), Is.EqualTo(Vector2.zero));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHorizontal"), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHang"), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHorizontalVelocity"), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHangVelocity"), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryPassiveTime"), Is.EqualTo(0f).Within(0.0001f));
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

        [Test]
        public void ApplyFloatingPose_SuppressesDuringDrag_AndBlendsBackAfterRelease()
        {
            var root = new GameObject("FloatingBlendTests");
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

                runtime.SetUserDragMotionActive(true);
                InvokePrivateMethod(runtime, "ApplyFloatingPose", 0.5f);

                Assert.That(runtime.DebugVisualLocalPosition, Is.EqualTo(Vector3.zero));
                Assert.That(runtime.DebugVisualLocalRotation.eulerAngles, Is.EqualTo(Vector3.zero));

                runtime.SetUserDragMotionActive(false);
                SetPrivateField(runtime, "_floatTime", 0f);
                InvokePrivateMethod(runtime, "ApplyFloatingPose", 0.05f);

                var earlyBlend = Mathf.MoveTowards(0f, 1f, 0.05f / 0.22f);
                var earlyPosition = EvaluateFloatingPosition(settings, 0.05f, earlyBlend);
                var earlyTilt = EvaluateFloatingTilt(settings, 0.05f, earlyBlend);
                var earlyActualTilt = NormalizeAngle(runtime.DebugVisualLocalRotation.eulerAngles.z);

                Assert.That(runtime.DebugVisualLocalPosition.x, Is.EqualTo(earlyPosition.x).Within(0.0001f));
                Assert.That(runtime.DebugVisualLocalPosition.y, Is.EqualTo(earlyPosition.y).Within(0.0001f));
                Assert.That(earlyActualTilt, Is.EqualTo(earlyTilt).Within(0.0001f));

                SetPrivateField(runtime, "_floatTime", 0f);
                InvokePrivateMethod(runtime, "ApplyFloatingPose", 0.4f);

                var fullPosition = EvaluateFloatingPosition(settings, 0.4f, 1f);
                var fullTilt = EvaluateFloatingTilt(settings, 0.4f, 1f);
                var fullActualTilt = NormalizeAngle(runtime.DebugVisualLocalRotation.eulerAngles.z);

                Assert.That(runtime.DebugVisualLocalPosition.x, Is.EqualTo(fullPosition.x).Within(0.0001f));
                Assert.That(runtime.DebugVisualLocalPosition.y, Is.EqualTo(fullPosition.y).Within(0.0001f));
                Assert.That(fullActualTilt, Is.EqualTo(fullTilt).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_UpdatesProceduralDragSecondaryMotion_AndSettlesAfterRelease()
        {
            var root = new GameObject("DragSecondaryTests");
            var cameraObject = new GameObject("MascotCamera");

            try
            {
                var runtime = CreateInitializedRuntime(root, cameraObject);
                var displayBounds = new RectInt(0, 0, 1920, 1080);
                var displays = new[] { new DesktopDisplayInfo(0, displayBounds) };
                runtime.SetDesktopContext(displayBounds, displays, new[] { 0 });

                runtime.SetUserDragMotionActive(true);
                runtime.MoveByScreenDelta(new Vector2(240f, 0f));
                runtime.Tick(0.1f, 0f);

                var expectedHorizontal = StepSpring(0f, 0f, 1f, 0.1f);
                var expectedHang = StepSpring(0f, 0f, 0.72f, 0.1f);

                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHorizontal"), Is.EqualTo(expectedHorizontal.Value).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHorizontalVelocity"), Is.EqualTo(expectedHorizontal.Velocity).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHang"), Is.EqualTo(expectedHang.Value).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHangVelocity"), Is.EqualTo(expectedHang.Velocity).Within(0.0001f));

                runtime.SetUserDragMotionActive(false);
                for (var i = 0; i < 30; i++)
                {
                    runtime.Tick(0.1f, 0f);
                }

                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHorizontal"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHorizontalVelocity"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHang"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHangVelocity"), Is.EqualTo(0f).Within(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_WhenDraggingWithoutMovement_AddsPassiveGravitySway()
        {
            var root = new GameObject("DragPassiveSwayTests");
            var cameraObject = new GameObject("MascotCamera");

            try
            {
                var runtime = CreateInitializedRuntime(root, cameraObject);
                var displayBounds = new RectInt(0, 0, 1920, 1080);
                var displays = new[] { new DesktopDisplayInfo(0, displayBounds) };
                runtime.SetDesktopContext(displayBounds, displays, new[] { 0 });

                runtime.SetUserDragMotionActive(true);
                runtime.Tick(0.1f, 0f);

                var passiveHorizontalTarget = EvaluatePassiveDragHorizontalSway(0.1f);
                var passiveHangTarget = 0.72f + EvaluatePassiveDragHangSway(0.1f);
                var expectedHorizontal = StepSpring(0f, 0f, passiveHorizontalTarget, 0.1f);
                var expectedHang = StepSpring(0f, 0f, passiveHangTarget, 0.1f);

                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryPassiveTime"), Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHorizontal"), Is.EqualTo(expectedHorizontal.Value).Within(0.0001f));
                Assert.That(GetPrivateField<MascotRuntime, float>(runtime, "_dragSecondaryHang"), Is.EqualTo(expectedHang.Value).Within(0.0001f));
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

        private static Vector3 EvaluateFloatingPosition(GlideLocomotionSettings settings, float time, float blend)
        {
            var y = settings.FloatAmplitudeY * (
                0.55f * Mathf.Sin(2f * Mathf.PI * settings.FloatFrequency1 * time) +
                0.30f * Mathf.Sin(2f * Mathf.PI * settings.FloatFrequency2 * time) +
                0.15f * Mathf.Sin(2f * Mathf.PI * settings.FloatFrequency3 * time));
            var x = settings.FloatAmplitudeX *
                Mathf.Sin(2f * Mathf.PI * settings.FloatFrequency3 * time + 1.2f);
            return new Vector3(x, y, 0f) * blend;
        }

        private static float EvaluateFloatingTilt(GlideLocomotionSettings settings, float time, float blend)
        {
            var tilt = settings.TiltAmplitudeDeg *
                Mathf.Sin(2f * Mathf.PI * settings.TiltFrequency * time);
            return tilt * blend;
        }

        private static (float Value, float Velocity) StepSpring(float value, float velocity, float target, float deltaTime)
        {
            velocity += (target - value) * 26f * deltaTime;
            velocity *= Mathf.Exp(-7f * deltaTime);
            value += velocity * deltaTime;
            return (value, velocity);
        }

        private static float EvaluatePassiveDragHorizontalSway(float time)
        {
            return 0.16f * (
                0.65f * Mathf.Sin(2f * Mathf.PI * 1.35f * time) +
                0.35f * Mathf.Sin(2f * Mathf.PI * 2.15f * time + 1.2f));
        }

        private static float EvaluatePassiveDragHangSway(float time)
        {
            return 0.12f * (
                0.60f * Mathf.Sin(2f * Mathf.PI * 1.65f * time + 0.9f) +
                0.40f * Mathf.Sin(2f * Mathf.PI * 2.45f * time + 2.1f));
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

        private static TValue GetPrivateField<TTarget, TValue>(TTarget target, string fieldName)
        {
            var field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(TTarget).FullName, fieldName);
            }

            return (TValue)field.GetValue(target);
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
