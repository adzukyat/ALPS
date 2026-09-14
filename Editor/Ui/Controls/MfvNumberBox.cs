using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Ui.Editor
{
    /// <summary>
    /// The small right-aligned value box beside every slider. Shows the number with its
    /// unit ("65 %", "90°") and stays editable: typing parses the leading number
    /// and the unit is re-applied on commit.
    /// </summary>
    public class MfvNumberBox : BaseField<float>
    {
        public new static readonly string ussClassName = "mfv-numberbox";

        private static readonly Regex LeadingNumber =
            new Regex(@"^\s*[-+]?[0-9]*\.?[0-9]+", RegexOptions.Compiled);

        private readonly TextField _text;

        public MfvNumberBox(string unit = "", string format = "0.###")
            : base(null, new VisualElement())
        {
            Unit = unit;
            Format = format;

            var container = this.Q(className: BaseField<float>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _text = new TextField { isDelayed = true };
            _text.AddToClassList(ussClassName);
            _text.RegisterValueChangedCallback(OnTextChanged);
            container.Add(_text);

            style.flexGrow = 0;
            style.flexShrink = 0;
            SetValueWithoutNotify(0f);
        }

        /// <summary>Suffix shown after the number. A leading space is added for word units.</summary>
        public string Unit { get; set; }

        public string Format { get; set; }

        public Vector2 Limit { get; set; } = new Vector2(float.MinValue, float.MaxValue);

        public override void SetValueWithoutNotify(float newValue)
        {
            newValue = Mathf.Clamp(newValue, Limit.x, Limit.y);
            base.SetValueWithoutNotify(newValue);
            _text.SetValueWithoutNotify(FormatValue(newValue));
        }

        private string FormatValue(float number)
        {
            var text = number.ToString(Format, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(Unit))
            {
                return text;
            }

            // Degree signs sit tight against the number. Every other unit is spaced.
            return Unit == "°" ? text + Unit : text + " " + Unit;
        }

        private void OnTextChanged(ChangeEvent<string> evt)
        {
            var match = LeadingNumber.Match(evt.newValue ?? string.Empty);
            if (match.Success &&
                float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                value = Mathf.Clamp(parsed, Limit.x, Limit.y);
            }

            // Re-render so the unit comes back even when the entry was rejected.
            _text.SetValueWithoutNotify(FormatValue(value));
        }
    }
}
