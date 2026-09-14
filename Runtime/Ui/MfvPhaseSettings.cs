using System;
using UnityEngine;

namespace ManeuverForVRC.Ui
{
    /// <summary>
    /// Shared settings: the phase side of the model. Turns a fixture's normalized position
    /// p_i and the clip time into a phase φ_i(t), and parameters then look their own value
    /// up with that phase. The same panel is reused verbatim when a single parameter
    /// opts out with own phase.
    /// </summary>
    [Serializable]
    public class MfvPhaseSettings
    {
        public MfvPhaseMode mode = MfvPhaseMode.PingPong;

        /// <summary>Easing function. Ignored while <see cref="mode"/> is Random.</summary>
        public MfvEaseType ease = MfvEaseType.InOutCubic;

        /// <summary>Ping-pong ratio: where the triangle wave peaks. PingPong only.</summary>
        [Range(0f, 1f)] public float pingPongRatio = 0.65f;

        /// <summary>Fixture group size: how many neighbouring fixtures share one phase.</summary>
        [Min(1)] public int fixtureGroupSize = 1;

        /// <summary>Delay: phase offset between adjacent fixture groups.</summary>
        public float delay = 0f;

        /// <summary>Speed in beats per cycle.</summary>
        [Min(0f)] public float beatsPerCycle = 2f;

        /// <summary>Invert. Ignored while <see cref="mode"/> is Random.</summary>
        public bool inverse;

        public MfvPhaseSettings() { }

        public MfvPhaseSettings(MfvPhaseSettings other)
        {
            mode = other.mode;
            ease = other.ease;
            pingPongRatio = other.pingPongRatio;
            fixtureGroupSize = other.fixtureGroupSize;
            delay = other.delay;
            beatsPerCycle = other.beatsPerCycle;
            inverse = other.inverse;
        }

        /// <summary>
        /// Phase for the fixture at <paramref name="orderIndex"/> at normalized clip
        /// time <paramref name="normalizedTime"/>. Result is in 0..1.
        /// </summary>
        public float Evaluate(float normalizedTime, int orderIndex)
        {
            var group = Mathf.Max(1, fixtureGroupSize);
            var groupIndex = Mathf.Max(0, orderIndex) / group;
            var t = normalizedTime - delay * groupIndex;
            t -= Mathf.Floor(t);

            switch (mode)
            {
                case MfvPhaseMode.PingPong:
                    t = EvaluatePingPong(t);
                    break;
                case MfvPhaseMode.Random:
                    // Smooth wander: a per-fixture seeded noise walk inside the cycle.
                    return Mathf.PerlinNoise(t * 2f, (groupIndex + 1) * 7.31f);
                default:
                    break;
            }

            if (inverse)
            {
                t = 1f - t;
            }

            return MfvEase.Evaluate(ease, t);
        }

        private float EvaluatePingPong(float t)
        {
            var peak = Mathf.Clamp(pingPongRatio, 0.001f, 0.999f);
            return t < peak ? t / peak : 1f - (t - peak) / (1f - peak);
        }

        /// <summary>True while the phase is on the return leg of a ping-pong cycle.</summary>
        public bool IsOnReturnLeg(float normalizedTime, int orderIndex)
        {
            if (mode != MfvPhaseMode.PingPong)
            {
                return false;
            }

            var group = Mathf.Max(1, fixtureGroupSize);
            var groupIndex = Mathf.Max(0, orderIndex) / group;
            var t = normalizedTime - delay * groupIndex;
            t -= Mathf.Floor(t);
            return t >= Mathf.Clamp(pingPongRatio, 0.001f, 0.999f);
        }
    }
}
