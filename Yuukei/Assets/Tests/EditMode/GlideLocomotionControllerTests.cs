using NUnit.Framework;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class GlideLocomotionControllerTests
    {
        [Test]
        public void Defaults_MatchFloatingMotionAlgorithm()
        {
            var settings = new GlideLocomotionSettings();

            Assert.That(settings.FloatAmplitudeY, Is.EqualTo(0.06f).Within(0.0001f));
            Assert.That(settings.FloatFrequency1, Is.EqualTo(0.55f).Within(0.0001f));
            Assert.That(settings.FloatFrequency2, Is.EqualTo(1.10f).Within(0.0001f));
            Assert.That(settings.FloatFrequency3, Is.EqualTo(0.28f).Within(0.0001f));
            Assert.That(settings.FloatAmplitudeX, Is.EqualTo(0.018f).Within(0.0001f));
            Assert.That(settings.TiltAmplitudeDeg, Is.EqualTo(1.8f).Within(0.0001f));
            Assert.That(settings.TiltFrequency, Is.EqualTo(0.40f).Within(0.0001f));
        }

        [Test]
        public void Clone_CopiesFloatingParameters()
        {
            var source = new GlideLocomotionSettings
            {
                FloatAmplitudeY = 0.12f,
                FloatFrequency1 = 0.41f,
                FloatFrequency2 = 0.92f,
                FloatFrequency3 = 0.25f,
                FloatAmplitudeX = 0.023f,
                TiltAmplitudeDeg = 2.4f,
                TiltFrequency = 0.33f,
            };

            var clone = source.Clone();

            Assert.That(clone, Is.Not.SameAs(source));
            Assert.That(clone.FloatAmplitudeY, Is.EqualTo(source.FloatAmplitudeY));
            Assert.That(clone.FloatFrequency1, Is.EqualTo(source.FloatFrequency1));
            Assert.That(clone.FloatFrequency2, Is.EqualTo(source.FloatFrequency2));
            Assert.That(clone.FloatFrequency3, Is.EqualTo(source.FloatFrequency3));
            Assert.That(clone.FloatAmplitudeX, Is.EqualTo(source.FloatAmplitudeX));
            Assert.That(clone.TiltAmplitudeDeg, Is.EqualTo(source.TiltAmplitudeDeg));
            Assert.That(clone.TiltFrequency, Is.EqualTo(source.TiltFrequency));
        }
    }
}
