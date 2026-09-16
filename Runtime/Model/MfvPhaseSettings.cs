using System;
using UnityEngine;

namespace ManeuverForVRC
{
    /// <summary>
    /// Shared settings: the phase side of the model. Turns a fixture's normalized position
    /// p_i and the clip time into a phase φ_i(t), and parameters then look their own value
    /// up with that phase. The same panel is reused verbatim when a single parameter
    /// opts out with own phase. <see cref="MfvShowEvaluator.Phase"/> does the math.
    /// </summary>
    [Serializable]
    public class MfvPhaseSettings
    {
        public MfvPhaseMode mode = MfvPhaseMode.PingPong;

        /// <summary>Easing function. Ignored while <see cref="mode"/> is Random.</summary>
        public MfvEaseType ease = MfvEaseType.InOutCubic;

        /// <summary>Ping-pong ratio: where the triangle wave peaks. PingPong only.</summary>
        [Range(0f, 1f)] public float pingPongRatio = 0.5f;

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
    }
}
