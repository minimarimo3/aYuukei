using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Yuukei.Runtime;

namespace Yuukei.Tests.PlayMode
{
    public sealed class SpeechBubbleControllerTests
    {
        private const string StarterPackageFolder = "yuukei-v0.0.1-0f479418-2a7a-4c1d-93d8-b6cf7af6bfc0";

        [Test]
        public void ShowImmediate_UsesCompactBodyWidth()
        {
            var rig = CreateRig();
            try
            {
                rig.Anchor.WorldPosition = Vector3.zero;
                rig.Controller.ShowImmediate("こんにちは", autoHideSeconds: 0f);
                ForceUiUpdate();

                var body = GetPrivateField<RectTransform>(rig.Controller, "_body");
                var canvasRect = (RectTransform)rig.Canvas.transform;
                var maxWidth = ResolveCanvasWidth(canvasRect) * 0.25f;

                Assert.That(body.sizeDelta.x, Is.LessThan(340f));
                Assert.That(body.sizeDelta.x, Is.LessThanOrEqualTo(maxWidth + 0.5f));
            }
            finally
            {
                DestroyRig(rig);
            }
        }

        [Test]
        public void LongText_WrapsInsteadOfGrowingWide()
        {
            var rig = CreateRig();
            try
            {
                rig.Anchor.WorldPosition = Vector3.zero;
                rig.Controller.ShowImmediate("短文", autoHideSeconds: 0f);
                ForceUiUpdate();
                var body = GetPrivateField<RectTransform>(rig.Controller, "_body");
                var shortHeight = body.sizeDelta.y;

                rig.Controller.ShowImmediate("これはかなり長めの文章です。吹き出しの横幅が伸びすぎず、高さ方向へ自然に伸びることを確認します。", autoHideSeconds: 0f);
                ForceUiUpdate();
                var canvasRect = (RectTransform)rig.Canvas.transform;
                var maxWidth = ResolveCanvasWidth(canvasRect) * 0.25f;

                Assert.That(body.sizeDelta.x, Is.LessThanOrEqualTo(maxWidth + 0.5f));
                Assert.That(body.sizeDelta.y, Is.GreaterThan(shortHeight));
            }
            finally
            {
                DestroyRig(rig);
            }
        }

        [Test]
        public void OffscreenAnchor_ClampsBodyInsideCanvas_AndSlidesTail()
        {
            var rig = CreateRig();
            try
            {
                rig.Controller.ApplyTheme(GetStarterTexturePath("speech_bubble_bg.png"), GetStarterTexturePath("speech_bubble_tail.png"));
                rig.Anchor.WorldPosition = new Vector3(-999f, 999f, 0f);
                rig.Controller.ShowImmediate("端寄せテスト", autoHideSeconds: 0f);
                ForceUiUpdate();

                var root = GetPrivateField<RectTransform>(rig.Controller, "_root");
                var body = GetPrivateField<RectTransform>(rig.Controller, "_body");
                var tailRect = GetPrivateField<RectTransform>(rig.Controller, "_tailRect");
                var canvasRect = (RectTransform)rig.Canvas.transform;
                var safeRect = canvasRect.rect;
                safeRect.xMin += 12f;
                safeRect.xMax -= 12f;
                safeRect.yMin += 12f;
                safeRect.yMax -= 12f;

                var left = root.anchoredPosition.x - (body.sizeDelta.x * 0.5f);
                var right = root.anchoredPosition.x + (body.sizeDelta.x * 0.5f);
                var bottom = root.anchoredPosition.y;
                var top = root.anchoredPosition.y + body.sizeDelta.y;

                Assert.That(left, Is.GreaterThanOrEqualTo(safeRect.xMin - 0.5f));
                Assert.That(right, Is.LessThanOrEqualTo(safeRect.xMax + 0.5f));
                Assert.That(bottom, Is.GreaterThanOrEqualTo(safeRect.yMin - 0.5f));
                Assert.That(top, Is.LessThanOrEqualTo(safeRect.yMax + 0.5f));
                Assert.That(tailRect.anchoredPosition.x, Is.LessThan(0f));
            }
            finally
            {
                DestroyRig(rig);
            }
        }

        [Test]
        public void ApplyTheme_ResetsTint_AndUsesNaturalTailPresentation()
        {
            var rig = CreateRig();
            try
            {
                rig.Controller.ApplyTheme(GetStarterTexturePath("speech_bubble_bg.png"), GetStarterTexturePath("speech_bubble_tail.png"));
                rig.Anchor.WorldPosition = Vector3.zero;
                rig.Controller.ShowImmediate("theme", autoHideSeconds: 0f);
                ForceUiUpdate();

                var background = GetPrivateField<Image>(rig.Controller, "_background");
                var backgroundRect = GetPrivateField<RectTransform>(rig.Controller, "_backgroundRect");
                var body = GetPrivateField<RectTransform>(rig.Controller, "_body");
                var tail = GetPrivateField<Image>(rig.Controller, "_tail");
                var tailRect = GetPrivateField<RectTransform>(rig.Controller, "_tailRect");
                var backgroundAspect = backgroundRect.sizeDelta.x / backgroundRect.sizeDelta.y;

                Assert.That(background.color, Is.EqualTo(Color.white));
                Assert.That(background.preserveAspect, Is.True);
                Assert.That(backgroundAspect, Is.EqualTo(1f).Within(0.01f));
                Assert.That(backgroundRect.sizeDelta.x, Is.GreaterThanOrEqualTo(body.sizeDelta.x - 0.5f));
                Assert.That(backgroundRect.sizeDelta.y, Is.GreaterThanOrEqualTo(body.sizeDelta.y - 0.5f));
                Assert.That(tail.color, Is.EqualTo(Color.white));
                Assert.That(tail.enabled, Is.True);
                Assert.That(tail.type, Is.EqualTo(Image.Type.Simple));
                Assert.That(tail.preserveAspect, Is.True);
                Assert.That(Quaternion.Angle(tailRect.localRotation, Quaternion.identity), Is.LessThan(0.01f));
            }
            finally
            {
                DestroyRig(rig);
            }
        }

        [Test]
        public void TextStyle_DefaultsTo24PtBlack_AndCanBeOverriddenExternally()
        {
            var rig = CreateRig();
            try
            {
                var label = GetPrivateField<Component>(rig.Controller, "_label");
                var initialStyle = rig.Controller.GetTextStyle();

                Assert.That(initialStyle.FontSize, Is.EqualTo(24f));
                Assert.That(initialStyle.TextColor, Is.EqualTo(Color.black));
                Assert.That(GetFloatProperty(label, "fontSize"), Is.EqualTo(24f));
                Assert.That(GetColorProperty(label, "color"), Is.EqualTo(Color.black));

                rig.Controller.ApplyTextStyle(new SpeechBubbleTextStyle
                {
                    FontSize = 30f,
                    TextColor = Color.red,
                });
                rig.Controller.ShowImmediate("style", autoHideSeconds: 0f);
                ForceUiUpdate();

                var updatedStyle = rig.Controller.GetTextStyle();
                Assert.That(updatedStyle.FontSize, Is.EqualTo(30f));
                Assert.That(updatedStyle.TextColor, Is.EqualTo(Color.red));
                Assert.That(GetFloatProperty(label, "fontSize"), Is.EqualTo(30f));
                Assert.That(GetColorProperty(label, "color"), Is.EqualTo(Color.red));
            }
            finally
            {
                DestroyRig(rig);
            }
        }

        private static void ForceUiUpdate()
        {
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();
        }

        private static float ResolveCanvasWidth(RectTransform canvasRect)
        {
            return canvasRect.rect.width > 0f ? canvasRect.rect.width : Screen.width;
        }

        private static string GetStarterTexturePath(string fileName)
        {
            return Path.Combine(
                Application.dataPath,
                "StreamingAssets",
                "StarterPackages",
                StarterPackageFolder,
                "Textures",
                fileName);
        }

        private static T GetPrivateField<T>(object target, string fieldName) where T : class
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
            var value = field.GetValue(target) as T;
            Assert.That(value, Is.Not.Null, $"Field '{fieldName}' was null.");
            return value;
        }

        private static float GetFloatProperty(Component component, string propertyName)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, $"Property '{propertyName}' was not found.");
            return (float)property.GetValue(component);
        }

        private static Color GetColorProperty(Component component, string propertyName)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, $"Property '{propertyName}' was not found.");
            return (Color)property.GetValue(component);
        }

        private static void DestroyRig(TestRig rig)
        {
            if (rig.Root != null)
            {
                Object.Destroy(rig.Root);
            }
        }

        private static TestRig CreateRig()
        {
            var anchor = new AnchorState();
            var root = new GameObject("SpeechBubbleTestRoot");

            var cameraObject = new GameObject("SpeechBubbleCamera", typeof(Camera));
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;

            var canvasObject = new GameObject("SpeechBubbleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var controller = root.AddComponent<SpeechBubbleController>();
            controller.Initialize(canvas, camera, () => anchor.WorldPosition);
            return new TestRig(root, canvas, controller, anchor);
        }

        private sealed class TestRig
        {
            public TestRig(GameObject root, Canvas canvas, SpeechBubbleController controller, AnchorState anchor)
            {
                Root = root;
                Canvas = canvas;
                Controller = controller;
                Anchor = anchor;
            }

            public GameObject Root { get; }
            public Canvas Canvas { get; }
            public SpeechBubbleController Controller { get; }
            public AnchorState Anchor { get; }
        }

        private sealed class AnchorState
        {
            public Vector3 WorldPosition;
        }
    }
}
