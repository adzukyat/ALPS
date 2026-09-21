using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// One effect card. The body is built per <see cref="AlpsEffectKind"/>. Everything the
    /// kinds share (header, description, copy / delete, collapse) lives in
    /// <see cref="AlpsEffectCard"/>.
    /// </summary>
    public class AlpsEffectView : VisualElement
    {
        private readonly AlpsEffect _effect;

        /// <summary>A fresh effect of the same kind. Double clicking a slider thumb restores its value.</summary>
        private readonly AlpsEffect _defaults;
        private readonly AlpsPhaseSettings _clipPhase;
        private readonly Action _onChanged;
        private readonly List<AlpsAnimatableView> _animatables = new List<AlpsAnimatableView>();

        public AlpsEffectView(
            AlpsEffect effect,
            AlpsPhaseSettings clipPhase,
            Action onChanged,
            Action onPaste,
            Action onDelete)
        {
            _effect = effect;
            _defaults = AlpsEffect.Create(effect.kind);
            _clipPhase = clipPhase;
            _onChanged = onChanged;

            Card = new AlpsEffectCard(
                AlpsEffectCatalog.GetTitle(effect.kind, effect.parity),
                AlpsEffectCatalog.GetDescription(effect.kind),
                showActions: true);

            // Copy takes the parameters, not the card: a kind may exist at most twice
            // on a clip (even / odd), so duplicating one is never a legal edit.
            Card.CopyRequested += () => AlpsEffectClipboard.Copy(_effect);
            Card.PasteRequested += () => onPaste?.Invoke();
            Card.DeleteRequested += () => onDelete?.Invoke();

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                AlpsEffectClipboard.Changed += RefreshPasteAvailability;
                RefreshPasteAvailability();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
                AlpsEffectClipboard.Changed -= RefreshPasteAvailability);
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
            Card.SetPasteAvailable(AlpsEffectClipboard.CanPasteInto(_effect));
        }

        public AlpsEffectCard Card { get; }

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
            if (_effect.kind == AlpsEffectKind.Color)
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
                case AlpsEffectKind.Move: BuildMove(body); break;
                case AlpsEffectKind.Cone: BuildCone(body); break;
                case AlpsEffectKind.Color: BuildColor(body); break;
                case AlpsEffectKind.Brightness: BuildBrightness(body); break;
                case AlpsEffectKind.Flicker: BuildFlicker(body); break;
                case AlpsEffectKind.Gobo: BuildGobo(body); break;
            }
        }

        private AlpsAnimatableView Animatable(
            string label,
            AlpsAnimatableValue model,
            AlpsAnimatableValue defaults,
            string unit = "",
            string format = "0.###",
            Func<Vector2, float[]> snaps = null)
        {
            var view = new AlpsAnimatableView(label, model, _clipPhase, _onChanged, unit, format, snaps: snaps, defaults: defaults);
            _animatables.Add(view);
            return view;
        }

        // ------------------------------------------------------------- Move

        private void BuildMove(VisualElement body)
        {
            var tabs = new AlpsTabControl(
                ("角度指定", AlpsIcons.Angle),
                ("円", AlpsIcons.Rotate360),
                ("ユーザー追跡", AlpsIcons.Compass));
            tabs.SetValueWithoutNotify((int)_effect.moveMode);
            body.Add(tabs);

            var anglePane = new VisualElement();
            anglePane.AddToClassList("alps-flow9");
            var tilt = Animatable("Tilt", _effect.tilt, _defaults.tilt, "°", "0.#", AlpsSnapPoints.Angles);
            var pan = Animatable("Pan", _effect.pan, _defaults.pan, "°", "0.#", AlpsSnapPoints.Angles);
            anglePane.Add(tilt);
            anglePane.Add(pan);

            var phaseOffset = new AlpsValueSlider("位相差", new Vector2(0f, 360f), "°", "0")
            {
                DefaultValue = _defaults.panTiltPhaseOffsetDegrees,
            };
            phaseOffset.Snaps = AlpsSnapPoints.Angles(phaseOffset.Limit);
            phaseOffset.SetValueWithoutNotify(_effect.panTiltPhaseOffsetDegrees);
            phaseOffset.RegisterValueChangedCallback(evt =>
            {
                _effect.panTiltPhaseOffsetDegrees = evt.newValue;
                _onChanged?.Invoke();
            });
            anglePane.Add(phaseOffset);
            body.Add(anglePane);

            var circlePane = new VisualElement();
            circlePane.AddToClassList("alps-flow9");
            circlePane.Add(Animatable("中心 Tilt", _effect.circleCenterTilt, _defaults.circleCenterTilt, "°", "0.#", AlpsSnapPoints.Angles));
            circlePane.Add(Animatable("中心 Pan", _effect.circleCenterPan, _defaults.circleCenterPan, "°", "0.#", AlpsSnapPoints.Angles));
            circlePane.Add(Animatable("半径", _effect.circleRadius, _defaults.circleRadius, "°", "0.#", AlpsSnapPoints.Angles));

            // 1 draws a true circle.
            var aspect = new AlpsValueSlider("縦横比", new Vector2(0.1f, 4f), string.Empty, "0.##") 
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
            trackPane.AddToClassList("alps-flow9");
            var userName = BuildTextRow("ユーザー名", _effect.trackUserName, text =>
            {
                _effect.trackUserName = text;
                _onChanged?.Invoke();
            });
            trackPane.Add(userName);

            var followSpeed = new AlpsValueSlider("追従速度", new Vector2(0f, 20f), string.Empty, "0.#")
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
                var angle = _effect.moveMode == AlpsMoveMode.Angle;
                AlpsPhaseSettingsView.Show(anglePane, angle);
                AlpsPhaseSettingsView.Show(circlePane, _effect.moveMode == AlpsMoveMode.Circle);
                AlpsPhaseSettingsView.Show(trackPane, _effect.moveMode == AlpsMoveMode.TrackUser);
                // Phase offset only means something once both axes sweep a range.
                AlpsPhaseSettingsView.Show(phaseOffset, angle && _effect.ShowPanTiltPhaseOffset);
            }

            tabs.RegisterValueChangedCallback(evt =>
            {
                _effect.moveMode = (AlpsMoveMode)evt.newValue;
                ApplyTab();
                _onChanged?.Invoke();
            });

            RefreshHooks += ApplyTab;
            ApplyTab();
        }

        // ------------------------------------------------------------- Cone

        private void BuildCone(VisualElement body)
        {
            body.Add(Animatable("幅", _effect.coneWidth, _defaults.coneWidth, "°", "0.#", AlpsSnapPoints.Angles));
            body.Add(Animatable("長さ", _effect.coneLength, _defaults.coneLength, "m", "0.#"));
        }

        // ------------------------------------------------------------ Color

        private void BuildColor(VisualElement body)
        {
            var palette = new AlpsColorPalette(
                "パレット",
                _effect.colorStops,
                () => _effect.selectedColorStop,
                index => _effect.selectedColorStop = index);

            var view = new AlpsAnimatableView(
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
            AlpsAnimatableView view = null;

            // Effects saved before brightness reached 200% still carry the old limit.
            _effect.brightness.limit = _defaults.brightness.limit;

            // Untitled frame for the fades, which run inside the outbound leg.
            var fadeFrame = new VisualElement();
            fadeFrame.AddToClassList("alps-sub");
            var fade = new AlpsFadeSlider("フェード", 50f, "%", "0")
            {
                Snaps = new[] { 25f },
                DefaultValue = new Vector2(_defaults.blackoutFadeIn, _defaults.blackoutFadeOut) * 100f,
            };
            fade.SetValueWithoutNotify(new Vector2(_effect.blackoutFadeIn, _effect.blackoutFadeOut) * 100f);
            fade.RegisterValueChangedCallback(evt =>
            {
                _effect.blackoutFadeIn = evt.newValue.x / 100f;
                _effect.blackoutFadeOut = evt.newValue.y / 100f;
                _onChanged?.Invoke();
            });
            fadeFrame.Add(fade);

            var blackout = new AlpsToggleSwitch("復路で消灯");
            blackout.SetValueWithoutNotify(_effect.blackoutOnReturn);
            blackout.RegisterValueChangedCallback(evt =>
            {
                _effect.blackoutOnReturn = evt.newValue;
                ApplyBlackout();
                _onChanged?.Invoke();
            });

            // The return leg belongs to whichever phase drives brightness, so switching
            // own phase or its mode inside the row has to re-evaluate the switch too.
            view = new AlpsAnimatableView(
                "明るさ",
                _effect.brightness,
                _clipPhase,
                () =>
                {
                    ApplyBlackout();
                    _onChanged?.Invoke();
                },
                "%",
                "0",
                snaps: limit => AlpsSnapPoints.Multiples(limit, 100f),
                defaults: _defaults.brightness);
            _animatables.Add(view);
            body.Add(view);
            body.Add(blackout);
            body.Add(fadeFrame);

            void ApplyBlackout()
            {
                var pingPong = view.GoverningPhase.mode == AlpsPhaseMode.PingPong;
                AlpsPhaseSettingsView.Show(blackout, pingPong);
                AlpsPhaseSettingsView.Show(fadeFrame, pingPong && _effect.blackoutOnReturn);
            }

            RefreshHooks += ApplyBlackout;
            ApplyBlackout();
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
            var palette = new AlpsGoboPalette(
                "パレット",
                _effect.goboStops,
                () => _effect.selectedGoboStop,
                index => _effect.selectedGoboStop = index,
                () => _effect.goboPickerExpanded,
                open => _effect.goboPickerExpanded = open);

            var view = new AlpsAnimatableView(
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

            var rotation = new AlpsStepper("回転速度", "拍", 1f, "/ 1回転") { Minimum = 0f };
            rotation.SetValueWithoutNotify(_effect.goboRotationBeats);
            rotation.RegisterValueChangedCallback(evt =>
            {
                _effect.goboRotationBeats = evt.newValue;
                _onChanged?.Invoke();
            });
            body.Add(rotation);

            var goboStagger = BuildSimpleSlider("灯体間ズレ", new Vector2(0f, 360f), _effect.goboFixtureStaggerDegrees,
                _defaults.goboFixtureStaggerDegrees, "°", "0", v => _effect.goboFixtureStaggerDegrees = v);
            goboStagger.Snaps = AlpsSnapPoints.Angles(goboStagger.Limit);
            body.Add(goboStagger);
        }

        // ------------------------------------------------------------ helpers

        private event Action RefreshHooks;

        private AlpsValueSlider BuildSimpleSlider(
            string label,
            Vector2 limit,
            float initial,
            float defaultValue,
            string unit,
            string format,
            Action<float> setter)
        {
            var slider = new AlpsValueSlider(label, limit, unit, format) { DefaultValue = defaultValue };
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
            row.AddToClassList("alps-row");

            var labelElement = new Label(label);
            labelElement.AddToClassList("alps-row__label");
            row.Add(labelElement);

            var field = new TextField { value = initial ?? string.Empty };
            field.AddToClassList("alps-textbox");
            field.RegisterValueChangedCallback(evt => setter(evt.newValue));
            row.Add(field);

            return row;
        }
    }

}
