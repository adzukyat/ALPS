using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Animatable: one parameter row plus its framed options.
    ///
    /// The row is [ slider ][ R ][ S ]. R ranges the row and S turns it into a spread,
    /// so the slider shows the value, the value range, the spread or the spread range.
    /// While spread is on the value moves into the spread frame as an offset.
    ///
    /// Visibility rules:
    ///   R / S ..................... everything except palettes
    ///   Spread frame .............. S is on
    ///   Range frame ............... timing / own phase are shown
    ///   Timing / own phase ........ a range with two distinct ends, or a palette with two or more stops
    ///   Own phase ON .............. the shared settings panel opens right below
    /// </summary>
    public class AlpsAnimatableView : VisualElement
    {
        /// <summary>Marks the growing half of a [ control ][ R ][ S ] row.</summary>
        private const string FieldClass = "alps-animatable__field";

        /// <summary>
        /// The least a seeded spread range opens, as a fraction of the value's span. The spread
        /// track covers twice the span, so this keeps the two thumbs an eighth of the track apart.
        /// </summary>
        private const float SpreadRangeOpening = 0.25f;

        /// <summary>Makes room at the top of a frame for its title.</summary>
        private const string LegendFrameClass = "alps-sub--legend";

        private readonly AlpsAnimatableValue _model;
        private readonly AlpsPhaseSettings _clipPhase;
        private readonly Action _onChanged;
        private readonly bool _isPalette;
        private readonly Func<int> _paletteCount;

        private readonly AlpsValueSlider _valueSlider;
        private readonly AlpsRangeSlider _rangeSlider;
        private readonly AlpsValueSlider _spreadSlider;
        private readonly AlpsRangeSlider _spreadRange;
        private readonly AlpsRangeFlag _rangeFlag;
        private readonly AlpsRangeFlag _spreadFlag;
        private readonly VisualElement _valueRow;

        private readonly VisualElement _rangeFrame;
        private readonly VisualElement _rangeLegend;
        private readonly AlpsSegmentedControl _timing;
        private readonly AlpsToggleSwitch _ownPhase;
        private readonly AlpsPhaseSettingsView _ownPhaseView;

        private readonly VisualElement _spreadFrame;
        private readonly AlpsValueSlider _offsetSlider;

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
            AlpsMixedValues mixed = null)
        {
            _model = model;
            _clipPhase = clipPhase;
            _onChanged = onChanged;
            _isPalette = paletteRow != null;
            _paletteCount = paletteCount;

            // --- value row -------------------------------------------------
            _valueRow = new VisualElement();
            _valueRow.AddToClassList("alps-animatable__row");

            // Negative spread fans the other way.
            var span = Mathf.Max(1f, model.limit.y - model.limit.x);
            var spreadLimit = new Vector2(-span, span);

            // The spread sliders are signed, so zero always snaps even when the value has no points.
            var valueSnaps = snaps != null ? snaps(model.limit) : AlpsSnapPoints.None;
            var spreadSnaps = AlpsSnapPoints.Zero(spreadLimit);
            if (snaps != null)
            {
                spreadSnaps = MergeSnaps(spreadSnaps, snaps(spreadLimit));
            }

            if (_isPalette)
            {
                _valueRow.Add(paletteRow);
                paletteRow.AddToClassList(FieldClass);
            }
            else
            {
                _valueSlider = new AlpsValueSlider(label, model.limit, unit, format) { Snaps = valueSnaps, DefaultValue = defaults?.value };
                _valueSlider.AddToClassList(FieldClass);
                _valueSlider.SetValueWithoutNotify(model.value);
                _valueSlider.RegisterValueChangedCallback(evt => SetValue(evt.newValue));

                _rangeSlider = new AlpsRangeSlider(label, model.limit, unit, format) { Snaps = valueSnaps, DefaultValue = defaults?.range };
                _rangeSlider.AddToClassList(FieldClass);
                _rangeSlider.SetValueWithoutNotify(model.range);
                _rangeSlider.RegisterValueChangedCallback(evt =>
                {
                    model.range = evt.newValue;
                    Refresh();
                    Changed();
                });

                _spreadSlider = new AlpsValueSlider(label, spreadLimit, unit, format) { Snaps = spreadSnaps, DefaultValue = 0f };
                _spreadSlider.AddToClassList(FieldClass);
                _spreadSlider.SetValueWithoutNotify(model.spread);
                _spreadSlider.RegisterValueChangedCallback(evt =>
                {
                    model.spread = evt.newValue;
                    Changed();
                });

                _spreadRange = new AlpsRangeSlider(label, spreadLimit, unit, format)
                {
                    Snaps = spreadSnaps,
                    DefaultValue = new Vector2(0f, span * SpreadRangeOpening),
                };
                _spreadRange.AddToClassList(FieldClass);
                _spreadRange.SetValueWithoutNotify(model.spreadRange);
                _spreadRange.RegisterValueChangedCallback(evt =>
                {
                    model.spreadRange = evt.newValue;
                    Refresh();
                    Changed();
                });

                _rangeFlag = new AlpsRangeFlag();
                _rangeFlag.SetValueWithoutNotify(model.isRange);
                _rangeFlag.RegisterValueChangedCallback(evt =>
                {
                    model.isRange = evt.newValue;
                    SeedSpreadRange(span);
                    Refresh();
                    Changed();
                });

                _spreadFlag = new AlpsRangeFlag("S", "広がり");
                _spreadFlag.AddToClassList("alps-spreadflag");
                _spreadFlag.SetValueWithoutNotify(model.hasSpread);
                _spreadFlag.RegisterValueChangedCallback(evt =>
                {
                    model.hasSpread = evt.newValue;
                    SeedSpreadRange(span);
                    Refresh();
                    Changed();
                });

                mixed?.Bind(_valueSlider, model, nameof(AlpsAnimatableValue.value));
                mixed?.Bind(_rangeSlider, model, nameof(AlpsAnimatableValue.range));
                mixed?.Bind(_spreadSlider, model, nameof(AlpsAnimatableValue.spread));
                mixed?.Bind(_spreadRange, model, nameof(AlpsAnimatableValue.spreadRange));
                mixed?.Bind(_rangeFlag, model, nameof(AlpsAnimatableValue.isRange));
                mixed?.Bind(_spreadFlag, model, nameof(AlpsAnimatableValue.hasSpread));

                _valueRow.Add(_valueSlider);
                _valueRow.Add(_rangeSlider);
                _valueRow.Add(_spreadSlider);
                _valueRow.Add(_spreadRange);
                _valueRow.Add(_rangeFlag);
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

            // --- spread frame ----------------------------------------------
            if (!_isPalette)
            {
                _spreadFrame = Frame("広がり", out _);

                _offsetSlider = new AlpsValueSlider("オフセット", model.limit, unit, format) { Snaps = valueSnaps, DefaultValue = defaults?.value };
                _offsetSlider.SetValueWithoutNotify(model.value);
                _offsetSlider.RegisterValueChangedCallback(evt => SetValue(evt.newValue));
                mixed?.Bind(_offsetSlider, model, nameof(AlpsAnimatableValue.value));
                _spreadFrame.Add(_offsetSlider);

                Add(_spreadFrame);
            }

            Refresh();
        }

        /// <summary>The phase that actually drives this parameter.</summary>
        public AlpsPhaseSettings GoverningPhase => _model.useOwnPhase ? _model.ownPhase : _clipPhase;

        private bool HasMultipleStops =>
            _isPalette
                ? (_paletteCount?.Invoke() ?? 0) >= 2
                : _model.HasMultipleStops;

        public void Refresh()
        {
            if (!_isPalette)
            {
                var spread = _model.hasSpread;
                AlpsPhaseSettingsView.Show(_valueSlider, !spread && !_model.isRange);
                AlpsPhaseSettingsView.Show(_rangeSlider, !spread && _model.isRange);
                AlpsPhaseSettingsView.Show(_spreadSlider, spread && !_model.isRange);
                AlpsPhaseSettingsView.Show(_spreadRange, spread && _model.isRange);
                AlpsPhaseSettingsView.Show(_spreadFrame, spread);
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

        private static float[] MergeSnaps(float[] a, float[] b)
        {
            var merged = new float[a.Length + b.Length];
            a.CopyTo(merged, 0);
            b.CopyTo(merged, a.Length);
            return merged;
        }

        /// <summary>The row slider and the offset slider edit the same value.</summary>
        private void SetValue(float value)
        {
            _model.value = value;
            _valueSlider.SetValueWithoutNotify(value);
            _offsetSlider.SetValueWithoutNotify(value);
            Changed();
        }

        /// <summary>
        /// A collapsed spread range has nothing to animate, so its options would stay hidden.
        /// Start it closed and open to the current spread. A spread too small to pull the
        /// thumbs apart opens to <see cref="SpreadRangeOpening"/> of the span in its direction.
        /// </summary>
        private void SeedSpreadRange(float span)
        {
            if (!_model.hasSpread || !_model.isRange || !Mathf.Approximately(_model.spreadRange.x, _model.spreadRange.y))
            {
                return;
            }

            var opening = span * SpreadRangeOpening;
            var end = Mathf.Abs(_model.spread) >= opening
                ? _model.spread
                : (_model.spread < 0f ? -opening : opening);
            _model.spreadRange = new Vector2(0f, end);
            _spreadRange.SetValueWithoutNotify(_model.spreadRange);
        }

        private void Changed()
        {
            _onChanged?.Invoke();
        }
    }
}
