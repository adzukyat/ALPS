using System;
using UnityEngine;

namespace ManeuverForVRC
{
    /// <summary>
    /// Animatable: the value side of the model. A scalar that can be widened
    /// into a range and then read with the clip phase, plus the per-parameter switches
    /// (range / spread / timing / cancel on return / own phase).
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

        /// <summary>
        /// Spread toggle: the small "S" next to "R". While on, <see cref="value"/> is a fixed
        /// offset and the range toggle ranges the spread instead of the value.
        /// </summary>
        public bool hasSpread;

        /// <summary>Spread: offset added once per order position.</summary>
        public float spread;

        /// <summary>Used instead of <see cref="spread"/> while both toggles are on.</summary>
        public Vector2 spreadRange;

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
            hasSpread = other.hasSpread;
            spread = other.spread;
            spreadRange = other.spreadRange;
            timing = other.timing;
            cancelOnReturn = other.cancelOnReturn;
            useOwnPhase = other.useOwnPhase;
            ownPhase = new MfvPhaseSettings(other.ownPhase);
        }

        /// <summary>The range the "R" toggle drives: the spread range while spread is on.</summary>
        public Vector2 ActiveRange => hasSpread ? spreadRange : range;

        /// <summary>
        /// True when the active range has two distinct ends, which is the condition for
        /// showing timing and own phase.
        /// </summary>
        public bool HasMultipleStops => isRange && !Mathf.Approximately(ActiveRange.x, ActiveRange.y);
    }
}
