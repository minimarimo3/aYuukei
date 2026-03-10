using NUnit.Framework;
using UnityEngine;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class GlideLocomotionControllerTests
    {
        [Test]
        public void Step_WhenHoldingPosition_RemainsIdleAndKeepsLogicalPosition()
        {
            var controller = new GlideLocomotionController(new GlideLocomotionSettings());
            controller.SnapTo(Vector2.zero);

            GlideLocomotionFrame frame = default;
            for (var i = 0; i < 10; i++)
            {
                frame = controller.Step(0.1f, Vector2.zero, true, 0f);
            }

            Assert.That(frame.State, Is.EqualTo(MascotLocomotionState.IdleHover));
            Assert.That(frame.LogicalPosition.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(frame.LogicalPosition.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(frame.LogicalVelocity.magnitude, Is.EqualTo(0f).Within(0.001f));
            Assert.That(Mathf.Abs(frame.VisualOffset.y), Is.GreaterThan(0.001f));
        }

        [Test]
        public void Step_TransitionsFromGlideToApproachAndSettlesAtTarget()
        {
            var controller = new GlideLocomotionController(new GlideLocomotionSettings
            {
                GlideMaxSpeed = 120f,
                Acceleration = 480f,
                Deceleration = 520f,
                SlowRadius = 72f,
                ArrivalRadius = 6f,
                StopSpeed = 4f,
            });

            controller.SnapTo(Vector2.zero);

            var sawGlide = false;
            var sawApproach = false;
            GlideLocomotionFrame frame = default;
            for (var i = 0; i < 200; i++)
            {
                frame = controller.Step(0.05f, new Vector2(180f, 0f), false, 0f);
                sawGlide |= frame.State == MascotLocomotionState.Glide;
                sawApproach |= frame.State == MascotLocomotionState.GlideApproach;
                if (frame.State == MascotLocomotionState.IdleHover && frame.ReachedTarget)
                {
                    break;
                }
            }

            Assert.That(sawGlide, Is.True);
            Assert.That(sawApproach, Is.True);
            Assert.That(frame.State, Is.EqualTo(MascotLocomotionState.IdleHover));
            Assert.That(frame.ReachedTarget, Is.True);
            Assert.That(frame.LogicalPosition.x, Is.EqualTo(180f).Within(0.5f));
            Assert.That(frame.LogicalVelocity.magnitude, Is.LessThan(0.1f));
        }

        [Test]
        public void Step_WithHighBusyScore_ReducesVelocityMagnitude()
        {
            var relaxedController = new GlideLocomotionController(new GlideLocomotionSettings
            {
                GlideMaxSpeed = 200f,
                Acceleration = 1000f,
                Deceleration = 1000f,
            });
            var busyController = new GlideLocomotionController(new GlideLocomotionSettings
            {
                GlideMaxSpeed = 200f,
                Acceleration = 1000f,
                Deceleration = 1000f,
                BusySpeedMultiplier = 0.3f,
            });

            relaxedController.SnapTo(Vector2.zero);
            busyController.SnapTo(Vector2.zero);

            var relaxedFrame = relaxedController.Step(0.25f, new Vector2(800f, 0f), false, 0f);
            var busyFrame = busyController.Step(0.25f, new Vector2(800f, 0f), false, 1f);

            Assert.That(busyFrame.LogicalVelocity.magnitude, Is.LessThan(relaxedFrame.LogicalVelocity.magnitude));
        }

        [Test]
        public void Step_WhenMovingHorizontally_AddsHorizontalSwayAndTilt()
        {
            var controller = new GlideLocomotionController(new GlideLocomotionSettings
            {
                GlideMaxSpeed = 180f,
                Acceleration = 900f,
                Deceleration = 900f,
                GlideSwayAmplitude = 0.2f,
                TiltAmount = 10f,
            });

            controller.SnapTo(Vector2.zero);

            var frame = controller.Step(0.1f, new Vector2(300f, 0f), false, 0f);
            var tiltedUp = frame.VisualRotation * Vector3.up;

            Assert.That(frame.State, Is.EqualTo(MascotLocomotionState.Glide));
            Assert.That(Mathf.Abs(frame.VisualOffset.x), Is.GreaterThan(0.001f));
            Assert.That(Mathf.Abs(tiltedUp.x), Is.GreaterThan(0.001f));
        }
    }
}
