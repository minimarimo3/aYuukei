using System;
using UnityEngine;

namespace Yuukei.Runtime
{
    public enum MascotLocomotionState
    {
        IdleHover,
        Glide,
        GlideApproach,
    }

    [Serializable]
    public sealed class GlideLocomotionSettings
    {
        [Header("Movement")]
        [Min(10f)] public float GlideMaxSpeed = 220f;
        [Min(10f)] public float Acceleration = 320f;
        [Min(10f)] public float Deceleration = 420f;
        [Min(24f)] public float SlowRadius = 180f;
        [Min(4f)] public float ArrivalRadius = 28f;
        [Min(0.1f)] public float StopSpeed = 16f;

        [Header("Float Feel")]
        [Min(0f)] public float HoverBobAmplitude = 0.18f;
        [Min(0.1f)] public float HoverBobFrequency = 1.1f;
        [Min(0f)] public float GlideSwayAmplitude = 0.12f;
        [Min(0.1f)] public float GlideSwayFrequency = 0.75f;
        [Min(0f)] public float TiltAmount = 9f;
        [Min(0.1f)] public float VisualFollowSpeed = 8f;
        [Min(0f)] public float HoverPauseMinSeconds = 1.1f;
        [Min(0f)] public float HoverPauseMaxSeconds = 2.4f;

        [Header("Busy Response")]
        [Range(0.2f, 1f)] public float BusySpeedMultiplier = 0.35f;
        [Range(0.2f, 1f)] public float BusyTravelMultiplier = 0.55f;

        public GlideLocomotionSettings Clone()
        {
            return new GlideLocomotionSettings
            {
                GlideMaxSpeed = GlideMaxSpeed,
                Acceleration = Acceleration,
                Deceleration = Deceleration,
                SlowRadius = SlowRadius,
                ArrivalRadius = ArrivalRadius,
                StopSpeed = StopSpeed,
                HoverBobAmplitude = HoverBobAmplitude,
                HoverBobFrequency = HoverBobFrequency,
                GlideSwayAmplitude = GlideSwayAmplitude,
                GlideSwayFrequency = GlideSwayFrequency,
                TiltAmount = TiltAmount,
                VisualFollowSpeed = VisualFollowSpeed,
                HoverPauseMinSeconds = HoverPauseMinSeconds,
                HoverPauseMaxSeconds = HoverPauseMaxSeconds,
                BusySpeedMultiplier = BusySpeedMultiplier,
                BusyTravelMultiplier = BusyTravelMultiplier,
            };
        }
    }

    internal readonly struct GlideLocomotionFrame
    {
        public GlideLocomotionFrame(
            Vector2 logicalPosition,
            Vector2 logicalVelocity,
            Vector3 visualOffset,
            Quaternion visualRotation,
            MascotLocomotionState state,
            bool reachedTarget,
            float remainingDistance)
        {
            LogicalPosition = logicalPosition;
            LogicalVelocity = logicalVelocity;
            VisualOffset = visualOffset;
            VisualRotation = visualRotation;
            State = state;
            ReachedTarget = reachedTarget;
            RemainingDistance = remainingDistance;
        }

        public Vector2 LogicalPosition { get; }
        public Vector2 LogicalVelocity { get; }
        public Vector3 VisualOffset { get; }
        public Quaternion VisualRotation { get; }
        public MascotLocomotionState State { get; }
        public bool ReachedTarget { get; }
        public float RemainingDistance { get; }
    }

    internal sealed class GlideLocomotionController
    {
        private GlideLocomotionSettings _settings;
        private float _phase;
        private Vector2 _lastHeading = Vector2.right;

        public GlideLocomotionController(GlideLocomotionSettings settings = null)
        {
            ApplySettings(settings);
        }

        public Vector2 LogicalPosition { get; private set; }
        public Vector2 LogicalVelocity { get; private set; }
        public MascotLocomotionState State { get; private set; } = MascotLocomotionState.IdleHover;
        public bool HasPosition { get; private set; }
        public GlideLocomotionSettings Settings => _settings;

        public void ApplySettings(GlideLocomotionSettings settings)
        {
            _settings = settings?.Clone() ?? new GlideLocomotionSettings();
        }

        public void SnapTo(Vector2 position)
        {
            LogicalPosition = position;
            LogicalVelocity = Vector2.zero;
            HasPosition = true;
            State = MascotLocomotionState.IdleHover;
        }

        public void MoveBy(Vector2 delta)
        {
            if (!HasPosition)
            {
                SnapTo(Vector2.zero);
            }

            LogicalPosition += delta;
            LogicalVelocity = Vector2.zero;
            State = MascotLocomotionState.IdleHover;
        }

        public GlideLocomotionFrame Step(float deltaTime, Vector2 targetPosition, bool holdPosition, float busyScore)
        {
            if (!HasPosition)
            {
                SnapTo(targetPosition);
            }

            if (deltaTime < 0f)
            {
                deltaTime = 0f;
            }

            var clampedBusy = Mathf.Clamp01(busyScore);
            var effectiveMaxSpeed = Mathf.Lerp(_settings.GlideMaxSpeed, _settings.GlideMaxSpeed * _settings.BusySpeedMultiplier, clampedBusy);
            var effectiveSlowRadius = Mathf.Max(_settings.ArrivalRadius + 1f, _settings.SlowRadius * Mathf.Lerp(1f, 0.8f, clampedBusy));
            var effectiveStopSpeed = Mathf.Max(_settings.StopSpeed, effectiveMaxSpeed * 0.08f);

            var toTarget = targetPosition - LogicalPosition;
            var remainingDistance = toTarget.magnitude;
            var desiredVelocity = Vector2.zero;

            if (!holdPosition && remainingDistance > 0.001f)
            {
                var desiredSpeed = effectiveMaxSpeed;
                if (remainingDistance < effectiveSlowRadius)
                {
                    desiredSpeed *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(remainingDistance / effectiveSlowRadius));
                }

                desiredVelocity = toTarget / remainingDistance * desiredSpeed;
            }

            var accel = desiredVelocity.sqrMagnitude > LogicalVelocity.sqrMagnitude + 0.01f
                ? _settings.Acceleration
                : _settings.Deceleration;
            LogicalVelocity = Vector2.MoveTowards(LogicalVelocity, desiredVelocity, accel * deltaTime);
            LogicalPosition += LogicalVelocity * deltaTime;

            toTarget = targetPosition - LogicalPosition;
            remainingDistance = toTarget.magnitude;

            if (holdPosition)
            {
                LogicalVelocity = Vector2.MoveTowards(LogicalVelocity, Vector2.zero, _settings.Deceleration * deltaTime);
                if (LogicalVelocity.magnitude <= effectiveStopSpeed)
                {
                    LogicalVelocity = Vector2.zero;
                }

                State = MascotLocomotionState.IdleHover;
            }
            else if (remainingDistance <= _settings.ArrivalRadius)
            {
                LogicalVelocity = Vector2.MoveTowards(LogicalVelocity, Vector2.zero, _settings.Deceleration * deltaTime);
                if (LogicalVelocity.magnitude <= effectiveStopSpeed)
                {
                    LogicalPosition = targetPosition;
                    LogicalVelocity = Vector2.zero;
                    remainingDistance = 0f;
                    State = MascotLocomotionState.IdleHover;
                }
                else
                {
                    State = MascotLocomotionState.GlideApproach;
                }
            }
            else
            {
                State = remainingDistance <= effectiveSlowRadius
                    ? MascotLocomotionState.GlideApproach
                    : MascotLocomotionState.Glide;
            }

            if (LogicalVelocity.sqrMagnitude > 0.25f)
            {
                _lastHeading = LogicalVelocity.normalized;
            }

            if (deltaTime > 0f)
            {
                var phaseRate = 1f + Mathf.Clamp01(LogicalVelocity.magnitude / Mathf.Max(1f, effectiveMaxSpeed));
                _phase += deltaTime * phaseRate;
            }

            var normalizedVelocity = effectiveMaxSpeed > 0.01f
                ? LogicalVelocity / effectiveMaxSpeed
                : Vector2.zero;
            var bobBlend = State == MascotLocomotionState.IdleHover ? 1f : 0.9f;
            var swayBlend = State == MascotLocomotionState.IdleHover ? 0.55f : 1f;
            var bob = EvaluateLayeredWave(_phase, _settings.HoverBobFrequency) * _settings.HoverBobAmplitude * bobBlend;
            var sway = EvaluateLayeredWave(_phase + 1.17f, _settings.GlideSwayFrequency) * _settings.GlideSwayAmplitude * swayBlend;
            var verticalDrift = EvaluateLayeredWave(_phase + 2.41f, _settings.GlideSwayFrequency * 0.61f) * (_settings.GlideSwayAmplitude * 0.2f) * swayBlend;
            var inertialOffset = -normalizedVelocity * (_settings.GlideSwayAmplitude * 0.35f);
            var visualOffset = new Vector3(
                sway + inertialOffset.x,
                bob + verticalDrift + (inertialOffset.y * 0.5f),
                0f);

            var pitch = Mathf.Clamp(-normalizedVelocity.y * _settings.TiltAmount * 0.45f, -_settings.TiltAmount, _settings.TiltAmount);
            var roll = Mathf.Clamp(-normalizedVelocity.x * _settings.TiltAmount, -_settings.TiltAmount, _settings.TiltAmount);
            var visualRotation = Quaternion.Euler(pitch, 0f, roll);

            return new GlideLocomotionFrame(
                LogicalPosition,
                LogicalVelocity,
                visualOffset,
                visualRotation,
                State,
                State == MascotLocomotionState.IdleHover && remainingDistance <= _settings.ArrivalRadius,
                remainingDistance);
        }

        private static float EvaluateLayeredWave(float time, float frequency)
        {
            var primary = Mathf.Sin(time * frequency * Mathf.PI * 2f);
            var secondary = Mathf.Sin(time * frequency * 0.57f * Mathf.PI * 2f + 1.31f) * 0.35f;
            var tertiary = Mathf.Sin(time * frequency * 1.83f * Mathf.PI * 2f + 0.48f) * 0.12f;
            return primary + secondary + tertiary;
        }
    }
}
