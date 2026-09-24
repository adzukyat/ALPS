using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The small right-aligned value box beside every slider. Shows the number with its
    /// unit ("65 %", "90°") and stays editable: focusing drops the unit so only the
    /// number is edited, and the unit is re-applied on commit.
    /// </summary>
    public class AlpsNumberBox : BaseField<float>
    {
        public new static readonly string ussClassName = "alps-numberbox";

        private static readonly Regex LeadingNumber =
            new Regex(@"^\s*[-+]?[0-9]*\.?[0-9]+", RegexOptions.Compiled);

        private static readonly Regex TrailingDigit = new Regex("[0-9]", RegexOptions.Compiled);

        private readonly TextField _text;
        private bool _editing;

        /// <param name="textClass">Class for the text field. Hosts with their own box styling pass their own.</param>
        public AlpsNumberBox(string unit = "", string format = "0.###", string textClass = null)
            : base(null, new VisualElement())
        {
            Unit = unit;
            Format = format;

            var container = this.Q(className: BaseField<float>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _text = new TextField { isDelayed = true };
            _text.AddToClassList(textClass ?? ussClassName);
            _text.RegisterValueChangedCallback(OnTextChanged);
            _text.RegisterCallback<FocusInEvent>(_ => SetEditing(true));
            // The delayed commit runs after these callbacks and needs the typed text, and an
            // unchanged number commits without a ChangeEvent, so restore the unit a tick later.
            _text.RegisterCallback<FocusOutEvent>(_ => _text.schedule.Execute(EndEditing));
            _text.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    _text.schedule.Execute(() => SetEditing(false));
                }
            });
            container.Add(_text);

            style.flexGrow = 0;
            style.flexShrink = 0;
            SetValueWithoutNotify(0f);
        }

        /// <summary>Suffix shown after the number. A leading space is added for word units.</summary>
        public string Unit { get; set; }

        public string Format { get; set; }

        public Vector2 Limit { get; set; } = new Vector2(float.MinValue, float.MaxValue);

        /// <summary>Committing while mixed always reaches the other clips. See <see cref="AlpsMixedField"/>.</summary>
        public override float value
        {
            get => base.value;
            set => AlpsMixedField.Set(this, value, v => base.value = v);
        }

        public override void SetValueWithoutNotify(float newValue)
        {
            newValue = Mathf.Clamp(newValue, Limit.x, Limit.y);
            base.SetValueWithoutNotify(newValue);
            _text.SetValueWithoutNotify(FormatValue(newValue));
        }

        /// <summary>A dash while mixed. Editing shows the shown clip's number to start from.</summary>
        protected override void UpdateMixedValueContent()
        {
            _text.SetValueWithoutNotify(FormatValue(value));
        }

        private void EndEditing()
        {
            var focused = _text.focusController?.focusedElement as VisualElement;
            if (focused == null || !_text.Contains(focused))
            {
                SetEditing(false);
            }
        }

        private void SetEditing(bool editing)
        {
            _editing = editing;
            _text.SetValueWithoutNotify(FormatValue(value));
        }

        private string FormatValue(float number)
        {
            if (showMixedValue && !_editing)
            {
                return mixedValueString;
            }

            var text = number.ToString(Format, CultureInfo.InvariantCulture);
            if (_editing || string.IsNullOrEmpty(Unit))
            {
                return text;
            }

            // Degree signs sit tight against the number. Every other unit is spaced.
            return Unit == "°" ? text + Unit : text + " " + Unit;
        }

        private void OnTextChanged(ChangeEvent<string> evt)
        {
            if (TryParse(evt.newValue, Unit, out var parsed))
            {
                value = Mathf.Clamp(parsed, Limit.x, Limit.y);
            }

            // Re-render so the unit comes back even when the entry was rejected.
            SetEditing(false);
        }

        /// <summary>
        /// Reads a typed entry: a simple expression ("1/3", "120*2", "(4+2)/3") through Unity's own
        /// evaluator, full width characters included, or else the number it starts with, so an
        /// entry with its unit still commits.
        /// </summary>
        public static bool TryParse(string text, string unit, out float result)
        {
            text = Normalize(text).Trim();
            if (!string.IsNullOrEmpty(unit) && text.EndsWith(unit, StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - unit.Length).TrimEnd();
            }

            // An expression that reads but divides by zero is rejected, not cut to its first number.
            if (text.Length > 0 && ExpressionEvaluator.Evaluate(text, out result))
            {
                return IsFinite(result);
            }

            // Only a number followed by words, like a unit, falls back to that number. Anything
            // with more digits was a formula the evaluator refused, such as one dividing by zero.
            result = 0f;
            var match = LeadingNumber.Match(text);
            return match.Success &&
                   !TrailingDigit.IsMatch(text.Substring(match.Length)) &&
                   float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                   IsFinite(result);
        }

        /// <summary>Turns full width digits and signs into ASCII, and × ÷ into * /.</summary>
        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var chars = text.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (c >= '！' && c <= '～')
                {
                    chars[i] = (char)(c - 0xFEE0);
                }
                else if (c == '　')
                {
                    chars[i] = ' ';
                }
                else if (c == '×')
                {
                    chars[i] = '*';
                }
                else if (c == '÷')
                {
                    chars[i] = '/';
                }
                else if (c == '−')
                {
                    chars[i] = '-';
                }
            }

            return new string(chars);
        }

        private static bool IsFinite(float number)
        {
            return !float.IsNaN(number) && !float.IsInfinity(number);
        }
    }
}
