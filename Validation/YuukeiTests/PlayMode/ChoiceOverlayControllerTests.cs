using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Yuukei.Runtime;

namespace Yuukei.Tests.PlayMode
{
    public sealed class ChoiceOverlayControllerTests
    {
        [UnityTest]
        public IEnumerator CancelCurrent_ReturnsEmptyString()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var root = new GameObject("ChoiceOverlayTestRoot");
                var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(root.transform, false);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var controller = root.AddComponent<ChoiceOverlayController>();
                controller.Initialize(canvas);

                var task = controller.ShowChoicesAsync(new[] { "A", "B" }, CancellationToken.None);
                await UniTask.Yield();
                controller.CancelCurrent();
                var result = await task;

                Assert.That(result, Is.EqualTo(string.Empty));
                Object.Destroy(root);
            });
        }
    }
}
