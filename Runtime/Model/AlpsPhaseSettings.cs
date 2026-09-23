using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Shared settings: the phase side of the model. Turns a fixture's normalized position
    /// p_i and the clip time into a phase φ_i(t), and parameters then look their own value
    /// up with that phase. The same panel is reused verbatim when a single parameter
    /// opts out with own phase. <see cref="AlpsPhaseCurve.Phase"/> does the math.
    /// </summary>
    [Serializable]
    public class AlpsPhaseSettings : ISerializationCallbackReceiver
    {
        /// <summary>
        /// The layout <see cref="version"/> marks. Settings saved before version 1 had forward
        /// and ping-pong modes and a step ease. Before version 2 the rise and the fall shared
        /// one ease, the fall running it backwards. <see cref="OnAfterDeserialize"/> converts both.
        /// </summary>
        private const int CurrentVersion = 2;

        /// <summary>The ease list before version 1 still had Step here.</summary>
        private const int LegacyStepEase = 11;

        public AlpsPhaseMode mode = AlpsPhaseMode.Wave;

        /// <summary>
        /// Easing of the rise. Kept under its old name, since JsonUtility cannot follow a
        /// rename. Ignored while <see cref="mode"/> is Random.
        /// </summary>
        public AlpsEaseType ease = AlpsEaseType.InOutSine;

        /// <summary>
        /// Easing of the fall, read forwards in time on the way down, so an Out curve slows
        /// into the bottom. Ignored while <see cref="mode"/> is Random.
        /// </summary>
        public AlpsEaseType fallEase = AlpsEaseType.InOutSine;

        /// <summary>
        /// Distribution: the share of the cycle spent rising from 0 to 1. The four shares
        /// (<see cref="rise"/>, <see cref="holdHigh"/>, <see cref="fall"/> and
        /// <see cref="HoldLow"/>) always add up to one cycle. Wave only.
        /// </summary>
        [FormerlySerializedAs("pingPongRatio")]
        [Range(0f, 1f)] public float rise = 0.5f;

        /// <summary>The share of the cycle held at 1 after the rise. Wave only.</summary>
        [FormerlySerializedAs("pingPongHold")]
        [Range(0f, 1f)] public float holdHigh;

        /// <summary>The share of the cycle spent falling back to 0 after the high hold. Wave only.</summary>
        [Range(0f, 1f)] public float fall = 0.5f;

        /// <summary>The share of the cycle held at 0 until the next one: what the other three leave over.</summary>
        public float HoldLow => Mathf.Max(0f, 1f - rise - holdHigh - fall);

        /// <summary>
        /// Missing from anything saved before <see cref="CurrentVersion"/>, so it reads as 0
        /// there. Every save writes the current one.
        /// </summary>
        [SerializeField, HideInInspector] private int version;

        /// <summary>Fixture group size: how many neighbouring fixtures share one phase.</summary>
        [Min(1)] public int fixtureGroupSize = 1;

        /// <summary>
        /// Spread: the phase offset across the whole fixture group, in cycles. 1 walks the
        /// wave over every order position in exactly one cycle, whatever the fixture count,
        /// so the look survives adding or removing fixtures. The compiler divides it by
        /// <see cref="AlpsShowLayout.SpreadPositions"/> to get the per position delay.
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

        public AlpsPhaseSettings() { }

        /// <summary>
        /// True when the spread is read in beats. A speed of zero has no beats to count,
        /// so the spread falls back to its share of the cycle then.
        /// </summary>
        public bool UsesSpreadBeats => spreadInBeats && beatsPerCycle > 0f;

        /// <summary>The spread in cycles, whichever unit it was authored in.</summary>
        public float SpreadCycles => UsesSpreadBeats ? spreadBeats / beatsPerCycle : spread;

        public AlpsPhaseSettings(AlpsPhaseSettings other)
        {
            mode = other.mode;
            ease = other.ease;
            fallEase = other.fallEase;
            rise = other.rise;
            holdHigh = other.holdHigh;
            fall = other.fall;
            fixtureGroupSize = other.fixtureGroupSize;
            spread = other.spread;
            spreadBeats = other.spreadBeats;
            spreadInBeats = other.spreadInBeats;
            beatsPerCycle = other.beatsPerCycle;
            inverse = other.inverse;
        }

        /// <summary>
        /// Sets the three edited shares, keeping them inside one cycle: the rise first, then
        /// the high hold and the fall in what is left. The low hold takes the rest.
        /// </summary>
        public void SetShares(float riseShare, float holdHighShare, float fallShare)
        {
            rise = Mathf.Clamp01(riseShare);
            holdHigh = Mathf.Clamp(holdHighShare, 0f, 1f - rise);
            fall = Mathf.Clamp(fallShare, 0f, 1f - rise - holdHigh);
        }

        public void OnBeforeSerialize()
        {
            version = CurrentVersion;
        }

        public void OnAfterDeserialize()
        {
            if (version < 1)
            {
                UpgradeFromModes();
            }

            if (version < 2)
            {
                SplitEase();
            }

            version = CurrentVersion;
        }

        /// <summary>
        /// Gives the fall its own ease from the one the rise and fall shared. The fall ran the
        /// shared ease backwards, which is its reverse read forwards. Invert used to apply
        /// before the ease and now flips the eased wave, so an inverted wave swaps the two to
        /// keep the same motion.
        /// </summary>
        private void SplitEase()
        {
            var shared = ease;
            var reversed = AlpsEase.Reversed(shared);
            ease = inverse ? reversed : shared;
            fallEase = inverse ? shared : reversed;
        }

        /// <summary>
        /// Converts settings saved with the forward / ping-pong / random modes. Forward was a
        /// sawtooth, a rise over the whole cycle. Ping-pong kept its going out share and hold,
        /// which the renamed fields already read, and came back over the rest. The ease list
        /// lost Step, whose jump is now a share of zero.
        /// </summary>
        private void UpgradeFromModes()
        {
            const int legacyForward = 0;
            const int legacyRandom = 2;
            var legacyMode = (int)mode;

            if (legacyMode == legacyForward)
            {
                SetShares(1f, 0f, 0f);
            }
            else
            {
                var up = Mathf.Clamp01(rise);
                var top = Mathf.Clamp(holdHigh, 0f, 1f - up);
                SetShares(up, top, 1f - up - top);
            }

            mode = legacyMode == legacyRandom ? AlpsPhaseMode.Random : AlpsPhaseMode.Wave;

            var legacyEase = (int)ease;
            if (legacyEase == LegacyStepEase)
            {
                ease = AlpsEaseType.Linear;
                if (mode == AlpsPhaseMode.Wave)
                {
                    UpgradeStep(legacyMode == legacyForward);
                }
            }
            else if (legacyEase > LegacyStepEase)
            {
                ease = (AlpsEaseType)(legacyEase - 1);
            }
        }

        /// <summary>
        /// A step ease switched the wave from 0 to 1 where it crossed its middle, so the phase
        /// sat at 1 for one stretch of the cycle and at 0 for the rest. The new shares start
        /// every cycle with the rise, so the stretch at 1 can only begin at the cycle's start,
        /// or end at the cycle's end with invert on. When the old stretch sat in the middle,
        /// its length is kept and it moves to whichever of the two is nearer.
        /// </summary>
        private void UpgradeStep(bool wasForward)
        {
            float start;
            float length;
            if (wasForward)
            {
                start = 0.5f;
                length = 0.5f;
            }
            else
            {
                start = rise * 0.5f;
                length = rise * 0.5f + holdHigh + fall * 0.5f;
            }

            // Inverting the old wave put the phase at 1 wherever it had been at 0.
            if (inverse)
            {
                start = Mathf.Repeat(start + length, 1f);
                length = 1f - length;
            }

            var end = Mathf.Repeat(start + length, 1f);
            var toStart = Mathf.Min(start, 1f - start);
            var toEnd = Mathf.Min(end, 1f - end);
            if (toStart <= toEnd)
            {
                SetShares(0f, length, 0f);
                inverse = false;
            }
            else
            {
                SetShares(0f, 1f - length, 0f);
                inverse = true;
            }
        }
    }
}
