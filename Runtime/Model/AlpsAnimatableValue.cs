using System;
using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Animatable: the value side of the model. A scalar that can be widened
    /// into a range and then read with the clip phase, plus the per-parameter switches
    /// (range / spread / timing / own phase).
    /// </summary>
    [Serializable]
    public class AlpsAnimatableValue : ISerializationCallbackReceiver
    {
        /// <summary>
        /// The layout <see cref="version"/> marks. Values saved before it stored the spread as a
        /// step per order position, which needs the fixture count to become the values at the
        /// first and last position, so <see cref="OnAfterDeserialize"/> closes it on the value.
        /// </summary>
        private const int CurrentVersion = 1;

        /// <summary>Hard limits of the slider. Not animated.</summary>
        public Vector2 limit = new Vector2(0f, 1f);

        public float value;

        /// <summary>Used instead of <see cref="value"/> while <see cref="isRange"/>.</summary>
        public Vector2 range;

        /// <summary>Range toggle: the small "R" next to the slider.</summary>
        public bool isRange;

        /// <summary>
        /// Spread toggle: the small "S" next to "R". While on, the row sets the values of the
        /// first and last fixture in the order, and "R" moves between two such spreads.
        /// </summary>
        public bool hasSpread;

        /// <summary>
        /// Spread: the value at the first order position (x) and at the last (y). The positions
        /// between are spaced evenly whatever the fixture count, and x above y runs downward.
        /// Used instead of <see cref="value"/> while <see cref="hasSpread"/>. With
        /// <see cref="isRange"/> on as well, the spread the range starts from.
        /// </summary>
        public Vector2 spreadRange;

        /// <summary>The spread the range moves to while both toggles are on.</summary>
        public Vector2 spreadRangeEnd;

        /// <summary>
        /// Missing from anything saved before <see cref="CurrentVersion"/>, so it reads as 0
        /// there. Every save writes the current one.
        /// </summary>
        [SerializeField, HideInInspector] private int version;

        public AlpsTimingMode timing = AlpsTimingMode.WithinCycle;

        /// <summary>Own phase: opt out of the clip phase and use <see cref="ownPhase"/>.</summary>
        public bool useOwnPhase;

        public AlpsPhaseSettings ownPhase = new AlpsPhaseSettings();

        public AlpsAnimatableValue() { }

        public AlpsAnimatableValue(float value, Vector2 limit)
        {
            this.limit = limit;
            this.value = value;
            range = limit;
            spreadRange = new Vector2(value, value);
            spreadRangeEnd = spreadRange;
        }

        public AlpsAnimatableValue(AlpsAnimatableValue other)
        {
            limit = other.limit;
            value = other.value;
            range = other.range;
            isRange = other.isRange;
            hasSpread = other.hasSpread;
            spreadRange = other.spreadRange;
            spreadRangeEnd = other.spreadRangeEnd;
            timing = other.timing;
            useOwnPhase = other.useOwnPhase;
            ownPhase = new AlpsPhaseSettings(other.ownPhase);
        }

        /// <summary>
        /// True when the range moves between two distinct values, or two distinct spreads while
        /// spread is on, which is the condition for showing timing and own phase.
        /// </summary>
        public bool HasMultipleStops =>
            isRange && (hasSpread
                ? !Approximately(spreadRange, spreadRangeEnd)
                : !Mathf.Approximately(range.x, range.y));

        /// <summary>True when the two ends of a spread differ, so the fixtures do not all match.</summary>
        public static bool IsOpen(Vector2 spread)
        {
            return !Mathf.Approximately(spread.x, spread.y);
        }

        public static bool Approximately(Vector2 a, Vector2 b)
        {
            return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y);
        }

        public void OnBeforeSerialize()
        {
            version = CurrentVersion;
        }

        public void OnAfterDeserialize()
        {
            if (version < CurrentVersion)
            {
                spreadRange = new Vector2(value, value);
                spreadRangeEnd = spreadRange;
            }

            version = CurrentVersion;
        }
    }
}
