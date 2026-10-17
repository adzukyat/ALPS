using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// One effect card. The body is built per <see cref="AlpsEffectKind"/>. Everything the
    /// kinds share (header, description, copy / delete, collapse) lives in
    /// <see cref="AlpsEffectCard"/>. Under it hang the own motion cards of its parameters,
    /// which fold away with it.
    /// </summary>
    public class AlpsEffectView : VisualElement
    {
        /// <summary>Longest brightness fade either side of a clip, in beats.</summary>
        public const float MaxFadeBeats = 8f;

        private readonly AlpsEffect _effect;

        /// <summary>A fresh effect of the same kind. Double clicking a slider thumb restores its value.</summary>
        private readonly AlpsEffect _defaults;
        private readonly AlpsPhaseSettings _clipPhase;
        private readonly Action _onChanged;
        private readonly List<AlpsAnimatableView> _animatables = new List<AlpsAnimatableView>();
        private readonly AlpsMixedValues _mixed;
        private Action _refreshPhaseOffset;

        public AlpsEffectView(
            AlpsEffect effect,
            AlpsPhaseSettings clipPhase,
            Action onChanged,
            Action onPaste,
            Action onDelete,
            AlpsMixedValues mixed = null)
        {
            _effect = effect;
            _mixed = mixed;
            _defaults = AlpsEffect.Create(effect.kind);
            _clipPhase = clipPhase;

            // Any edit on the card can start or stop something following a phase, which is
            // what the phase offset row waits for.
            _onChanged = () =>
            {
                _refreshPhaseOffset?.Invoke();
                onChanged?.Invoke();
            };

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
            BuildPhaseOffset(Card.Body);
            Card.SetChips(BuildChips());

            var motions = new VisualElement();
            foreach (var animatable in _animatables)
            {
                motions.Add(animatable.MotionCard);
            }

            Card.ExpandedChanged += expanded => AlpsPhaseSettingsView.Show(motions, expanded);
            Card.Expanded = effect.expanded;
            RefreshPasteAvailability();

            Add(Card);
            Add(motions);
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
            var view = new AlpsAnimatableView(label, model, _clipPhase, _onChanged, unit, format, snaps: snaps, defaults: defaults, mixed: _mixed);
            _animatables.Add(view);
            return view;
        }

        // ------------------------------------------------------------- Move

        private void BuildMove(VisualElement body)
        {
            var tabs = new AlpsTabControl(
                (AlpsStrings.Tr("effect.move.mode.angle"), AlpsIcons.Angle),
                (AlpsStrings.Tr("effect.move.mode.circle"), AlpsIcons.Rotate360),
                (AlpsStrings.Tr("effect.move.mode.track"), AlpsIcons.Compass));
            tabs.SetValueWithoutNotify((int)_effect.moveMode);
            _mixed?.Bind(tabs, _effect, nameof(AlpsEffect.moveMode));
            body.Add(tabs);

            var anglePane = new VisualElement();
            anglePane.AddToClassList("alps-flow9");
            var tilt = Animatable("Tilt", _effect.tilt, _defaults.tilt, "°", "0.#", AlpsSnapPoints.Angles);
            var pan = Animatable("Pan", _effect.pan, _defaults.pan, "°", "0.#", AlpsSnapPoints.Angles);
            anglePane.Add(tilt);
            anglePane.Add(pan);

            var phaseOffset = new AlpsValueSlider(AlpsStrings.Tr("effect.move.phaseDiff"), new Vector2(0f, 360f), "°", "0")
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
            _mixed?.Bind(phaseOffset, _effect, nameof(AlpsEffect.panTiltPhaseOffsetDegrees));
            anglePane.Add(phaseOffset);
            body.Add(anglePane);

            var circlePane = new VisualElement();
            circlePane.AddToClassList("alps-flow9");
            var circleValues = new[]
            {
                Animatable(AlpsStrings.Tr("effect.move.centerTilt"), _effect.circleCenterTilt, _defaults.circleCenterTilt, "°", "0.#", AlpsSnapPoints.Angles),
                Animatable(AlpsStrings.Tr("effect.move.centerPan"), _effect.circleCenterPan, _defaults.circleCenterPan, "°", "0.#", AlpsSnapPoints.Angles),
                Animatable(AlpsStrings.Tr("common.radius"), _effect.circleRadius, _defaults.circleRadius, "°", "0.#", AlpsSnapPoints.Angles),
            };
            foreach (var value in circleValues)
            {
                circlePane.Add(value);
            }


            // 1 draws a true circle.
            var aspect = new AlpsValueSlider(AlpsStrings.Tr("effect.move.aspect"), new Vector2(0.1f, 4f), string.Empty, "0.##")
            {
                Snaps = new[] { 1f },
                DefaultValue = _defaults.circleAspect,
                AllowAboveLimit = true,
            };
            aspect.SetValueWithoutNotify(_effect.circleAspect);
            aspect.RegisterValueChangedCallback(evt =>
            {
                _effect.circleAspect = evt.newValue;
                _onChanged?.Invoke();
            });
            _mixed?.Bind(aspect, _effect, nameof(AlpsEffect.circleAspect));
            circlePane.Add(aspect);
            body.Add(circlePane);

            var trackPane = new VisualElement();
            trackPane.AddToClassList("alps-flow9");
            var userName = BuildTextRow(AlpsStrings.Tr("effect.move.userName"), _effect.trackUserName, text =>
            {
                _effect.trackUserName = text;
                _onChanged?.Invoke();
            }, out var userNameField);
            _mixed?.Bind(userNameField, _effect, nameof(AlpsEffect.trackUserName));
            trackPane.Add(userName);

            var followSpeed = new AlpsValueSlider(AlpsStrings.Tr("effect.move.followSpeed"), new Vector2(0f, 20f), string.Empty, "0.#")
            {
                DefaultValue = _defaults.trackSpeed,
                AllowAboveLimit = true,
            };
            followSpeed.SetValueWithoutNotify(_effect.trackSpeed);
            followSpeed.RegisterValueChangedCallback(evt =>
            {
                _effect.trackSpeed = evt.newValue;
                _onChanged?.Invoke();
            });
            _mixed?.Bind(followSpeed, _effect, nameof(AlpsEffect.trackSpeed));
            trackPane.Add(followSpeed);
            body.Add(trackPane);

            void ApplyTab()
            {
                var angle = _effect.moveMode == AlpsMoveMode.Angle;
                AlpsPhaseSettingsView.Show(anglePane, angle);
                AlpsPhaseSettingsView.Show(circlePane, _effect.moveMode == AlpsMoveMode.Circle);
                AlpsPhaseSettingsView.Show(trackPane, _effect.moveMode == AlpsMoveMode.TrackUser);

                // A motion card under the card follows its row in and out with the tab.
                tilt.RowShown = angle;
                pan.RowShown = angle;
                foreach (var value in circleValues)
                {
                    value.RowShown = _effect.moveMode == AlpsMoveMode.Circle;
                }

                // Phase offset only means something once both axes sweep a range, over a cycle.
                AlpsPhaseSettingsView.Show(phaseOffset, angle && _effect.ShowPanTiltPhaseOffset && _clipPhase.mode != AlpsPhaseMode.InOut);
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
            // Effects saved before the cone could outgrow the fixture's mesh still carry the old limit.
            _effect.coneLength.limit = _defaults.coneLength.limit;

            body.Add(Animatable(AlpsStrings.Tr("common.width"), _effect.coneWidth, _defaults.coneWidth, "°", "0.#", AlpsSnapPoints.Angles));
            // The tick marks where the cone fills the fixture's own mesh. Past it the mesh stretches.
            body.Add(Animatable(AlpsStrings.Tr("effect.cone.length"), _effect.coneLength, _defaults.coneLength, "m", "0.#",
                _ => new[] { AlpsShowPlayer.ModelConeLengthLimit }));
        }

        // ------------------------------------------------------------ Color

        private void BuildColor(VisualElement body)
        {
            var palette = new AlpsColorPalette(
                AlpsStrings.Tr("common.palette"),
                _effect.colorStops,
                () => _effect.selectedColorStop,
                index => _effect.selectedColorStop = index);
            _mixed?.BindDisplay(palette, palette.SetMixed, _effect, nameof(AlpsEffect.colorStops));

            var view = new AlpsAnimatableView(
                AlpsStrings.Tr("common.palette"),
                _effect.colorPhasing,
                _clipPhase,
                () =>
                {
                    Card.SetChips(BuildChips());
                    _onChanged?.Invoke();
                },
                paletteRow: palette,
                paletteCount: () => _effect.colorStops.Count,
                mixed: _mixed,
                motionName: AlpsStrings.Tr("effect.color.palette"));

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
            var fade = new AlpsFadeSlider(AlpsStrings.Tr("common.fade"), 50f, "%", "0")
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
            _mixed?.Bind(fade, _effect, nameof(AlpsEffect.blackoutFadeIn), nameof(AlpsEffect.blackoutFadeOut));
            fadeFrame.Add(fade);

            var blackout = new AlpsToggleSwitch(AlpsStrings.Tr("effect.brightness.blackoutReturn"));
            blackout.SetValueWithoutNotify(_effect.blackoutOnReturn);
            blackout.RegisterValueChangedCallback(evt =>
            {
                _effect.blackoutOnReturn = evt.newValue;
                ApplyBlackout();
                _onChanged?.Invoke();
            });
            _mixed?.Bind(blackout, _effect, nameof(AlpsEffect.blackoutOnReturn));

            // The return leg belongs to whichever phase drives brightness, so switching
            // own phase or its mode inside the row has to re-evaluate the switch too.
            view = new AlpsAnimatableView(
                AlpsStrings.Tr("effect.brightness.name"),
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
                defaults: _defaults.brightness,
                mixed: _mixed);
            _animatables.Add(view);
            body.Add(view);

            // The fade over the clip in beats, separate from Timeline's ease in and out, which
            // is set in seconds and blends with the layers below.
            var clipFade = new AlpsFadeSlider(AlpsStrings.Tr("effect.brightness.fade"), MaxFadeBeats, AlpsStrings.Tr("unit.beats"), "0.##")
            {
                Snaps = new[] { 1f, 2f, 4f },
                DefaultValue = new Vector2(_defaults.fadeInBeats, _defaults.fadeOutBeats),
                tooltip = AlpsStrings.Tr("effect.brightness.fade.tip"),
            };
            clipFade.SetValueWithoutNotify(new Vector2(_effect.fadeInBeats, _effect.fadeOutBeats));
            clipFade.RegisterValueChangedCallback(evt =>
            {
                _effect.fadeInBeats = evt.newValue.x;
                _effect.fadeOutBeats = evt.newValue.y;
                _onChanged?.Invoke();
            });
            _mixed?.Bind(clipFade, _effect, nameof(AlpsEffect.fadeInBeats), nameof(AlpsEffect.fadeOutBeats));
            body.Add(clipFade);

            body.Add(blackout);
            body.Add(fadeFrame);

            void ApplyBlackout()
            {
                var phase = view.GoverningPhase;
                var returns = phase.mode == AlpsPhaseMode.Wave && AlpsPhaseCurve.Returns(phase.rise, phase.holdHigh, phase.fall);
                AlpsPhaseSettingsView.Show(blackout, returns);
                AlpsPhaseSettingsView.Show(fadeFrame, returns && _effect.blackoutOnReturn);
            }

            RefreshHooks += ApplyBlackout;
            ApplyBlackout();
        }

        // ---------------------------------------------------------- Flicker

        private void BuildFlicker(VisualElement body)
        {
            var speed = BuildSimpleSlider(AlpsStrings.Tr("common.speed"), new Vector2(0f, 30f), _effect.flickerSpeed,
                _defaults.flickerSpeed, string.Empty, "0.#", v => _effect.flickerSpeed = v,
                nameof(AlpsEffect.flickerSpeed));
            speed.AllowAboveLimit = true;
            body.Add(speed);
            body.Add(BuildSimpleSlider(AlpsStrings.Tr("effect.flicker.strength"), new Vector2(0f, 100f), _effect.flickerStrength * 100f,
                _defaults.flickerStrength * 100f, "%", "0", v => _effect.flickerStrength = Mathf.Clamp01(v / 100f),
                nameof(AlpsEffect.flickerStrength)));
            var stagger = BuildSimpleSlider(AlpsStrings.Tr("effect.fixtureStagger"), new Vector2(0f, 2f), _effect.flickerFixtureStagger,
                _defaults.flickerFixtureStagger, string.Empty, "0.##", v => _effect.flickerFixtureStagger = v,
                nameof(AlpsEffect.flickerFixtureStagger));
            stagger.AllowAboveLimit = true;
            body.Add(stagger);
        }

        // ------------------------------------------------------------- Gobo

        private void BuildGobo(VisualElement body)
        {
            var palette = new AlpsGoboPalette(
                AlpsStrings.Tr("common.palette"),
                _effect.goboStops,
                () => _effect.selectedGoboStop,
                index => _effect.selectedGoboStop = index,
                () => _effect.goboPickerExpanded,
                open => _effect.goboPickerExpanded = open);
            _mixed?.BindDisplay(palette, palette.SetMixed, _effect, nameof(AlpsEffect.goboStops));

            var view = new AlpsAnimatableView(
                AlpsStrings.Tr("common.palette"),
                _effect.goboPhasing,
                _clipPhase,
                _onChanged,
                paletteRow: palette,
                paletteCount: () => _effect.goboStops.Count,
                mixed: _mixed,
                motionName: AlpsStrings.Tr("effect.gobo.palette"));

            palette.Changed += () =>
            {
                view.Refresh();
                _onChanged?.Invoke();
            };

            _animatables.Add(view);
            body.Add(view);

            // On shows the projection, which the model stores as off so effects saved before it load on.
            var projection = new AlpsToggleSwitch(AlpsStrings.Tr("effect.gobo.projection"));
            projection.SetValueWithoutNotify(!_effect.goboProjectionOff);
            projection.RegisterValueChangedCallback(evt =>
            {
                _effect.goboProjectionOff = !evt.newValue;
                _onChanged?.Invoke();
            });
            _mixed?.Bind(projection, _effect, nameof(AlpsEffect.goboProjectionOff));
            body.Add(projection);

            var rotation = new AlpsStepper(AlpsStrings.Tr("effect.gobo.rotationSpeed"), AlpsStrings.Tr("unit.beats"), 1f, AlpsStrings.Tr("effect.gobo.perTurn")) { Minimum = 0f, Subdivides = true };
            rotation.SetValueWithoutNotify(_effect.goboRotationBeats);
            rotation.RegisterValueChangedCallback(evt =>
            {
                _effect.goboRotationBeats = evt.newValue;
                _onChanged?.Invoke();
            });
            _mixed?.Bind(rotation, _effect, nameof(AlpsEffect.goboRotationBeats));
            body.Add(rotation);

            var goboStagger = BuildSimpleSlider(AlpsStrings.Tr("effect.fixtureStagger"), new Vector2(0f, 360f), _effect.goboFixtureStaggerDegrees,
                _defaults.goboFixtureStaggerDegrees, "°", "0", v => _effect.goboFixtureStaggerDegrees = v,
                nameof(AlpsEffect.goboFixtureStaggerDegrees));
            goboStagger.Snaps = AlpsSnapPoints.Angles(goboStagger.Limit);
            goboStagger.AllowAboveLimit = true;
            body.Add(goboStagger);
        }

        // ------------------------------------------------------- phase offset

        /// <summary>
        /// The card's phase offset, last on every card whose values can follow a phase. It
        /// shows only while something on the card does, since nothing else would move.
        /// </summary>
        private void BuildPhaseOffset(VisualElement body)
        {
            if (_effect.kind == AlpsEffectKind.Flicker)
            {
                return;
            }

            var offset = new AlpsValueSlider(AlpsStrings.Tr("effect.phaseOffset"), new Vector2(0f, 100f), "%", "0")
            {
                Snaps = new[] { 25f, 50f, 75f },
                DefaultValue = _defaults.phaseOffset * 100f,
                tooltip = AlpsStrings.Tr("effect.phaseOffset.tip"),
            };
            offset.SetValueWithoutNotify(_effect.phaseOffset * 100f);
            offset.RegisterValueChangedCallback(evt =>
            {
                _effect.phaseOffset = Mathf.Clamp01(evt.newValue / 100f);
                _onChanged?.Invoke();
            });
            _mixed?.Bind(offset, _effect, nameof(AlpsEffect.phaseOffset));
            body.Add(offset);

            // An in / out phase has no cycle to run late by.
            _refreshPhaseOffset = () => AlpsPhaseSettingsView.Show(offset, FollowsPhase() && _clipPhase.mode != AlpsPhaseMode.InOut);
            RefreshHooks += _refreshPhaseOffset;
            _refreshPhaseOffset();
        }

        /// <summary>True while any value on the card moves with a phase, which the offset then shifts.</summary>
        private bool FollowsPhase()
        {
            switch (_effect.kind)
            {
                case AlpsEffectKind.Move:
                    if (_effect.moveMode == AlpsMoveMode.Circle)
                    {
                        return true;
                    }

                    return _effect.moveMode == AlpsMoveMode.Angle
                           && (_effect.tilt.HasMultipleStops || _effect.pan.HasMultipleStops);
                case AlpsEffectKind.Cone:
                    return _effect.coneWidth.HasMultipleStops || _effect.coneLength.HasMultipleStops;
                case AlpsEffectKind.Color:
                    // A single gradient stop is walked by the phase as well.
                    return _effect.colorStops.Count >= 2
                           || (_effect.colorStops.Count == 1 && _effect.colorStops[0].isGradient);
                case AlpsEffectKind.Brightness:
                    return _effect.brightness.HasMultipleStops || _effect.blackoutOnReturn;
                case AlpsEffectKind.Gobo:
                    return _effect.goboStops.Count >= 2;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when something on <paramref name="effect"/> moves with the clip's shared phase:
        /// a range or a palette that does not run on its own phase, a circle, or a blackout on
        /// the return of a brightness that follows the clip.
        /// </summary>
        public static bool FollowsClipPhase(AlpsEffect effect)
        {
            bool Shared(AlpsAnimatableValue value) => value.HasMultipleStops && !value.useOwnPhase;

            switch (effect.kind)
            {
                case AlpsEffectKind.Move:
                    if (effect.moveMode == AlpsMoveMode.Circle)
                    {
                        return true;
                    }

                    return effect.moveMode == AlpsMoveMode.Angle && (Shared(effect.tilt) || Shared(effect.pan));
                case AlpsEffectKind.Cone:
                    return Shared(effect.coneWidth) || Shared(effect.coneLength);
                case AlpsEffectKind.Color:
                    // A single gradient stop is walked by the phase as well.
                    return !effect.colorPhasing.useOwnPhase
                           && (effect.colorStops.Count >= 2
                               || (effect.colorStops.Count == 1 && effect.colorStops[0].isGradient));
                case AlpsEffectKind.Brightness:
                    return !effect.brightness.useOwnPhase
                           && (effect.brightness.HasMultipleStops || effect.blackoutOnReturn);
                case AlpsEffectKind.Gobo:
                    return !effect.goboPhasing.useOwnPhase && effect.goboStops.Count >= 2;
                default:
                    return false;
            }
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
            Action<float> setter,
            string field)
        {
            var slider = new AlpsValueSlider(label, limit, unit, format) { DefaultValue = defaultValue };
            slider.SetValueWithoutNotify(initial);
            slider.RegisterValueChangedCallback(evt =>
            {
                setter(evt.newValue);
                _onChanged?.Invoke();
            });
            _mixed?.Bind(slider, _effect, field);
            return slider;
        }

        private static VisualElement BuildTextRow(string label, string initial, Action<string> setter, out TextField field)
        {
            var row = new VisualElement();
            row.AddToClassList("alps-row");

            var labelElement = new Label(label);
            labelElement.AddToClassList("alps-row__label");
            row.Add(labelElement);

            var text = new TextField { value = initial ?? string.Empty };
            text.AddToClassList("alps-textbox");
            text.RegisterValueChangedCallback(evt =>
            {
                // The base setter leaves the dash up on 2022.3.22, see AlpsMixedField.
                text.showMixedValue = false;
                setter(evt.newValue);
            });
            field = text;
            row.Add(text);

            return row;
        }
    }

}
