using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Ui.Editor
{
    /// <summary>The single-value slider row: label | track | value box.</summary>
    public class MfvValueSlider : BaseField<float>
    {
        public new static readonly string ussClassName = "mfv-slider";

        private readonly MfvSliderTrack _track;
        private readonly MfvNumberBox _box;
        private Vector2 _limit = new Vector2(0f, 1f);

        public MfvValueSlider(string label, Vector2 limit, string unit = "", string format = "0.###")
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            _limit = limit;

            var container = this.Q(className: BaseField<float>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _track = new MfvSliderTrack(false);
            _track.Changed += (_, high) => value = Mathf.Lerp(_limit.x, _limit.y, high);
            container.Add(_track);

            _box = new MfvNumberBox(unit, format) { Limit = limit };
            _box.AddToClassList("mfv-numberbox--right");
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
                SetValueWithoutNotify(this.value);
            }
        }

        public string Unit
        {
            get => _box.Unit;
            set { _box.Unit = value; SetValueWithoutNotify(this.value); }
        }

        public override void SetValueWithoutNotify(float newValue)
        {
            newValue = Mathf.Clamp(newValue, _limit.x, _limit.y);
            base.SetValueWithoutNotify(newValue);
            _track.SetWithoutNotify(0f, Mathf.InverseLerp(_limit.x, _limit.y, newValue));
            _box.SetValueWithoutNotify(newValue);
        }
    }

    /// <summary>The ranged slider row: label | min box | two-thumb track | max box.</summary>
    public class MfvRangeSlider : BaseField<Vector2>
    {
        public new static readonly string ussClassName = "mfv-slider";

        private readonly MfvSliderTrack _track;
        private readonly MfvNumberBox _minBox;
        private readonly MfvNumberBox _maxBox;
        private Vector2 _limit = new Vector2(0f, 1f);

        public MfvRangeSlider(string label, Vector2 limit, string unit = "", string format = "0.###")
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            _limit = limit;

            var container = this.Q(className: BaseField<Vector2>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _minBox = new MfvNumberBox(unit, format) { Limit = limit };
            _minBox.AddToClassList("mfv-numberbox--left");
            _minBox.RegisterValueChangedCallback(evt => value = new Vector2(evt.newValue, value.y));
            container.Add(_minBox);

            _track = new MfvSliderTrack(true);
            _track.Changed += (low, high) => value = new Vector2(
                Mathf.Lerp(_limit.x, _limit.y, low),
                Mathf.Lerp(_limit.x, _limit.y, high));
            container.Add(_track);

            _maxBox = new MfvNumberBox(unit, format) { Limit = limit };
            _maxBox.AddToClassList("mfv-numberbox--right");
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
                SetValueWithoutNotify(this.value);
            }
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
    }
}
