using System;
using UnityEngine;

namespace Yuukei.Runtime
{
    [Serializable]
    public sealed class GlideLocomotionSettings
    {
        [Header("Idle Floating")]
        [Min(0f)] public float FloatAmplitudeY = 0.06f;
        [Min(0.1f)] public float FloatFrequency1 = 0.55f;
        [Min(0.1f)] public float FloatFrequency2 = 1.10f;
        [Min(0.1f)] public float FloatFrequency3 = 0.28f;
        [Min(0f)] public float FloatAmplitudeX = 0.018f;
        [Min(0f)] public float TiltAmplitudeDeg = 1.8f;
        [Min(0.1f)] public float TiltFrequency = 0.40f;

        public GlideLocomotionSettings Clone()
        {
            return new GlideLocomotionSettings
            {
                FloatAmplitudeY = FloatAmplitudeY,
                FloatFrequency1 = FloatFrequency1,
                FloatFrequency2 = FloatFrequency2,
                FloatFrequency3 = FloatFrequency3,
                FloatAmplitudeX = FloatAmplitudeX,
                TiltAmplitudeDeg = TiltAmplitudeDeg,
                TiltFrequency = TiltFrequency,
            };
        }
    }
}
