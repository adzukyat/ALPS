using System;
using UnityEngine;

namespace ManeuverForVRC.Ui
{
    /// <summary>
    /// Animatable: the value side of the model. A scalar that can be widened
    /// into a range and then read with the clip phase, plus the per-parameter switches
    /// (spread / timing / cancel on return / own phase).
    /// </summary>
    [Serializable]
    public class MfvAnimatableValue
    {
        /// <summary>Hard limits of the slider. Not animated.</summary>
        public Vector2 limit = new Vector2(0f, 1f);

        public float value;

        /// <summary>Used instead of <see cref="value"/> while <see cref="isRange"/>.</summary>
        public Vector2 range;

        /// <summary>Range toggle: the small "R" next to the slider.</summary>
        public bool isRange;

        /// <summary>Spread: per-fixture fixed offset. Animatable, but never recursive.</summary>
        public float spread;

        public Vector2 spreadRange;

        public bool spreadIsRange;

        public MfvTimingMode timing = MfvTimingMode.WithinCycle;

        /// <summary>Cancel on return: mute this value while the phase is on the return leg.</summary>
        public bool cancelOnReturn;

        /// <summary>Own phase: opt out of the clip phase and use <see cref="ownPhase"/>.</summary>
        public bool useOwnPhase;

        public MfvPhaseSettings ownPhase = new MfvPhaseSettings();

        public MfvAnimatableValue() { }

        public MfvAnimatableValue(float value, Vector2 limit)
        {
            this.limit = limit;
            this.value = value;
            range = limit;
            spreadRange = new Vector2(0f, 0f);
        }

        public MfvAnimatableValue(MfvAnimatableValue other)
        {
            limit = other.limit;
            value = other.value;
            range = other.range;
            isRange = other.isRange;
            spread = other.spread;
            spreadRange = other.spreadRange;
            spreadIsRange = other.spreadIsRange;
            timing = other.timing;
            cancelOnReturn = other.cancelOnReturn;
            useOwnPhase = other.useOwnPhase;
            ownPhase = new MfvPhaseSettings(other.ownPhase);
        }

        /// <summary>
        /// True when the parameter carries two or more distinct stops, which is the
        /// condition for showing timing and own phase.
        /// </summary>
        public bool HasMultipleStops => isRange && !Mathf.Approximately(range.x, range.y);

        /// <summary>Resolve the parameter for one fixture at one phase.</summary>
        public float Evaluate(float phase, int orderIndex)
        {
            if (!isRange)
            {
                return value + SpreadFor(orderIndex);
            }

            var t = timing == MfvTimingMode.PerCycle ? (phase >= 0.5f ? 1f : 0f) : Mathf.Clamp01(phase);
            return Mathf.Lerp(range.x, range.y, t) + SpreadFor(orderIndex);
        }

        private float SpreadFor(int orderIndex)
        {
            if (!spreadIsRange)
            {
                return spread * orderIndex;
            }

            // A ranged spread distributes deterministically across the fixture index.
            var t = Mathf.Repeat(orderIndex * 0.6180339f, 1f);
            return Mathf.Lerp(spreadRange.x, spreadRange.y, t);
        }
    }
}
