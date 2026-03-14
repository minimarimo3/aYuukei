using NUnit.Framework;
using UnityEngine;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class StrokeGestureRecognizerTests
    {
        [Test]
        public void TrySample_FiresDiscreteStrokeEventForHeadBackAndForth()
        {
            var recognizer = new StrokeGestureRecognizer();

            Assert.That(recognizer.TrySample(new Vector2(100f, 100f), 0f, true, out _), Is.False);
            Assert.That(recognizer.TrySample(new Vector2(154f, 102f), 0.2f, true, out _), Is.False);

            Assert.That(recognizer.TrySample(new Vector2(92f, 100f), 0.4f, true, out var detection), Is.True);
            Assert.That(detection.Position, Is.EqualTo(new Vector2(92f, 100f)));
            Assert.That(detection.StrokeCount, Is.EqualTo(2));
            Assert.That(detection.StrokeDirection, Is.EqualTo("mixed"));
            Assert.That(detection.StrokeSpeed, Is.EqualTo(290f).Within(0.001f));
        }

        [Test]
        public void TrySample_RespectsCooldownAndSuppression()
        {
            var recognizer = new StrokeGestureRecognizer();

            recognizer.TrySample(new Vector2(100f, 100f), 0f, true, out _);
            recognizer.TrySample(new Vector2(154f, 100f), 0.2f, true, out _);
            Assert.That(recognizer.TrySample(new Vector2(92f, 100f), 0.4f, true, out _), Is.True);

            Assert.That(recognizer.TrySample(new Vector2(100f, 100f), 0.5f, true, out _), Is.False);
            Assert.That(recognizer.TrySample(new Vector2(154f, 100f), 0.7f, true, out _), Is.False);

            recognizer.SuppressUntil(1.6f);
            Assert.That(recognizer.TrySample(new Vector2(100f, 100f), 1.5f, true, out _), Is.False);
            Assert.That(recognizer.TrySample(new Vector2(100f, 100f), 1.7f, true, out _), Is.False);
            Assert.That(recognizer.TrySample(new Vector2(154f, 100f), 1.9f, true, out _), Is.False);
            Assert.That(recognizer.TrySample(new Vector2(92f, 100f), 2.1f, true, out var detection), Is.True);
            Assert.That(detection.StrokeCount, Is.EqualTo(2));
        }

        [Test]
        public void TrySample_ResetsAfterLeavingHeadPastGraceWindow()
        {
            var recognizer = new StrokeGestureRecognizer();

            recognizer.TrySample(new Vector2(100f, 100f), 0f, true, out _);
            recognizer.TrySample(new Vector2(154f, 100f), 0.2f, true, out _);

            Assert.That(recognizer.TrySample(new Vector2(154f, 120f), 0.4f, false, out _), Is.False);
            Assert.That(recognizer.TrySample(new Vector2(92f, 100f), 0.5f, true, out _), Is.False);
        }
    }
}
