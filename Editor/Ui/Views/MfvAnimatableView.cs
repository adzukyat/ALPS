using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Ui.Editor
{
    /// <summary>
    /// Animatable: one parameter row plus its indented sub-panel.
    ///
    /// Visibility rules:
    ///   Range / spread ............ everything except palettes
    ///   Cancel on return .......... the governing phase is PingPong
    ///   Timing / own phase ........ a range, or a palette with two or more stops
    ///   Own phase ON .............. the shared settings panel opens right below
    /// </summary>
    public class MfvAnimatableView : VisualElement
    {
        /// <summary>Marks the growing half of a [ control ][ R ] row.</summary>
        private const string FieldClass = "mfv-animatable__field";

        private readonly MfvAnimatableValue _model;
        private readonly MfvPhaseSettings _clipPhase;
        private readonly Action _onChanged;
        private readonly bool _isPalette;
        private readonly Func<int> _paletteCount;

        private readonly MfvValueSlider _valueSlider;
        private readonly MfvRangeSlider _rangeSlider;
        private readonly MfvRangeFlag _rangeFlag;
        private readonly VisualElement _valueRow;

        private readonly VisualElement _sub;
        private readonly MfvValueSlider _spreadSlider;
        private readonly MfvRangeSlider _spreadRange;
        private readonly MfvRangeFlag _spreadFlag;
        private readonly VisualElement _spreadRow;
        private readonly MfvSegmentedControl _timing;
        private readonly MfvToggleSwitch _cancelOnReturn;
        private readonly MfvToggleSwitch _ownPhase;
        private readonly MfvPhaseSettingsView _ownPhaseView;

        public MfvAnimatableView(
            string label,
            MfvAnimatableValue model,
            MfvPhaseSettings clipPhase,
            Action onChanged,
            string unit = "",
            string format = "0.###",
            VisualElement paletteRow = null,
            Func<int> paletteCount = null)
        {
            _model = model;
            _clipPhase = clipPhase;
            _onChanged = onChanged;
            _isPalette = paletteRow != null;
            _paletteCount = paletteCount;

            // --- value row -------------------------------------------------
            _valueRow = new VisualElement();
            _valueRow.AddToClassList("mfv-animatable__row");

            if (_isPalette)
            {
                _valueRow.Add(paletteRow);
                paletteRow.AddToClassList(FieldClass);
            }
            else
            {
                _valueSlider = new MfvValueSlider(label, model.limit, unit, format);
                _valueSlider.AddToClassList(FieldClass);
                _valueSlider.SetValueWithoutNotify(model.value);
                _valueSlider.RegisterValueChangedCallback(evt =>
                {
                    model.value = evt.newValue;
                    Changed();
                });

                _rangeSlider = new MfvRangeSlider(label, model.limit, unit, format);
                _rangeSlider.AddToClassList(FieldClass);
                _rangeSlider.SetValueWithoutNotify(model.range);
                _rangeSlider.RegisterValueChangedCallback(evt =>
                {
                    model.range = evt.newValue;
                    Refresh();
                    Changed();
                });

                _rangeFlag = new MfvRangeFlag();
                _rangeFlag.SetValueWithoutNotify(model.isRange);
                _rangeFlag.RegisterValueChangedCallback(evt =>
                {
                    model.isRange = evt.newValue;
                    Refresh();
                    Changed();
                });

                _valueRow.Add(_valueSlider);
                _valueRow.Add(_rangeSlider);
                _valueRow.Add(_rangeFlag);
            }

            Add(_valueRow);

            // --- sub panel -------------------------------------------------
            _sub = new VisualElement();
            _sub.AddToClassList("mfv-sub");

            if (!_isPalette)
            {
                _spreadRow = new VisualElement();
                _spreadRow.AddToClassList("mfv-animatable__row");

                var spreadLimit = new Vector2(0f, Mathf.Max(1f, model.limit.y - model.limit.x));

                _spreadSlider = new MfvValueSlider("広がり", spreadLimit, unit, format);
                _spreadSlider.AddToClassList(FieldClass);
                _spreadSlider.SetValueWithoutNotify(model.spread);
                _spreadSlider.RegisterValueChangedCallback(evt =>
                {
                    model.spread = evt.newValue;
                    Changed();
                });

                _spreadRange = new MfvRangeSlider("広がり", spreadLimit, unit, format);
                _spreadRange.AddToClassList(FieldClass);
                _spreadRange.SetValueWithoutNotify(model.spreadRange);
                _spreadRange.RegisterValueChangedCallback(evt =>
                {
                    model.spreadRange = evt.newValue;
                    Changed();
                });

                _spreadFlag = new MfvRangeFlag();
                _spreadFlag.SetValueWithoutNotify(model.spreadIsRange);
                _spreadFlag.RegisterValueChangedCallback(evt =>
                {
                    model.spreadIsRange = evt.newValue;
                    Refresh();
                    Changed();
                });

                _spreadRow.Add(_spreadSlider);
                _spreadRow.Add(_spreadRange);
                _spreadRow.Add(_spreadFlag);
                _sub.Add(_spreadRow);
            }

            _timing = new MfvSegmentedControl("タイミング", "周期内", "周期ごと");
            _timing.AddToClassList("mfv-seg--compact");
            _timing.SetValueWithoutNotify((int)model.timing);
            _timing.RegisterValueChangedCallback(evt =>
            {
                model.timing = (MfvTimingMode)evt.newValue;
                Changed();
            });
            _sub.Add(_timing);

            _cancelOnReturn = new MfvToggleSwitch("復路でキャンセル");
            _cancelOnReturn.SetValueWithoutNotify(model.cancelOnReturn);
            _cancelOnReturn.RegisterValueChangedCallback(evt =>
            {
                model.cancelOnReturn = evt.newValue;
                Changed();
            });
            _sub.Add(_cancelOnReturn);

            _ownPhase = new MfvToggleSwitch("独自の動き");
            _ownPhase.SetValueWithoutNotify(model.useOwnPhase);
            _ownPhase.RegisterValueChangedCallback(evt =>
            {
                model.useOwnPhase = evt.newValue;
                Refresh();
                Changed();
            });
            _sub.Add(_ownPhase);

            _ownPhaseView = new MfvPhaseSettingsView(model.ownPhase, () =>
            {
                Refresh();
                Changed();
            }, compact: true);
            _sub.Add(_ownPhaseView);

            Add(_sub);
            Refresh();
        }

        /// <summary>The phase that actually drives this parameter.</summary>
        private MfvPhaseSettings GoverningPhase => _model.useOwnPhase ? _model.ownPhase : _clipPhase;

        private bool HasMultipleStops =>
            _isPalette
                ? (_paletteCount?.Invoke() ?? 0) >= 2
                : _model.HasMultipleStops;

        public void Refresh()
        {
            if (!_isPalette)
            {
                MfvPhaseSettingsView.Show(_valueSlider, !_model.isRange);
                MfvPhaseSettingsView.Show(_rangeSlider, _model.isRange);
                MfvPhaseSettingsView.Show(_spreadSlider, !_model.spreadIsRange);
                MfvPhaseSettingsView.Show(_spreadRange, _model.spreadIsRange);
            }

            var multiple = HasMultipleStops;
            var pingPong = GoverningPhase.mode == MfvPhaseMode.PingPong;

            MfvPhaseSettingsView.Show(_cancelOnReturn, pingPong);
            MfvPhaseSettingsView.Show(_timing, multiple);
            MfvPhaseSettingsView.Show(_ownPhase, multiple);
            MfvPhaseSettingsView.Show(_ownPhaseView, multiple && _model.useOwnPhase);
            _ownPhaseView.Refresh();

            // Hide the whole sub-panel when nothing inside it applies.
            var anyVisible = !_isPalette || multiple || pingPong;
            MfvPhaseSettingsView.Show(_sub, anyVisible);
        }

        private void Changed()
        {
            _onChanged?.Invoke();
        }
    }
}
