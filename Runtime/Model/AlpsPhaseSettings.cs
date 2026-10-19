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
        /// one ease, the fall running it backwards. Before version 3 the delay in beats was
        /// measured over the whole group, not between neighbours. <see cref="OnAfterDeserialize"/>
        /// converts all three.
        /// </summary>
        private const int CurrentVersion = 3;

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
        /// <see cref="HoldLow"/>) add up to at least one cycle. The first three may add up to
        /// <see cref="AlpsShowLayout.WaveMaxCycles"/> cycles, and the wave then runs on into
        /// the cycles after the one it started in. Wave only.
        /// </summary>
        [FormerlySerializedAs("pingPongRatio")]
        [Range(0f, AlpsShowLayout.WaveMaxCycles)] public float rise = 0.5f;

        /// <summary>The share of the cycle held at 1 after the rise. Wave only.</summary>
        [FormerlySerializedAs("pingPongHold")]
        [Range(0f, AlpsShowLayout.WaveMaxCycles)] public float holdHigh;

        /// <summary>The share of the cycle spent falling back to 0 after the high hold. Wave only.</summary>
        [Range(0f, AlpsShowLayout.WaveMaxCycles)] public float fall = 0.5f;

        /// <summary>
        /// The share of the cycle held at 0 until the next one: what the other three leave
        /// over, none once they fill the cycle.
        /// </summary>
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
        /// The delay in beats between neighbouring order positions, for a show authored
        /// against the music: it stays that many beats when the speed changes or fixtures are
        /// added, where <see cref="spread"/> stays a share of the cycle over the whole group.
        /// Used while <see cref="spreadInBeats"/> is on.
        /// </summary>
        public float spreadBeats = 0f;

        /// <summary>
        /// Which of the two spreads is authored. The other one is kept, not used. In / out
        /// always reads <see cref="spreadBeats"/>.
        /// </summary>
        public bool spreadInBeats;

        /// <summary>
        /// An in / out saved before version 3, whose <see cref="spreadBeats"/> still runs from
        /// the first order position to the last. Turning that into the delay between
        /// neighbours needs the fixture count, which only the bound track knows, so
        /// <see cref="ConvertLegacyDelay"/> does it once one is known, and the compiler reads
        /// it the old way until then.
        /// </summary>
        [SerializeField, HideInInspector] private bool legacyDelayAcrossGroup;

        /// <summary>The digits a converted delay keeps, the four the beats stepper shows.</summary>
        private const float LegacyDelayPrecision = 10000f;

        /// <summary>Speed in beats per cycle. In / out has no cycle and ignores it.</summary>
        [Min(0f)] public float beatsPerCycle = 2f;

        /// <summary>Invert. Ignored while <see cref="mode"/> is Random.</summary>
        public bool inverse;

        /// <summary>
        /// Chance that a cycle fires, rolled for every cycle at every order position. A cycle
        /// that does not fire rests at the bottom of the wave, or the top when inverted. Missing
        /// from older data, which then keeps 1 and fires every cycle. Wave only.
        /// </summary>
        [Range(0f, 1f)] public float fireChance = 1f;

        /// <summary>
        /// Beats an in / out phase takes to rise from 0 to 1 after the clip's start, along
        /// <see cref="ease"/>. In / out only.
        /// </summary>
        [Min(0f)] public float inBeats = 2f;

        /// <summary>
        /// Beats an in / out phase takes to fall back to 0 before the clip's end, along
        /// <see cref="fallEase"/>. In / out only.
        /// </summary>
        [Min(0f)] public float outBeats = 2f;

        /// <summary>
        /// Which of the in and the out play. The one left out keeps its beats for when it is
        /// turned back on. Missing from older data, which then keeps both. In / out only.
        /// </summary>
        public AlpsInOutMode inOutMode = AlpsInOutMode.Both;

        /// <summary>The in beats that play: none while <see cref="inOutMode"/> is out only.</summary>
        public float PlayedInBeats => inOutMode == AlpsInOutMode.OutOnly ? 0f : Mathf.Max(0f, inBeats);

        /// <summary>The out beats that play: none while <see cref="inOutMode"/> is in only.</summary>
        public float PlayedOutBeats => inOutMode == AlpsInOutMode.InOnly ? 0f : Mathf.Max(0f, outBeats);

        public AlpsPhaseSettings() { }

        /// <summary>
        /// True when the spread is read in beats. A speed of zero has no beats to count,
        /// so the spread falls back to its share of the cycle then.
        /// </summary>
        public bool UsesSpreadBeats => spreadInBeats && beatsPerCycle > 0f;

        /// <summary>True while an in / out's delay still waits for <see cref="ConvertLegacyDelay"/>.</summary>
        public bool HasLegacyDelay => legacyDelayAcrossGroup;

        /// <summary>
        /// The wave's delay from one order position to the next in cycles, whichever unit it
        /// was authored in. The share is spread over <paramref name="positions"/>, and the
        /// beats are already between neighbours.
        /// </summary>
        public float PositionDelayCycles(int positions)
        {
            return UsesSpreadBeats ? spreadBeats / beatsPerCycle : spread / Mathf.Max(1, positions);
        }

        /// <summary>
        /// The in / out's delay from one order position to the next in beats. One saved before
        /// version 3 and not converted yet still spreads its beats from the first position to
        /// the last.
        /// </summary>
        public float InOutDelayBeats(int positions)
        {
            if (!legacyDelayAcrossGroup)
            {
                return spreadBeats;
            }

            return positions > 1 ? spreadBeats / (positions - 1) : 0f;
        }

        /// <summary>
        /// The delay between neighbours in beats that the share comes to over
        /// <paramref name="positions"/>, so a switch from the share to beats keeps the look.
        /// </summary>
        public float SpreadAsBeats(int positions)
        {
            return spread * beatsPerCycle / Mathf.Max(1, positions);
        }

        /// <summary>The share over <paramref name="positions"/> that the delay in beats comes to.</summary>
        public float BeatsAsSpread(int positions)
        {
            return beatsPerCycle > 0f ? spreadBeats / beatsPerCycle * Mathf.Max(1, positions) : spread;
        }

        /// <summary>
        /// Seeds <see cref="spreadBeats"/> from the share. In / out has no cycle and reads the
        /// delay between neighbours in beats alone, so switching to it starts from the delay
        /// the wave had over <paramref name="positions"/>.
        /// </summary>
        public void SeedSpreadBeats(int positions)
        {
            if (!UsesSpreadBeats)
            {
                SetSpreadBeats(SpreadAsBeats(positions));
            }
        }

        /// <summary>Sets the delay between neighbours in beats, which is never the old one across the group.</summary>
        public void SetSpreadBeats(float beats)
        {
            spreadBeats = beats;
            legacyDelayAcrossGroup = false;
        }

        /// <summary>
        /// Turns an in / out delay saved across the group into the delay between neighbours
        /// over <paramref name="positions"/>, kept to the four digits the stepper shows, so the
        /// last position lands very close to where it did. Returns whether anything changed.
        /// </summary>
        public bool ConvertLegacyDelay(int positions)
        {
            if (!legacyDelayAcrossGroup)
            {
                return false;
            }

            SetSpreadBeats(Mathf.Round(InOutDelayBeats(positions) * LegacyDelayPrecision) / LegacyDelayPrecision);
            return true;
        }

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
            legacyDelayAcrossGroup = other.legacyDelayAcrossGroup;
            beatsPerCycle = other.beatsPerCycle;
            inverse = other.inverse;
            fireChance = other.fireChance;
            inBeats = other.inBeats;
            outBeats = other.outBeats;
            inOutMode = other.inOutMode;
        }

        /// <summary>
        /// Sets the three edited shares, keeping them inside
        /// <see cref="AlpsShowLayout.WaveMaxCycles"/> cycles: the rise first, then the high
        /// hold and the fall in what is left. The low hold takes the rest of the first cycle.
        /// </summary>
        public void SetShares(float riseShare, float holdHighShare, float fallShare)
        {
            const float max = AlpsShowLayout.WaveMaxCycles;
            rise = Mathf.Clamp(riseShare, 0f, max);
            holdHigh = Mathf.Clamp(holdHighShare, 0f, max - rise);
            fall = Mathf.Clamp(fallShare, 0f, max - rise - holdHigh);
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

            if (version < 3)
            {
                SplitDelay();
            }

            version = CurrentVersion;
        }

        /// <summary>
        /// The delay in beats used to run from the first order position to the last. A wave
        /// takes the share of the cycle it came to, which still reads across the group. An in /
        /// out has no cycle, so it waits for the fixture count (<see cref="ConvertLegacyDelay"/>).
        /// No delay reads the same either way and keeps its unit.
        /// </summary>
        private void SplitDelay()
        {
            if (spreadBeats == 0f)
            {
                return;
            }

            if (mode == AlpsPhaseMode.InOut)
            {
                legacyDelayAcrossGroup = true;
                return;
            }

            // Without a speed the share was already what played.
            if (UsesSpreadBeats)
            {
                spread = spreadBeats / beatsPerCycle;
            }

            spreadInBeats = false;
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
