using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Animatable: one parameter row plus its framed options.
    ///
    /// The row is [ slider ][ R ][ S ]. R ranges the row over the cycle and S spreads it over
    /// the fixtures, so the slider shows the value, the value range, or the values of the
    /// first and last fixture. With both on a second spread slider stacks under the first,
    /// and the range moves from the upper spread to the lower one.
    ///
    /// Visibility rules:
    ///   R / S ..................... everything except palettes
    ///   Lower spread slider ....... R and S are both on
    ///   Range frame ............... timing / own phase are shown
    ///   Timing / own phase ........ a range with two distinct ends, or a palette with two or more stops
    ///   Own phase ON .............. the shared settings panel opens right below
    ///
    /// A value with nothing to move it over time, such as an arrangement's, leaves R out,
    /// and a range saved on it is read as off.
    ///
    /// The sliders cover a value's usual span, and typing can go past its upper end, or past
    /// either end for a value that runs both ways of zero.
    /// </summary>
    public class AlpsAnimatableView : VisualElement
    {
        /// <summary>Marks the growing half of a [ control ][ R ][ S ] row.</summary>
        private const string FieldClass = "alps-animatable__field";

        /// <summary>Lines R and S up with the upper slider while two spread sliders stack in the row.</summary>
        private const string StackedRowClass = "alps-animatable__row--stacked";

        /// <summary>
        /// How far a closed spread opens, as a fraction of the value's span, when R needs two
        /// different spreads to move between.
        /// </summary>
        private const float SpreadRangeOpening = 0.25f;

        /// <summary>Makes room at the top of a frame for its title.</summary>
        private const string LegendFrameClass = "alps-sub--legend";

        /// <summary>Marks a row without R, so S keeps the gap R would have left.</summary>
        private const string NoRangeRowClass = "alps-animatable__row--no-range";

        private readonly AlpsAnimatableValue _model;
        private readonly AlpsPhaseSettings _clipPhase;
        private readonly Action _onChanged;
        private readonly bool _isPalette;
        private readonly bool _allowRange;
        private readonly Func<int> _paletteCount;

        private readonly AlpsValueSlider _valueSlider;
        private readonly AlpsRangeSlider _rangeSlider;
        private readonly VisualElement _spreadSliders;
        private readonly AlpsRangeSlider _spreadSlider;
        private readonly AlpsRangeSlider _spreadEndSlider;
        private readonly AlpsRangeFlag _rangeFlag;
        private readonly AlpsRangeFlag _spreadFlag;
        private readonly VisualElement _valueRow;

        private readonly VisualElement _rangeFrame;
        private readonly VisualElement _rangeLegend;
        private readonly AlpsSegmentedControl _timing;
        private readonly AlpsToggleSwitch _ownPhase;
        private readonly AlpsPhaseSettingsView _ownPhaseView;

        public AlpsAnimatableView(
            string label,
            AlpsAnimatableValue model,
            AlpsPhaseSettings clipPhase,
            Action onChanged,
            string unit = "",
            string format = "0.###",
            VisualElement paletteRow = null,
            Func<int> paletteCount = null,
            Func<Vector2, float[]> snaps = null,
            AlpsAnimatableValue defaults = null,
            AlpsMixedValues mixed = null,
            bool allowRange = true,
            string spreadFirstTip = "最初の器具",
            string spreadLastTip = "最後の器具")
        {
            _model = model;
            _clipPhase = clipPhase;
            _onChanged = onChanged;
            _isPalette = paletteRow != null;
            _allowRange = allowRange;
            _paletteCount = paletteCount;

            // --- value row -------------------------------------------------
            _valueRow = new VisualElement();
            _valueRow.AddToClassList("alps-animatable__row");

            var valueSnaps = snaps != null ? snaps(model.limit) : AlpsSnapPoints.None;

            // A value that runs both ways of zero, such as an angle, can be typed past either end.
            var signed = model.limit.x < 0f;

            if (_isPalette)
            {
                _valueRow.Add(paletteRow);
                paletteRow.AddToClassList(FieldClass);
            }
            else
            {
                _valueSlider = new AlpsValueSlider(label, model.limit, unit, format)
                {
                    Snaps = valueSnaps,
                    DefaultValue = defaults?.value,
                    AllowAboveLimit = true,
                    AllowBelowLimit = signed,
                };
                _valueSlider.AddToClassList(FieldClass);
                _valueSlider.SetValueWithoutNotify(model.value);
                _valueSlider.RegisterValueChangedCallback(evt =>
                {
                    model.value = evt.newValue;
                    Changed();
                });

                _rangeSlider = new AlpsRangeSlider(label, model.limit, unit, format)
                {
                    Snaps = valueSnaps,
                    DefaultValue = defaults?.range,
                    AllowAboveLimit = true,
                    AllowBelowLimit = signed,
                };
                _rangeSlider.AddToClassList(FieldClass);
                _rangeSlider.SetValueWithoutNotify(model.range);
                _rangeSlider.RegisterValueChangedCallback(evt =>
                {
                    model.range = evt.newValue;
                    Refresh();
                    Changed();
                });

                // Either end of a spread restores the value's default, which lines every fixture up.
                var spreadDefault = defaults != null ? new Vector2(defaults.value, defaults.value) : (Vector2?)null;

                _spreadSliders = new VisualElement();
                _spreadSliders.AddToClassList(FieldClass);
                _spreadSliders.AddToClassList("alps-animatable__spreads");

                _spreadSlider = SpreadSlider(label, model.limit, unit, format, valueSnaps, spreadDefault, spreadFirstTip, spreadLastTip);
                _spreadSlider.SetValueWithoutNotify(model.spreadRange);
                _spreadSlider.RegisterValueChangedCallback(evt =>
                {
                    model.spreadRange = evt.newValue;
                    Refresh();
                    Changed();
                });
                _spreadSliders.Add(_spreadSlider);

                // Only its place under the upper slider tells it apart, so its label keeps the
                // column without being drawn.
                _spreadEndSlider = SpreadSlider(label, model.limit, unit, format, valueSnaps, spreadDefault, spreadFirstTip, spreadLastTip);
                _spreadEndSlider.AddToClassList("alps-animatable__spread-end");
                _spreadEndSlider.SetValueWithoutNotify(model.spreadRangeEnd);
                _spreadEndSlider.RegisterValueChangedCallback(evt =>
                {
                    model.spreadRangeEnd = evt.newValue;
                    Refresh();
                    Changed();
                });
                _spreadSliders.Add(_spreadEndSlider);

                if (allowRange)
                {
                    _rangeFlag = new AlpsRangeFlag();
                    _rangeFlag.SetValueWithoutNotify(model.isRange);
                    _rangeFlag.RegisterValueChangedCallback(evt =>
                    {
                        model.isRange = evt.newValue;
                        SeedSpreadRangeEnd();
                        Refresh();
                        Changed();
                    });
                }

                _spreadFlag = new AlpsRangeFlag("S", "広がり");
                _spreadFlag.AddToClassList("alps-spreadflag");
                _spreadFlag.SetValueWithoutNotify(model.hasSpread);
                _spreadFlag.RegisterValueChangedCallback(evt =>
                {
                    model.hasSpread = evt.newValue;
                    SeedSpread();
                    Refresh();
                    Changed();
                });

                mixed?.Bind(_valueSlider, model, nameof(AlpsAnimatableValue.value));
                mixed?.Bind(_rangeSlider, model, nameof(AlpsAnimatableValue.range));
                mixed?.Bind(_spreadSlider, model, nameof(AlpsAnimatableValue.spreadRange));
                mixed?.Bind(_spreadEndSlider, model, nameof(AlpsAnimatableValue.spreadRangeEnd));
                if (_rangeFlag != null)
                {
                    mixed?.Bind(_rangeFlag, model, nameof(AlpsAnimatableValue.isRange));
                }

                mixed?.Bind(_spreadFlag, model, nameof(AlpsAnimatableValue.hasSpread));

                _valueRow.Add(_valueSlider);
                _valueRow.Add(_rangeSlider);
                _valueRow.Add(_spreadSliders);
                if (_rangeFlag != null)
                {
                    _valueRow.Add(_rangeFlag);
                }
                else
                {
                    _valueRow.AddToClassList(NoRangeRowClass);
                }

                _valueRow.Add(_spreadFlag);
            }

            Add(_valueRow);

            // --- range frame -----------------------------------------------
            _rangeFrame = Frame("レンジ", out _rangeLegend);

            _timing = new AlpsSegmentedControl("タイミング", "周期内", "周期ごと");
            _timing.AddToClassList("alps-seg--compact");
            _timing.SetValueWithoutNotify((int)model.timing);
            _timing.RegisterValueChangedCallback(evt =>
            {
                model.timing = (AlpsTimingMode)evt.newValue;
                Changed();
            });
            mixed?.Bind(_timing, model, nameof(AlpsAnimatableValue.timing));
            _rangeFrame.Add(_timing);

            _ownPhase = new AlpsToggleSwitch("独自の動き");
            _ownPhase.SetValueWithoutNotify(model.useOwnPhase);
            _ownPhase.RegisterValueChangedCallback(evt =>
            {
                model.useOwnPhase = evt.newValue;
                Refresh();
                Changed();
            });
            mixed?.Bind(_ownPhase, model, nameof(AlpsAnimatableValue.useOwnPhase));
            _rangeFrame.Add(_ownPhase);

            _ownPhaseView = new AlpsPhaseSettingsView(model.ownPhase, () =>
            {
                Refresh();
                Changed();
            }, compact: true, mixed: mixed);
            _rangeFrame.Add(_ownPhaseView);

            Add(_rangeFrame);

            Refresh();
        }

        /// <summary>The phase that actually drives this parameter.</summary>
        public AlpsPhaseSettings GoverningPhase => _model.useOwnPhase ? _model.ownPhase : _clipPhase;

        private bool HasMultipleStops =>
            _isPalette
                ? (_paletteCount?.Invoke() ?? 0) >= 2
                : _allowRange && _model.HasMultipleStops;

        /// <summary>R as the row reads it: a range saved on a row without R is off.</summary>
        private bool IsRanged => _allowRange && _model.isRange;

        /// <summary>
        /// Shows the model's values again after something other than this view changed them,
        /// such as a scene handle.
        /// </summary>
        public void Reload()
        {
            if (!_isPalette)
            {
                _valueSlider.SetValueWithoutNotify(_model.value);
                _rangeSlider.SetValueWithoutNotify(_model.range);
                _spreadSlider.SetValueWithoutNotify(_model.spreadRange);
                _spreadEndSlider.SetValueWithoutNotify(_model.spreadRangeEnd);
                _rangeFlag?.SetValueWithoutNotify(_model.isRange);
                _spreadFlag.SetValueWithoutNotify(_model.hasSpread);
            }

            _timing.SetValueWithoutNotify((int)_model.timing);
            _ownPhase.SetValueWithoutNotify(_model.useOwnPhase);
            Refresh();
        }

        public void Refresh()
        {
            if (!_isPalette)
            {
                var spread = _model.hasSpread;
                var ranged = IsRanged;
                AlpsPhaseSettingsView.Show(_valueSlider, !spread && !ranged);
                AlpsPhaseSettingsView.Show(_rangeSlider, !spread && ranged);
                AlpsPhaseSettingsView.Show(_spreadSliders, spread);
                AlpsPhaseSettingsView.Show(_spreadEndSlider, spread && ranged);
                _valueRow.EnableInClassList(StackedRowClass, spread && ranged);
            }

            var multiple = HasMultipleStops;
            AlpsPhaseSettingsView.Show(_timing, multiple);
            AlpsPhaseSettingsView.Show(_ownPhase, multiple);
            AlpsPhaseSettingsView.Show(_ownPhaseView, multiple && _model.useOwnPhase);
            _ownPhaseView.Refresh();

            // A palette's frame holds the same options but is not titled as a range.
            AlpsPhaseSettingsView.Show(_rangeFrame, multiple);
            var titled = !_isPalette;
            AlpsPhaseSettingsView.Show(_rangeLegend, titled);
            _rangeFrame.EnableInClassList(LegendFrameClass, titled);
        }

        /// <summary>
        /// A frame whose title sits in a gap cut into its top border. The gap is two strips,
        /// the panel color above the border line and the frame color below it, so the line
        /// stops at the title on both sides.
        /// </summary>
        private static VisualElement Frame(string title, out VisualElement legend)
        {
            var frame = new VisualElement();
            frame.AddToClassList("alps-sub");
            frame.AddToClassList(LegendFrameClass);

            legend = new VisualElement();
            legend.AddToClassList("alps-sub__legend");
            legend.pickingMode = PickingMode.Ignore;

            var outside = new VisualElement();
            outside.AddToClassList("alps-sub__legend-outside");
            legend.Add(outside);

            var inside = new VisualElement();
            inside.AddToClassList("alps-sub__legend-inside");
            legend.Add(inside);

            var label = new Label(title);
            label.AddToClassList("alps-sub__title");
            legend.Add(label);

            frame.Add(legend);
            return frame;
        }

        /// <summary>
        /// A range slider over the fixtures. x is the first fixture and y the last, and either
        /// may be the larger, so its thumbs pass each other.
        /// </summary>
        private static AlpsRangeSlider SpreadSlider(
            string label,
            Vector2 limit,
            string unit,
            string format,
            float[] snaps,
            Vector2? defaultValue,
            string firstTip,
            string lastTip)
        {
            var slider = new AlpsRangeSlider(label, limit, unit, format)
            {
                Snaps = snaps,
                DefaultValue = defaultValue,
                EndsCanCross = true,
                AllowAboveLimit = true,
                AllowBelowLimit = limit.x < 0f,
            };
            slider.AddToClassList("alps-animatable__spread");
            slider.SetEndTooltips(firstTip, lastTip);
            return slider;
        }

        /// <summary>
        /// Turning S on keeps the look. A spread with nothing in it closes on the value, or on
        /// the two ends of the range while R is on, so every fixture starts where it was.
        /// </summary>
        private void SeedSpread()
        {
            if (!_model.hasSpread ||
                AlpsAnimatableValue.IsOpen(_model.spreadRange) ||
                AlpsAnimatableValue.IsOpen(_model.spreadRangeEnd))
            {
                return;
            }

            var start = IsRanged ? _model.range.x : _model.value;
            var end = IsRanged ? _model.range.y : _model.value;
            _model.spreadRange = new Vector2(start, start);
            _model.spreadRangeEnd = new Vector2(end, end);
            _spreadSlider.SetValueWithoutNotify(_model.spreadRange);
            _spreadEndSlider.SetValueWithoutNotify(_model.spreadRangeEnd);
        }

        /// <summary>
        /// Two equal spreads have nothing to move between, so R's options would stay hidden.
        /// The lower slider then starts as the other state of the upper one: an open spread
        /// closes on its first value, and a closed one opens by <see cref="SpreadRangeOpening"/>
        /// of the span, upward unless that would leave the limits.
        /// </summary>
        private void SeedSpreadRangeEnd()
        {
            if (!_model.hasSpread ||
                !_model.isRange ||
                !AlpsAnimatableValue.Approximately(_model.spreadRange, _model.spreadRangeEnd))
            {
                return;
            }

            var first = _model.spreadRange.x;
            var last = first;
            if (!AlpsAnimatableValue.IsOpen(_model.spreadRange))
            {
                var opening = (_model.limit.y - _model.limit.x) * SpreadRangeOpening;
                last = first + opening <= _model.limit.y ? first + opening : first - opening;
            }

            _model.spreadRangeEnd = new Vector2(first, last);
            _spreadEndSlider.SetValueWithoutNotify(_model.spreadRangeEnd);
        }

        private void Changed()
        {
            _onChanged?.Invoke();
        }
    }
}
