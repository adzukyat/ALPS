using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>The single-value slider row: label | track | value box.</summary>
    public class AlpsValueSlider : BaseField<float>
    {
        public new static readonly string ussClassName = "alps-slider";

        private readonly AlpsSliderTrack _track;
        private readonly AlpsNumberBox _box;
        private readonly AlpsSliderSnaps _snaps = new AlpsSliderSnaps();
        private Vector2 _limit = new Vector2(0f, 1f);

        public AlpsValueSlider(string label, Vector2 limit, string unit = "", string format = "0.###")
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            _limit = limit;

            var container = this.Q(className: BaseField<float>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _track = new AlpsSliderTrack(false) { Origin = OriginOf(limit) };
            _track.Changed += (_, high) => value = _snaps.ToValue(_limit, high);
            _track.ResetRequested += _ => ResetToDefault();
            container.Add(_track);

            _box = new AlpsNumberBox(unit, format) { Limit = limit };
            _box.AddToClassList("alps-numberbox--right");
            _box.RegisterValueChangedCallback(evt => value = evt.newValue);
            container.Add(_box);

            SetValueWithoutNotify(limit.x);
        }

        public Vector2 Limit
        {
            get => _limit;
            set
            {
                _limit = value;
                _box.Limit = value;
                _track.Origin = OriginOf(value);
                _track.SetSnaps(_snaps.Normalize(value));
                SetValueWithoutNotify(this.value);
            }
        }

        /// <summary>Values a drag sticks to, in value space. Points on or past the limits are dropped.</summary>
        public float[] Snaps
        {
            get => _snaps.Values;
            set
            {
                _snaps.Values = value;
                _track.SetSnaps(_snaps.Normalize(_limit));
            }
        }

        /// <summary>What a double click on the thumb restores. Null leaves the thumb as it is.</summary>
        public float? DefaultValue { get; set; }

        public void ResetToDefault()
        {
            if (DefaultValue.HasValue)
            {
                value = DefaultValue.Value;
            }
        }

        /// <summary>Where zero sits on the track when the limits span it, otherwise the left edge.</summary>
        private static float OriginOf(Vector2 limit)
        {
            return limit.x < 0f && limit.y > 0f ? Mathf.InverseLerp(limit.x, limit.y, 0f) : 0f;
        }

        public string Unit
        {
            get => _box.Unit;
            set { _box.Unit = value; SetValueWithoutNotify(this.value); }
        }

        /// <summary>Committing while mixed always reaches the other clips. See <see cref="AlpsMixedField"/>.</summary>
        public override float value
        {
            get => base.value;
            set => AlpsMixedField.Set(this, value, v => base.value = v);
        }

        public override void SetValueWithoutNotify(float newValue)
        {
            newValue = Mathf.Clamp(newValue, _limit.x, _limit.y);
            base.SetValueWithoutNotify(newValue);
            _track.SetWithoutNotify(0f, Mathf.InverseLerp(_limit.x, _limit.y, newValue));
            _box.SetValueWithoutNotify(newValue);
        }

        protected override void UpdateMixedValueContent()
        {
            _box.showMixedValue = showMixedValue;
            _track.EnableInClassList(AlpsSliderTrack.MixedClass, showMixedValue);
        }
    }

    /// <summary>The ranged slider row: label | min box | two-thumb track | max box.</summary>
    public class AlpsRangeSlider : BaseField<Vector2>
    {
        public new static readonly string ussClassName = "alps-slider";

        private readonly AlpsSliderTrack _track;
        private readonly AlpsNumberBox _minBox;
        private readonly AlpsNumberBox _maxBox;
        private readonly AlpsSliderSnaps _snaps = new AlpsSliderSnaps();
        private Vector2 _limit = new Vector2(0f, 1f);

        public AlpsRangeSlider(string label, Vector2 limit, string unit = "", string format = "0.###")
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            _limit = limit;

            var container = this.Q(className: BaseField<Vector2>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _minBox = new AlpsNumberBox(unit, format) { Limit = limit };
            _minBox.AddToClassList("alps-numberbox--left");
            _minBox.RegisterValueChangedCallback(evt => value = new Vector2(evt.newValue, value.y));
            container.Add(_minBox);

            _track = new AlpsSliderTrack(true);
            _track.Changed += (low, high) => value = new Vector2(
                _snaps.ToValue(_limit, low),
                _snaps.ToValue(_limit, high));
            _track.ResetRequested += ResetToDefault;
            container.Add(_track);

            _maxBox = new AlpsNumberBox(unit, format) { Limit = limit };
            _maxBox.AddToClassList("alps-numberbox--right");
            _maxBox.RegisterValueChangedCallback(evt => value = new Vector2(value.x, evt.newValue));
            container.Add(_maxBox);

            SetValueWithoutNotify(limit);
        }

        public Vector2 Limit
        {
            get => _limit;
            set
            {
                _limit = value;
                _minBox.Limit = value;
                _maxBox.Limit = value;
                _track.SetSnaps(_snaps.Normalize(value));
                SetValueWithoutNotify(this.value);
            }
        }

        /// <summary>Values a drag sticks to, in value space. Points on or past the limits are dropped.</summary>
        public float[] Snaps
        {
            get => _snaps.Values;
            set
            {
                _snaps.Values = value;
                _track.SetSnaps(_snaps.Normalize(_limit));
            }
        }

        /// <summary>What a double click on a thumb restores, per end. Null leaves the thumbs as they are.</summary>
        public Vector2? DefaultValue { get; set; }

        /// <summary>
        /// Restores the end shown by <paramref name="thumb"/>. When the restored end would pass
        /// the other one, the whole default range comes back instead.
        /// </summary>
        public void ResetToDefault(int thumb)
        {
            if (!DefaultValue.HasValue)
            {
                return;
            }

            var defaults = DefaultValue.Value;
            var current = value;
            var restored = thumb == AlpsSliderTrack.ThumbLow
                ? new Vector2(defaults.x, current.y)
                : new Vector2(current.x, defaults.y);

            value = restored.x <= restored.y ? restored : defaults;
        }

        public string Unit
        {
            get => _minBox.Unit;
            set
            {
                _minBox.Unit = value;
                _maxBox.Unit = value;
                SetValueWithoutNotify(this.value);
            }
        }

        /// <summary>Committing while mixed always reaches the other clips. See <see cref="AlpsMixedField"/>.</summary>
        public override Vector2 value
        {
            get => base.value;
            set => AlpsMixedField.Set(this, value, v => base.value = v);
        }

        public override void SetValueWithoutNotify(Vector2 newValue)
        {
            newValue = new Vector2(
                Mathf.Clamp(newValue.x, _limit.x, _limit.y),
                Mathf.Clamp(newValue.y, _limit.x, _limit.y));

            base.SetValueWithoutNotify(newValue);
            _track.SetWithoutNotify(
                Mathf.InverseLerp(_limit.x, _limit.y, newValue.x),
                Mathf.InverseLerp(_limit.x, _limit.y, newValue.y));
            _minBox.SetValueWithoutNotify(newValue.x);
            _maxBox.SetValueWithoutNotify(newValue.y);
        }

        protected override void UpdateMixedValueContent()
        {
            _minBox.showMixedValue = showMixedValue;
            _maxBox.showMixedValue = showMixedValue;
            _track.EnableInClassList(AlpsSliderTrack.MixedClass, showMixedValue);
        }
    }

    /// <summary>
    /// Snap points of one slider, kept in value space and in track space side by side so a
    /// thumb that landed on a snap reports the exact value rather than a lerp of it.
    /// </summary>
    internal class AlpsSliderSnaps
    {
        private float[] _requested = new float[0];
        private float[] _values = new float[0];
        private float[] _normalized = new float[0];

        /// <summary>The points as given. Kept whole so a later, wider limit can show them again.</summary>
        public float[] Values
        {
            get => _requested;
            set => _requested = value ?? new float[0];
        }

        /// <summary>Sorted normalized points strictly inside the limits.</summary>
        public float[] Normalize(Vector2 limit)
        {
            var inside = new List<float>();
            foreach (var point in _requested)
            {
                if (point > limit.x && point < limit.y && !inside.Contains(point))
                {
                    inside.Add(point);
                }
            }

            inside.Sort();
            _values = inside.ToArray();
            _normalized = new float[_values.Length];
            for (var i = 0; i < _values.Length; i++)
            {
                _normalized[i] = Mathf.InverseLerp(limit.x, limit.y, _values[i]);
            }

            return _normalized;
        }

        public float ToValue(Vector2 limit, float t)
        {
            var index = Array.IndexOf(_normalized, t);
            return index >= 0 ? _values[index] : Mathf.Lerp(limit.x, limit.y, t);
        }
    }

    /// <summary>Snap point sets that mean something for a kind of parameter.</summary>
    public static class AlpsSnapPoints
    {
        /// <summary>More ticks than this crowd the rail, so angle steps widen until they fit.</summary>
        public const int MaxTicks = 7;

        public static readonly float[] None = new float[0];

        /// <summary>Zero, the neutral middle of a signed slider.</summary>
        public static float[] Zero(Vector2 limit)
        {
            return limit.x < 0f && limit.y > 0f ? new[] { 0f } : None;
        }

        /// <summary>Every 45 degrees, or every 90 or 180 when 45 would be too dense.</summary>
        public static float[] Angles(Vector2 limit)
        {
            var step = 45f;
            float[] points;
            while ((points = Multiples(limit, step)).Length > MaxTicks)
            {
                step *= 2f;
            }

            return points;
        }

        /// <summary>Multiples of <paramref name="step"/> strictly inside the limits.</summary>
        public static float[] Multiples(Vector2 limit, float step)
        {
            var points = new List<float>();
            for (var point = Mathf.Floor(limit.x / step) * step; point < limit.y; point += step)
            {
                if (point > limit.x)
                {
                    points.Add(point);
                }
            }

            return points.ToArray();
        }
    }
}
