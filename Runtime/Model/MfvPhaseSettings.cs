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
        public MfvEaseType ease = MfvEaseType.InOutSine;

        /// <summary>Ping-pong ratio: where the triangle wave peaks. PingPong only.</summary>
        [Range(0f, 1f)] public float pingPongRatio = 0.5f;

        /// <summary>Fixture group size: how many neighbouring fixtures share one phase.</summary>
        [Min(1)] public int fixtureGroupSize = 1;

        /// <summary>
        /// Spread: the phase offset across the whole fixture group, in cycles. 1 walks the
        /// wave over every order position in exactly one cycle, whatever the fixture count,
        /// so the look survives adding or removing fixtures. The compiler divides it by
        /// <see cref="MfvShowEvaluator.SpreadPositions"/> to get the per position delay.
        /// Used while <see cref="spreadInBeats"/> is off.
        /// </summary>
        public float spread = 0f;

        /// <summary>
        /// The same spread in beats, for a show authored against the music: it stays that
        /// many beats when the speed changes, where <see cref="spread"/> stays a share of
        /// the cycle. Used while <see cref="spreadInBeats"/> is on.
        /// </summary>
        public float spreadBeats = 0f;

        /// <summary>Which of the two spreads is authored. The other one is kept, not used.</summary>
        public bool spreadInBeats;

        /// <summary>Speed in beats per cycle.</summary>
        [Min(0f)] public float beatsPerCycle = 2f;

        /// <summary>Invert. Ignored while <see cref="mode"/> is Random.</summary>
        public bool inverse;

        public MfvPhaseSettings() { }

        /// <summary>
        /// True when the spread is read in beats. A speed of zero has no beats to count,
        /// so the spread falls back to its share of the cycle then.
        /// </summary>
        public bool UsesSpreadBeats => spreadInBeats && beatsPerCycle > 0f;

        /// <summary>The spread in cycles, whichever unit it was authored in.</summary>
        public float SpreadCycles => UsesSpreadBeats ? spreadBeats / beatsPerCycle : spread;

        public MfvPhaseSettings(MfvPhaseSettings other)
        {
            mode = other.mode;
            ease = other.ease;
            pingPongRatio = other.pingPongRatio;
            fixtureGroupSize = other.fixtureGroupSize;
            spread = other.spread;
            spreadBeats = other.spreadBeats;
            spreadInBeats = other.spreadInBeats;
            beatsPerCycle = other.beatsPerCycle;
            inverse = other.inverse;
        }
    }
}
