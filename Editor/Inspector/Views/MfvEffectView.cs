using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// One effect card. The body is built per <see cref="MfvEffectKind"/>. Everything the
    /// kinds share (header, description, copy / delete, collapse) lives in
    /// <see cref="MfvEffectCard"/>.
    /// </summary>
    public class MfvEffectView : VisualElement
    {
        private readonly MfvEffect _effect;

        /// <summary>A fresh effect of the same kind. Double clicking a slider thumb restores its value.</summary>
        private readonly MfvEffect _defaults;
        private readonly MfvPhaseSettings _clipPhase;
        private readonly Action _onChanged;
        private readonly List<MfvAnimatableView> _animatables = new List<MfvAnimatableView>();

        public MfvEffectView(
            MfvEffect effect,
            MfvPhaseSettings clipPhase,
            Action onChanged,
            Action onPaste,
            Action onDelete)
        {
            _effect = effect;
            _defaults = MfvEffect.Create(effect.kind);
            _clipPhase = clipPhase;
            _onChanged = onChanged;

            Card = new MfvEffectCard(
                MfvEffectCatalog.GetTitle(effect.kind, effect.parity),
                MfvEffectCatalog.GetDescription(effect.kind),
                showActions: true);

            // Copy takes the parameters, not the card: a kind may exist at most twice
            // on a clip (even / odd), so duplicating one is never a legal edit.
            Card.CopyRequested += () => MfvEffectClipboard.Copy(_effect);
            Card.PasteRequested += () => onPaste?.Invoke();
            Card.DeleteRequested += () => onDelete?.Invoke();

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MfvEffectClipboard.Changed += RefreshPasteAvailability;
                RefreshPasteAvailability();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
                MfvEffectClipboard.Changed -= RefreshPasteAvailability);
            Card.ExpandedChanged += expanded =>
            {
                effect.expanded = expanded;
                onChanged?.Invoke();
            };

            BuildBody(Card.Body);
            Card.SetChips(BuildChips());
            Card.Expanded = effect.expanded;
            RefreshPasteAvailability();

            Add(Card);
        }

        private void RefreshPasteAvailability()
        {
            Card.SetPasteAvailable(MfvEffectClipboard.CanPasteInto(_effect));
        }

        public MfvEffectCard Card { get; }

        public void Refresh()
        {
            foreach (var animatable in _animatables)
            {
                animatable.Refresh();
            }

            RefreshPasteAvailability();
            RefreshHooks?.Invoke();
        }

        private IEnumerable<Color> BuildChips()
        {
            var chips = new List<Color>();
            if (_effect.kind == MfvEffectKind.Color)
            {
                for (var i = 0; i < _effect.colorStops.Count && i < 4; i++)
                {
                    chips.Add(_effect.colorStops[i].Evaluate(0.5f));
                }
            }

            return chips;
        }

        private void BuildBody(VisualElement body)
        {
            switch (_effect.kind)
            {
                case MfvEffectKind.Move: BuildMove(body); break;
                case MfvEffectKind.Cone: BuildCone(body); break;
                case MfvEffectKind.Color: BuildColor(body); break;
                case MfvEffectKind.Brightness: BuildBrightness(body); break;
                case MfvEffectKind.Flicker: BuildFlicker(body); break;
                case MfvEffectKind.Gobo: BuildGobo(body); break;
            }
        }

        private MfvAnimatableView Animatable(
            string label,
            MfvAnimatableValue model,
            MfvAnimatableValue defaults,
            string unit = "",
            string format = "0.###",
            Func<Vector2, float[]> snaps = null)
        {
            var view = new MfvAnimatableView(label, model, _clipPhase, _onChanged, unit, format, snaps: snaps, defaults: defaults);
            _animatables.Add(view);
            return view;
        }

        // ------------------------------------------------------------- Move

        private void BuildMove(VisualElement body)
        {
            var tabs = new MfvTabControl(
                ("角度指定", MfvIcons.Angle),
                ("円", MfvIcons.Rotate360),
                ("ユーザー追跡", MfvIcons.Compass));
            tabs.SetValueWithoutNotify((int)_effect.moveMode);
            body.Add(tabs);

            var anglePane = new VisualElement();
            anglePane.AddToClassList("mfv-flow9");
            var tilt = Animatable("Tilt", _effect.tilt, _defaults.tilt, "°", "0.#", MfvSnapPoints.Angles);
            var pan = Animatable("Pan", _effect.pan, _defaults.pan, "°", "0.#", MfvSnapPoints.Angles);
            anglePane.Add(tilt);
            anglePane.Add(pan);

            var phaseOffset = new MfvValueSlider("位相差", new Vector2(0f, 360f), "°", "0")
            {
                DefaultValue = _defaults.panTiltPhaseOffsetDegrees,
            };
            phaseOffset.Snaps = MfvSnapPoints.Angles(phaseOffset.Limit);
            phaseOffset.SetValueWithoutNotify(_effect.panTiltPhaseOffsetDegrees);
            phaseOffset.RegisterValueChangedCallback(evt =>
            {
                _effect.panTiltPhaseOffsetDegrees = evt.newValue;
                _onChanged?.Invoke();
            });
            anglePane.Add(phaseOffset);
            body.Add(anglePane);

            var circlePane = new VisualElement();
            circlePane.AddToClassList("mfv-flow9");
            circlePane.Add(Animatable("中心 Tilt", _effect.circleCenterTilt, _defaults.circleCenterTilt, "°", "0.#", MfvSnapPoints.Angles));
            circlePane.Add(Animatable("中心 Pan", _effect.circleCenterPan, _defaults.circleCenterPan, "°", "0.#", MfvSnapPoints.Angles));
            circlePane.Add(Animatable("半径", _effect.circleRadius, _defaults.circleRadius, "°", "0.#", MfvSnapPoints.Angles));

            // 1 draws a true circle.
            var aspect = new MfvValueSlider("縦横比", new Vector2(0.1f, 4f), string.Empty, "0.##") 
            {
                Snaps = new[] { 1f },
                DefaultValue = _defaults.circleAspect,
            };
            aspect.SetValueWithoutNotify(_effect.circleAspect);
            aspect.RegisterValueChangedCallback(evt =>
            {
                _effect.circleAspect = evt.newValue;
                _onChanged?.Invoke();
            });
            circlePane.Add(aspect);
            body.Add(circlePane);

            var trackPane = new VisualElement();
            trackPane.AddToClassList("mfv-flow9");
            var userName = BuildTextRow("ユーザー名", _effect.trackUserName, text =>
            {
                _effect.trackUserName = text;
                _onChanged?.Invoke();
            });
            trackPane.Add(userName);

            var followSpeed = new MfvValueSlider("追従速度", new Vector2(0f, 20f), string.Empty, "0.#")
            {
                DefaultValue = _defaults.trackSpeed,
            };
            followSpeed.SetValueWithoutNotify(_effect.trackSpeed);
            followSpeed.RegisterValueChangedCallback(evt =>
            {
                _effect.trackSpeed = evt.newValue;
                _onChanged?.Invoke();
            });
            trackPane.Add(followSpeed);
            body.Add(trackPane);

            void ApplyTab()
            {
                var angle = _effect.moveMode == MfvMoveMode.Angle;
                MfvPhaseSettingsView.Show(anglePane, angle);
                MfvPhaseSettingsView.Show(circlePane, _effect.moveMode == MfvMoveMode.Circle);
                MfvPhaseSettingsView.Show(trackPane, _effect.moveMode == MfvMoveMode.TrackUser);
                // Phase offset only means something once both axes sweep a range.
                MfvPhaseSettingsView.Show(phaseOffset, angle && _effect.ShowPanTiltPhaseOffset);
            }

            tabs.RegisterValueChangedCallback(evt =>
            {
                _effect.moveMode = (MfvMoveMode)evt.newValue;
                ApplyTab();
                _onChanged?.Invoke();
            });

            RefreshHooks += ApplyTab;
            ApplyTab();
        }

        // ------------------------------------------------------------- Cone

        private void BuildCone(VisualElement body)
        {
            body.Add(Animatable("幅", _effect.coneWidth, _defaults.coneWidth, "°", "0.#", MfvSnapPoints.Angles));
            body.Add(Animatable("長さ", _effect.coneLength, _defaults.coneLength, "m", "0.#"));
        }

        // ------------------------------------------------------------ Color

        private void BuildColor(VisualElement body)
        {
            var palette = new MfvColorPalette(
                "パレット",
                _effect.colorStops,
                () => _effect.selectedColorStop,
                index => _effect.selectedColorStop = index);

            var view = new MfvAnimatableView(
                "パレット",
                _effect.colorPhasing,
                _clipPhase,
                () =>
                {
                    Card.SetChips(BuildChips());
                    _onChanged?.Invoke();
                },
                paletteRow: palette,
                paletteCount: () => _effect.colorStops.Count);

            palette.Changed += () =>
            {
                view.Refresh();
                Card.SetChips(BuildChips());
                _onChanged?.Invoke();
            };

            _animatables.Add(view);
            body.Add(view);
        }

        // ------------------------------------------------------- Brightness

        private void BuildBrightness(VisualElement body)
        {
            body.Add(Animatable("明るさ", _effect.brightness, _defaults.brightness, "%", "0"));
        }

        // ---------------------------------------------------------- Flicker

        private void BuildFlicker(VisualElement body)
        {
            body.Add(BuildSimpleSlider("速度", new Vector2(0f, 30f), _effect.flickerSpeed,
                _defaults.flickerSpeed, string.Empty, "0.#", v => _effect.flickerSpeed = v));
            body.Add(BuildSimpleSlider("強さ", new Vector2(0f, 100f), _effect.flickerStrength * 100f,
                _defaults.flickerStrength * 100f, "%", "0", v => _effect.flickerStrength = Mathf.Clamp01(v / 100f)));
            body.Add(BuildSimpleSlider("灯体間ズレ", new Vector2(0f, 2f), _effect.flickerFixtureStagger,
                _defaults.flickerFixtureStagger, string.Empty, "0.##", v => _effect.flickerFixtureStagger = v));
        }

        // ------------------------------------------------------------- Gobo

        private void BuildGobo(VisualElement body)
        {
            var palette = new MfvGoboPalette(
                "パレット",
                _effect.goboStops,
                () => _effect.selectedGoboStop,
                index => _effect.selectedGoboStop = index,
                () => _effect.goboPickerExpanded,
                open => _effect.goboPickerExpanded = open);

            var view = new MfvAnimatableView(
                "パレット",
                _effect.goboPhasing,
                _clipPhase,
                _onChanged,
                paletteRow: palette,
                paletteCount: () => _effect.goboStops.Count);

            palette.Changed += () =>
            {
                view.Refresh();
                _onChanged?.Invoke();
            };

            _animatables.Add(view);
            body.Add(view);

            var rotation = new MfvStepper("回転速度", "拍", 1f, "/ 1回転") { Minimum = 0f };
            rotation.SetValueWithoutNotify(_effect.goboRotationBeats);
            rotation.RegisterValueChangedCallback(evt =>
            {
                _effect.goboRotationBeats = evt.newValue;
                _onChanged?.Invoke();
            });
            body.Add(rotation);

            var goboStagger = BuildSimpleSlider("灯体間ズレ", new Vector2(0f, 360f), _effect.goboFixtureStaggerDegrees,
                _defaults.goboFixtureStaggerDegrees, "°", "0", v => _effect.goboFixtureStaggerDegrees = v);
            goboStagger.Snaps = MfvSnapPoints.Angles(goboStagger.Limit);
            body.Add(goboStagger);
        }

        // ------------------------------------------------------------ helpers

        private event Action RefreshHooks;

        private MfvValueSlider BuildSimpleSlider(
            string label,
            Vector2 limit,
            float initial,
            float defaultValue,
            string unit,
            string format,
            Action<float> setter)
        {
            var slider = new MfvValueSlider(label, limit, unit, format) { DefaultValue = defaultValue };
            slider.SetValueWithoutNotify(initial);
            slider.RegisterValueChangedCallback(evt =>
            {
                setter(evt.newValue);
                _onChanged?.Invoke();
            });
            return slider;
        }

        private static VisualElement BuildTextRow(string label, string initial, Action<string> setter)
        {
            var row = new VisualElement();
            row.AddToClassList("mfv-row");

            var labelElement = new Label(label);
            labelElement.AddToClassList("mfv-row__label");
            row.Add(labelElement);

            var field = new TextField { value = initial ?? string.Empty };
            field.AddToClassList("mfv-textbox");
            field.RegisterValueChangedCallback(evt => setter(evt.newValue));
            row.Add(field);

            return row;
        }
    }

}
