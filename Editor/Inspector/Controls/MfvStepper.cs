using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Editor
{
    /// <summary>Speed / rotation speed: a beats stepper with - / + buttons and an optional trailing hint.</summary>
    public class MfvStepper : BaseField<float>
    {
        public new static readonly string ussClassName = "mfv-stepper";

        private readonly Label _valueLabel;
        private readonly Label _hint;

        public MfvStepper(string label, string unit = "拍", float step = 1f, string hint = null)
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            Unit = unit;
            Step = step;

            var container = this.Q(className: BaseField<float>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            var box = new VisualElement();
            box.AddToClassList(ussClassName + "__box");

            var minus = new Label("−");
            minus.AddToClassList(ussClassName + "__button");
            minus.RegisterCallback<PointerDownEvent>(_ => value = Mathf.Max(Minimum, value - Step));

            _valueLabel = new Label();
            _valueLabel.AddToClassList(ussClassName + "__value");

            var plus = new Label("+");
            plus.AddToClassList(ussClassName + "__button");
            plus.RegisterCallback<PointerDownEvent>(_ => value += Step);

            box.Add(minus);
            box.Add(_valueLabel);
            box.Add(plus);
            container.Add(box);

            _hint = new Label(hint ?? string.Empty);
            _hint.AddToClassList("mfv-row__hint");
            _hint.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
            container.Add(_hint);

            SetValueWithoutNotify(0f);
        }

        public string Unit { get; set; }

        public float Step { get; set; }

        public float Minimum { get; set; }

        public override void SetValueWithoutNotify(float newValue)
        {
            newValue = Mathf.Max(Minimum, newValue);
            base.SetValueWithoutNotify(newValue);
            var text = newValue.ToString("0.###", CultureInfo.InvariantCulture);
            _valueLabel.text = string.IsNullOrEmpty(Unit) ? text : text + " " + Unit;
        }
    }
}
