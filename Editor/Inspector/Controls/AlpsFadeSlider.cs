using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The fade slider row: label | in box | in track || out track | out box.
    ///
    /// The track is split in two so fade in and fade out never meet. The in track fills
    /// from its left edge and the out track from its right edge, so the pair reads as the
    /// ramps of an envelope. <see cref="BaseField{T}.value"/> is (fade in, fade out).
    /// </summary>
    public class AlpsFadeSlider : BaseField<Vector2>
    {
        public new static readonly string ussClassName = "alps-slider";

        private readonly AlpsSliderTrack _inTrack;
        private readonly AlpsSliderTrack _outTrack;
        private readonly AlpsNumberBox _inBox;
        private readonly AlpsNumberBox _outBox;
        private readonly AlpsSliderSnaps _snaps = new AlpsSliderSnaps();
        private float[] _normalizedSnaps = new float[0];
        private float[] _mirroredSnaps = new float[0];
        private readonly float _max;

        public AlpsFadeSlider(string label, float max, string unit = "", string format = "0.###")
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            AddToClassList("alps-fade");
            _max = max;
            var limit = new Vector2(0f, max);

            var container = this.Q(className: BaseField<Vector2>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _inBox = new AlpsNumberBox(unit, format) { Limit = limit, tooltip = "フェードイン" };
            _inBox.AddToClassList("alps-numberbox--left");
            _inBox.RegisterValueChangedCallback(evt => value = new Vector2(evt.newValue, value.y));
            container.Add(_inBox);

            _inTrack = new AlpsSliderTrack(false) { tooltip = "フェードイン" };
            _inTrack.Changed += (_, high) => value = new Vector2(_snaps.ToValue(limit, high), value.y);
            _inTrack.ResetRequested += _ => ResetToDefault(true);
            container.Add(_inTrack);

            // The out track runs mirrored: its fill grows from the right edge toward the middle.
            _outTrack = new AlpsSliderTrack(false) { Origin = 1f, tooltip = "フェードアウト" };
            _outTrack.AddToClassList("alps-fade__out");
            _outTrack.Changed += (_, high) => value = new Vector2(value.x, OutValue(high));
            _outTrack.ResetRequested += _ => ResetToDefault(false);
            container.Add(_outTrack);

            _outBox = new AlpsNumberBox(unit, format) { Limit = limit, tooltip = "フェードアウト" };
            _outBox.AddToClassList("alps-numberbox--right");
            _outBox.RegisterValueChangedCallback(evt => value = new Vector2(value.x, evt.newValue));
            container.Add(_outBox);

            SetValueWithoutNotify(Vector2.zero);
        }

        /// <summary>Values a drag sticks to on either track, in value space.</summary>
        public float[] Snaps
        {
            get => _snaps.Values;
            set
            {
                _snaps.Values = value;
                _normalizedSnaps = _snaps.Normalize(new Vector2(0f, _max));
                _mirroredSnaps = new float[_normalizedSnaps.Length];
                for (var i = 0; i < _normalizedSnaps.Length; i++)
                {
                    _mirroredSnaps[_normalizedSnaps.Length - 1 - i] = 1f - _normalizedSnaps[i];
                }

                _inTrack.SetSnaps(_normalizedSnaps);
                _outTrack.SetSnaps(_mirroredSnaps);
            }
        }

        /// <summary>
        /// Fade out for a normalized out track position. A thumb on a mirrored snap point
        /// reports that snap's exact value rather than a lerp of 1 - position.
        /// </summary>
        private float OutValue(float high)
        {
            var index = System.Array.IndexOf(_mirroredSnaps, high);
            var t = index >= 0 ? _normalizedSnaps[_normalizedSnaps.Length - 1 - index] : 1f - high;
            return _snaps.ToValue(new Vector2(0f, _max), t);
        }

        /// <summary>What a double click on a thumb restores for its side. Null leaves the thumbs as they are.</summary>
        public Vector2? DefaultValue { get; set; }

        public void ResetToDefault(bool fadeIn)
        {
            if (!DefaultValue.HasValue)
            {
                return;
            }

            var defaults = DefaultValue.Value;
            value = fadeIn ? new Vector2(defaults.x, value.y) : new Vector2(value.x, defaults.y);
        }

        /// <summary>Committing while mixed always reaches the other clips. See <see cref="AlpsMixedField"/>.</summary>
        public override Vector2 value
        {
            get => base.value;
            set => AlpsMixedField.Set(this, value, v => base.value = v);
        }

        public override void SetValueWithoutNotify(Vector2 newValue)
        {
            newValue = new Vector2(Mathf.Clamp(newValue.x, 0f, _max), Mathf.Clamp(newValue.y, 0f, _max));
            base.SetValueWithoutNotify(newValue);
            _inTrack.SetWithoutNotify(0f, Mathf.InverseLerp(0f, _max, newValue.x));
            _outTrack.SetWithoutNotify(0f, 1f - Mathf.InverseLerp(0f, _max, newValue.y));
            _inBox.SetValueWithoutNotify(newValue.x);
            _outBox.SetValueWithoutNotify(newValue.y);
        }

        protected override void UpdateMixedValueContent()
        {
            _inBox.showMixedValue = showMixedValue;
            _outBox.showMixedValue = showMixedValue;
            _inTrack.EnableInClassList(AlpsSliderTrack.MixedClass, showMixedValue);
            _outTrack.EnableInClassList(AlpsSliderTrack.MixedClass, showMixedValue);
        }
    }
}
