using UnityEngine;

namespace Yuukei.Runtime
{
    internal readonly struct StrokeGestureDetection
    {
        public StrokeGestureDetection(Vector2 position, int strokeCount, float strokeSpeed, string strokeDirection)
        {
            Position = position;
            StrokeCount = strokeCount;
            StrokeSpeed = strokeSpeed;
            StrokeDirection = strokeDirection;
        }

        public Vector2 Position { get; }
        public int StrokeCount { get; }
        public float StrokeSpeed { get; }
        public string StrokeDirection { get; }
    }

    internal sealed class StrokeGestureRecognizer
    {
        internal const float SampleNoiseDistancePixels = 4f;
        internal const float MinimumHorizontalSegmentPixels = 18f;
        internal const float HorizontalDominanceRatio = 1.35f;
        internal const float GestureWindowSeconds = 0.90f;
        internal const float HeadLeaveGraceSeconds = 0.12f;
        internal const float MinimumAccumulatedHorizontalPixels = 96f;
        internal const float CooldownSeconds = 0.90f;

        private bool _trackingActive;
        private Vector2 _lastSamplePosition;
        private float _trackingStartedAt = float.MinValue;
        private float _lastSampleAt = float.MinValue;
        private float _lastHeadObservedAt = float.MinValue;
        private float _cooldownUntil = float.MinValue;
        private float _suppressedUntil = float.MinValue;
        private int _strokeCount;
        private float _accumulatedHorizontalDistance;
        private float _netHorizontalDistance;
        private int _currentSegmentDirection;

        public void Reset()
        {
            _trackingActive = false;
            _lastSamplePosition = default;
            _trackingStartedAt = float.MinValue;
            _lastSampleAt = float.MinValue;
            _lastHeadObservedAt = float.MinValue;
            _strokeCount = 0;
            _accumulatedHorizontalDistance = 0f;
            _netHorizontalDistance = 0f;
            _currentSegmentDirection = 0;
        }

        public void SuppressUntil(float timestamp)
        {
            _suppressedUntil = Mathf.Max(_suppressedUntil, timestamp);
            Reset();
        }

        public bool TrySample(Vector2 position, float timestamp, bool isOnHead, out StrokeGestureDetection detection)
        {
            detection = default;

            if (timestamp <= _suppressedUntil || timestamp <= _cooldownUntil)
            {
                Reset();
                return false;
            }

            if (!isOnHead)
            {
                if (_trackingActive && timestamp - _lastHeadObservedAt > HeadLeaveGraceSeconds)
                {
                    Reset();
                }

                return false;
            }

            if (!_trackingActive || timestamp - _trackingStartedAt > GestureWindowSeconds || timestamp - _lastSampleAt > GestureWindowSeconds)
            {
                StartTracking(position, timestamp);
                return false;
            }

            _lastHeadObservedAt = timestamp;

            var delta = position - _lastSamplePosition;
            _lastSamplePosition = position;
            _lastSampleAt = timestamp;

            if (delta.magnitude < SampleNoiseDistancePixels)
            {
                return false;
            }

            var absDx = Mathf.Abs(delta.x);
            var absDy = Mathf.Abs(delta.y);
            if (absDx < MinimumHorizontalSegmentPixels || absDx < absDy * HorizontalDominanceRatio)
            {
                return false;
            }

            var segmentDirection = delta.x >= 0f ? 1 : -1;
            if (_currentSegmentDirection == 0 || segmentDirection != _currentSegmentDirection)
            {
                _strokeCount++;
                _currentSegmentDirection = segmentDirection;
            }

            _accumulatedHorizontalDistance += absDx;
            _netHorizontalDistance += delta.x;

            if (_strokeCount < 2 || _accumulatedHorizontalDistance < MinimumAccumulatedHorizontalPixels)
            {
                return false;
            }

            var elapsed = Mathf.Max(timestamp - _trackingStartedAt, 0.01f);
            detection = new StrokeGestureDetection(
                position,
                _strokeCount,
                _accumulatedHorizontalDistance / elapsed,
                ResolveDirection());

            _cooldownUntil = timestamp + CooldownSeconds;
            Reset();
            return true;
        }

        private void StartTracking(Vector2 position, float timestamp)
        {
            Reset();
            _trackingActive = true;
            _lastSamplePosition = position;
            _trackingStartedAt = timestamp;
            _lastSampleAt = timestamp;
            _lastHeadObservedAt = timestamp;
        }

        private string ResolveDirection()
        {
            if (_accumulatedHorizontalDistance <= Mathf.Epsilon)
            {
                return "mixed";
            }

            if (Mathf.Abs(_netHorizontalDistance) >= _accumulatedHorizontalDistance * 0.55f)
            {
                return _netHorizontalDistance >= 0f ? "right" : "left";
            }

            return "mixed";
        }
    }
}
