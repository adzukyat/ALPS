using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Speed / rotation speed: a beats stepper with - / + buttons and an optional trailing hint.
    /// The value between the buttons is a number box, so clicking it edits the number directly.
    /// </summary>
    public class AlpsStepper : BaseField<float>
    {
        public new static readonly string ussClassName = "alps-stepper";

        private readonly AlpsNumberBox _box;
        private readonly Label _hint;

        public AlpsStepper(string label, string unit = "拍", float step = 1f, string hint = null)
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            Step = step;

            var container = this.Q(className: BaseField<float>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            var box = new VisualElement();
            box.AddToClassList(ussClassName + "__box");

            var minus = new Label("−");
            minus.AddToClassList(ussClassName + "__button");
            minus.RegisterCallback<PointerDownEvent>(_ => value = Mathf.Max(Minimum, value - Step));

            _box = new AlpsNumberBox(unit, textClass: ussClassName + "__text");
            _box.AddToClassList(ussClassName + "__value");
            _box.RegisterValueChangedCallback(evt => value = evt.newValue);
            Minimum = 0f;

            var plus = new Label("+");
            plus.AddToClassList(ussClassName + "__button");
            plus.RegisterCallback<PointerDownEvent>(_ => value = Mathf.Min(Maximum, value + Step));

            box.Add(minus);
            box.Add(_box);
            box.Add(plus);
            container.Add(box);

            _hint = new Label(hint ?? string.Empty);
            _hint.AddToClassList("alps-row__hint");
            _hint.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
            container.Add(_hint);

            SetValueWithoutNotify(0f);
        }

        public string Unit
        {
            get => _box.Unit;
            set { _box.Unit = value; SetValueWithoutNotify(this.value); }
        }

        public float Step { get; set; }

        public float Minimum
        {
            get => _box.Limit.x;
            set { _box.Limit = new Vector2(value, _box.Limit.y); SetValueWithoutNotify(this.value); }
        }

        /// <summary>Upper bound for the buttons and typed values. Unbounded unless set.</summary>
        public float Maximum
        {
            get => _box.Limit.y;
            set { _box.Limit = new Vector2(_box.Limit.x, value); SetValueWithoutNotify(this.value); }
        }

        /// <summary>Committing while mixed always reaches the other clips. See <see cref="AlpsMixedField"/>.</summary>
        public override float value
        {
            get => base.value;
            set => AlpsMixedField.Set(this, value, v => base.value = v);
        }

        public override void SetValueWithoutNotify(float newValue)
        {
            newValue = Mathf.Clamp(newValue, Minimum, Maximum);
            base.SetValueWithoutNotify(newValue);
            _box.SetValueWithoutNotify(newValue);
        }

        protected override void UpdateMixedValueContent()
        {
            _box.showMixedValue = showMixedValue;
        }
    }
}
