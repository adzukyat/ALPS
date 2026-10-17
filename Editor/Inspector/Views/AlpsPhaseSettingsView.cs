using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Shared settings: the mode tabs over the graph preview, then the repeating wave's shape
    /// and distribution or the in / out beats, easing, fixture group size, spread, speed,
    /// fire chance and the length it stretches, invert.
    ///
    /// The same view is reused verbatim under a parameter that turns own phase on,
    /// which is why it takes a plain <see cref="AlpsPhaseSettings"/> rather than reaching
    /// for the clip.
    /// </summary>
    public class AlpsPhaseSettingsView : VisualElement
    {
        /// <summary>Two full trips across the group in one cycle, which is as far as a wave reads.</summary>
        private const float SpreadLimit = 200f;

        /// <summary>The delay in beats reaches four bars of four, far past any cycle it staggers.</summary>
        private const float SpreadBeatsLimit = 16f;

        /// <summary>Longest in or out of an in / out phase, four bars of four.</summary>
        public const float MaxInOutBeats = 16f;

        /// <summary>Musical divisions the trip across the group can be made to land on.</summary>
        private static readonly float[] BeatSnaps = { 1f, 2f, 4f, 0.5f };

        /// <summary>The mode each tab picks, left to right. The enum order is load bearing, so the tabs map onto it.</summary>
        private static readonly AlpsPhaseMode[] TabModes = { AlpsPhaseMode.InOut, AlpsPhaseMode.Wave, AlpsPhaseMode.Random };

        /// <summary>
        /// Shapes of the repeating wave, the shape control's indexes. They are not saved: a
        /// sweep is a wave without a fall, a pulse one without a rise, and a ping-pong has both.
        /// </summary>
        public const int ShapeSweep = 0;
        public const int ShapePulse = 1;
        public const int ShapePingPong = 2;

        private readonly AlpsPhaseSettings _settings;
        private readonly Action _onChanged;

        private readonly AlpsTabControl _tabs;
        private readonly AlpsPhaseGraph _graph;
        private readonly AlpsSegmentedControl _shape;
        private readonly AlpsEasingPicker _easing;
        private readonly AlpsShareBar _shares;
        private readonly AlpsSegmentedControl _length;
        private readonly AlpsFadeSlider _inOut;
        private readonly AlpsStepper _speed;
        private readonly AlpsValueSlider _spread;
        private readonly AlpsStepper _spreadBeats;
        private readonly AlpsRangeFlag _beatsFlag;
        private readonly AlpsValueSlider _fireChance;
        private readonly AlpsToggleSwitch _inverse;

        /// <summary>The speed the delay's marks were last built for. NaN forces the first build.</summary>
        private float _snapBeats = float.NaN;

        /// <summary>
        /// The shape shown. Kept while the shares still fit it, so a ping-pong whose fall is
        /// dragged to zero stays a ping-pong until the view is built again.
        /// </summary>
        private int _shapeValue;

        public AlpsPhaseSettingsView(AlpsPhaseSettings settings, Action onChanged, bool compact = false, AlpsMixedValues mixed = null)
        {
            _settings = settings;
            _onChanged = onChanged;

            var defaults = new AlpsPhaseSettings();

            if (compact)
            {
                AddToClassList("alps-nested");
            }
            else
            {
                // The view sits inside a card body, so its rows carry the body's 9px rhythm.
                AddToClassList("alps-flow9");
            }

            // The mode picks what the graph below draws, so the tabs head it.
            _tabs = new AlpsTabControl(
                (AlpsStrings.Tr("phase.mode.inOut"), AlpsIcons.CircuitCapacitor),
                (AlpsStrings.Tr("phase.mode.wave"), AlpsIcons.ArrowBounce),
                (AlpsStrings.Tr("phase.mode.random"), AlpsIcons.CircuitBattery));
            if (compact)
            {
                _tabs.AddToClassList("alps-tabs--compact");
            }

            _tabs.SetValueWithoutNotify(TabOf(settings.mode));
            _tabs.RegisterValueChangedCallback(evt =>
            {
                settings.mode = TabModes[evt.newValue];
                if (settings.mode == AlpsPhaseMode.InOut)
                {
                    settings.SeedSpreadBeats();
                }

                Changed();
            });
            mixed?.Bind(_tabs, settings, nameof(AlpsPhaseSettings.mode));
            Add(_tabs);

            _graph = new AlpsPhaseGraph();
            if (compact)
            {
                _graph.AddToClassList("alps-graph--compact");
            }

            Add(_graph);

            // The shape is read off the distribution, which would otherwise have to be dragged
            // into a sawtooth by hand. Picking one rearranges the shares to fit.
            _shapeValue = ShapeOf(settings);
            _shape = new AlpsSegmentedControl(
                AlpsStrings.Tr("phase.mode"),
                AlpsStrings.Tr("phase.shape.sweep"),
                AlpsStrings.Tr("phase.shape.pulse"),
                AlpsStrings.Tr("phase.shape.pingPong"))
            {
                tooltip = AlpsStrings.Tr("phase.shape.tip"),
            };
            if (compact)
            {
                _shape.AddToClassList("alps-seg--compact");
            }

            _shape.SetValueWithoutNotify(_shapeValue);
            _shape.RegisterValueChangedCallback(evt => SetShape(evt.newValue));
            mixed?.Bind(
                _shape,
                settings,
                nameof(AlpsPhaseSettings.rise),
                nameof(AlpsPhaseSettings.holdHigh),
                nameof(AlpsPhaseSettings.fall));
            Add(_shape);

            // How long the wave rises, holds at the top, falls and holds at the bottom. A rise
            // over the whole cycle is a sawtooth, and zero length ramps make a square wave.
            _shares = new AlpsShareBar(AlpsStrings.Tr("phase.shares"))
            {
                DefaultValue = new Vector3(defaults.rise, defaults.holdHigh, defaults.fall),
            };
            _shares.SetValueWithoutNotify(new Vector3(settings.rise, settings.holdHigh, settings.fall));
            _shares.RegisterValueChangedCallback(evt =>
            {
                settings.SetShares(evt.newValue.x, evt.newValue.y, evt.newValue.z);
                Changed();
            });
            mixed?.Bind(
                _shares,
                settings,
                nameof(AlpsPhaseSettings.rise),
                nameof(AlpsPhaseSettings.holdHigh),
                nameof(AlpsPhaseSettings.fall));
            Add(_shares);

            // In / out rises once after the clip's start and falls once before its end, in
            // beats, where the wave's distribution splits a cycle.
            _inOut = new AlpsFadeSlider(AlpsStrings.Tr("phase.inOut"), MaxInOutBeats, AlpsStrings.Tr("unit.beats"), "0.##")
            {
                Snaps = new[] { 1f, 2f, 4f, 8f },
                DefaultValue = new Vector2(defaults.inBeats, defaults.outBeats),
                tooltip = AlpsStrings.Tr("phase.inOut.tip"),
            };
            _inOut.SetValueWithoutNotify(new Vector2(settings.inBeats, settings.outBeats));
            _inOut.RegisterValueChangedCallback(evt =>
            {
                settings.inBeats = evt.newValue.x;
                settings.outBeats = evt.newValue.y;
                Changed();
            });
            mixed?.Bind(_inOut, settings, nameof(AlpsPhaseSettings.inBeats), nameof(AlpsPhaseSettings.outBeats));
            Add(_inOut);

            // Under the wave it flips, whichever of the distribution or the in / out shows.
            _inverse = new AlpsToggleSwitch(AlpsStrings.Tr("phase.inverse"));
            _inverse.SetValueWithoutNotify(settings.inverse);
            _inverse.RegisterValueChangedCallback(evt =>
            {
                settings.inverse = evt.newValue;
                Changed();
            });
            mixed?.Bind(_inverse, settings, nameof(AlpsPhaseSettings.inverse));
            Add(_inverse);

            _easing = new AlpsEasingPicker(AlpsStrings.Tr("phase.easing"), compact);
            _easing.Rise.SetValueWithoutNotify((int)settings.ease);
            _easing.Rise.RegisterValueChangedCallback(evt =>
            {
                settings.ease = (AlpsEaseType)evt.newValue;
                Changed();
            });
            mixed?.Bind(_easing.Rise, settings, nameof(AlpsPhaseSettings.ease));

            _easing.Fall.SetValueWithoutNotify((int)settings.fallEase);
            _easing.Fall.RegisterValueChangedCallback(evt =>
            {
                settings.fallEase = (AlpsEaseType)evt.newValue;
                Changed();
            });
            mixed?.Bind(_easing.Fall, settings, nameof(AlpsPhaseSettings.fallEase));
            Add(_easing);

            // A count of fixtures, so it steps rather than drags.
            var group = new AlpsStepper(AlpsStrings.Tr("phase.group"), AlpsStrings.Tr("unit.light"), 1f) { Minimum = 1f };
            group.SetValueWithoutNotify(settings.fixtureGroupSize);
            group.RegisterValueChangedCallback(evt =>
            {
                settings.fixtureGroupSize = Mathf.Max(1, Mathf.RoundToInt(evt.newValue));
                group.SetValueWithoutNotify(settings.fixtureGroupSize);
                Changed();
            });
            mixed?.Bind(group, settings, nameof(AlpsPhaseSettings.fixtureGroupSize));
            Add(group);

            // Next to the fixture group, so the two steppers sit together above the sliders.
            _speed = new AlpsStepper(AlpsStrings.Tr("common.speed"), AlpsStrings.Tr("unit.beats"), 1f, compact ? null : AlpsStrings.Tr("phase.perCycle")) { Minimum = 0f, Subdivides = true };
            _speed.SetValueWithoutNotify(settings.beatsPerCycle);
            _speed.RegisterValueChangedCallback(evt =>
            {
                settings.beatsPerCycle = Mathf.Max(0f, evt.newValue);
                Changed();
            });
            mixed?.Bind(_speed, settings, nameof(AlpsPhaseSettings.beatsPerCycle));
            Add(_speed);

            // The delay is read over the whole group, so 100% walks the wave over every
            // fixture in exactly one cycle however many there are. Negative runs the order
            // backwards, for example from the edges in. The 拍 flag reads the same trip in
            // beats instead, which then holds when the speed changes. Beats are counted, not
            // dragged, so that unit gets the same stepper as the speed above.
            _spread = new AlpsValueSlider(AlpsStrings.Tr("phase.delay"), new Vector2(-SpreadLimit, SpreadLimit), "%", "0.##") { DefaultValue = 0f };
            _spread.AddToClassList("alps-animatable__field");
            _spread.RegisterValueChangedCallback(evt =>
            {
                settings.spread = evt.newValue / 100f;
                Changed();
            });

            _spreadBeats = new AlpsStepper(AlpsStrings.Tr("phase.delay"), AlpsStrings.Tr("unit.beats"), 1f)
            {
                Minimum = -SpreadBeatsLimit,
                Maximum = SpreadBeatsLimit,
                Subdivides = true,
            };
            // Growing like the slider keeps the flag on the right edge in either unit.
            _spreadBeats.AddToClassList("alps-animatable__field");
            _spreadBeats.RegisterValueChangedCallback(evt =>
            {
                settings.spreadBeats = evt.newValue;
                // Kept in step, so a speed of zero falls back to the same look. In / out reads
                // the beats alone and leaves the wave's share as it was.
                if (settings.mode != AlpsPhaseMode.InOut && settings.beatsPerCycle > 0f)
                {
                    settings.spread = evt.newValue / settings.beatsPerCycle;
                }

                Changed();
            });

            _beatsFlag = new AlpsRangeFlag(AlpsStrings.Tr("phase.beatsFlag.letter"), AlpsStrings.Tr("phase.beatsFlag.tip"));
            _beatsFlag.RegisterValueChangedCallback(evt =>
            {
                // Seed the unit being switched to from the current look, so the switch alone
                // never moves the fixtures.
                if (evt.newValue)
                {
                    settings.spreadBeats = settings.spread * settings.beatsPerCycle;
                }
                else if (settings.UsesSpreadBeats)
                {
                    settings.spread = settings.spreadBeats / settings.beatsPerCycle;
                }

                settings.spreadInBeats = evt.newValue;
                Changed();
            });

            mixed?.Bind(_spread, settings, nameof(AlpsPhaseSettings.spread));
            mixed?.Bind(_spreadBeats, settings, nameof(AlpsPhaseSettings.spreadBeats));
            mixed?.Bind(_beatsFlag, settings, nameof(AlpsPhaseSettings.spreadInBeats));

            var spreadRow = new VisualElement();
            spreadRow.AddToClassList("alps-animatable__row");
            spreadRow.Add(_spread);
            spreadRow.Add(_spreadBeats);
            spreadRow.Add(_beatsFlag);
            Add(spreadRow);

            // Every cycle at every order position rolls its own dice, so a chance below 100%
            // leaves some cycles resting at the bottom of the wave.
            _fireChance = new AlpsValueSlider(AlpsStrings.Tr("phase.fireChance"), new Vector2(0f, 100f), "%", "0.#")
            {
                DefaultValue = defaults.fireChance * 100f,
                Snaps = new[] { 25f, 50f, 75f },
                tooltip = AlpsStrings.Tr("phase.fireChance.tip"),
            };
            _fireChance.SetValueWithoutNotify(settings.fireChance * 100f);
            _fireChance.RegisterValueChangedCallback(evt =>
            {
                settings.fireChance = Mathf.Clamp01(evt.newValue / 100f);
                Changed();
            });
            mixed?.Bind(_fireChance, settings, nameof(AlpsPhaseSettings.fireChance));
            Add(_fireChance);

            // How many cycles the distribution covers. A wave longer than its cycle runs on
            // into the next ones, so a cycle can fire on every beat and still light longer.
            // That only reads apart from a slower speed once some cycles do not fire, so the
            // row follows the chance, and stays while the wave covers more than one cycle.
            _length = new AlpsSegmentedControl(
                AlpsStrings.Tr("phase.length"),
                AlpsStrings.Tr("phase.length.one"),
                AlpsStrings.Tr("phase.length.two"),
                AlpsStrings.Tr("phase.length.three"))
            {
                tooltip = AlpsStrings.Tr("phase.length.tip"),
            };
            if (compact)
            {
                _length.AddToClassList("alps-seg--compact");
            }

            _length.SetValueWithoutNotify(_shares.Cycles - 1);
            _length.RegisterValueChangedCallback(evt => _shares.SetCycles(evt.newValue + 1));
            _shares.CyclesChanged += () => _length.SetValueWithoutNotify(_shares.Cycles - 1);
            Add(_length);

            Refresh();
        }

        /// <summary>Re-applies the conditional visibility rules and repaints the graph.</summary>
        public void Refresh()
        {
            var isWave = _settings.mode == AlpsPhaseMode.Wave;
            var isInOut = _settings.mode == AlpsPhaseMode.InOut;
            // Written only when the mode moved from outside the tabs, an undo for example.
            if (_tabs.value != TabOf(_settings.mode))
            {
                _tabs.SetValueWithoutNotify(TabOf(_settings.mode));
            }

            RefreshShape();
            Show(_shape, isWave);
            Show(_easing, isWave || isInOut);
            Show(_inverse, isWave || isInOut);
            Show(_fireChance, isWave);
            Show(_shares, isWave);
            Show(_length, isWave && (_settings.fireChance < 1f || _shares.Cycles > 1));
            Show(_inOut, isInOut);
            Show(_speed, !isInOut);
            RefreshSpread();
            _graph.SetSettings(_settings);
        }

        /// <summary>The shape the shares make: no rise is a pulse, no fall a sweep, and both a ping-pong.</summary>
        public static int ShapeOf(AlpsPhaseSettings settings)
        {
            if (settings.rise <= 0f)
            {
                return ShapePulse;
            }

            return settings.fall <= 0f ? ShapeSweep : ShapePingPong;
        }

        private static int TabOf(AlpsPhaseMode mode)
        {
            return Mathf.Max(0, Array.IndexOf(TabModes, mode));
        }

        /// <summary>
        /// Takes the shape from the shares again once they no longer fit the one shown, after an
        /// undo for example, and locks the side the shape leaves out.
        /// </summary>
        private void RefreshShape()
        {
            if ((_shapeValue == ShapeSweep && _settings.fall > 0f) || (_shapeValue == ShapePulse && _settings.rise > 0f))
            {
                _shapeValue = ShapeOf(_settings);
            }

            var isWave = _settings.mode == AlpsPhaseMode.Wave;
            if (_shape.value != _shapeValue)
            {
                _shape.SetValueWithoutNotify(_shapeValue);
            }

            _shares.LockRise = isWave && _shapeValue == ShapePulse;
            _shares.LockFall = isWave && _shapeValue == ShapeSweep;

            var only = -1;
            if (isWave && _shapeValue == ShapeSweep)
            {
                only = AlpsEasingPicker.RiseSide;
            }
            else if (isWave && _shapeValue == ShapePulse)
            {
                only = AlpsEasingPicker.FallSide;
            }

            _easing.ShowOnly(only);
        }

        /// <summary>
        /// Rearranges the shares into <paramref name="shape"/>. The high hold stays, and the
        /// rise and the fall trade their time: a sweep or a pulse takes both on its one side,
        /// and a ping-pong splits it in half. A square wave has none to split, so its ping-pong
        /// takes half of the low hold, or of the high hold when that fills the cycle.
        /// </summary>
        private void SetShape(int shape)
        {
            _shapeValue = shape;
            var ramps = _settings.rise + _settings.fall;
            var high = _settings.holdHigh;
            if (shape == ShapeSweep)
            {
                _settings.SetShares(ramps, high, 0f);
            }
            else if (shape == ShapePulse)
            {
                _settings.SetShares(0f, high, ramps);
            }
            else
            {
                if (ramps <= 0f)
                {
                    if (_settings.HoldLow > 0f)
                    {
                        ramps = _settings.HoldLow * 0.5f;
                    }
                    else
                    {
                        ramps = high * 0.5f;
                        high -= ramps;
                    }
                }

                _settings.SetShares(ramps * 0.5f, high, ramps * 0.5f);
            }

            _shares.SetValueWithoutNotify(new Vector3(_settings.rise, _settings.holdHigh, _settings.fall));
            Changed();
        }

        /// <summary>
        /// Shows the delay in the unit the settings are read in. This runs on every edit,
        /// including each frame of a drag on the slider, and setting the snaps rebuilds the
        /// tick elements, so the marks are only rebuilt when the speed they come from moved.
        /// </summary>
        private void RefreshSpread()
        {
            // In / out has no cycle to take a share of, so its delay is always in beats.
            var isInOut = _settings.mode == AlpsPhaseMode.InOut;
            var inBeats = isInOut || _settings.UsesSpreadBeats;
            _beatsFlag.SetEnabled(_settings.beatsPerCycle > 0f);
            _beatsFlag.SetValueWithoutNotify(inBeats);
            Show(_beatsFlag, !isInOut);
            Show(_spread, !inBeats);
            Show(_spreadBeats, inBeats);
            _spreadBeats.tooltip = isInOut ? AlpsStrings.Tr("phase.delay.inOut.tip") : string.Empty;

            if (_settings.beatsPerCycle != _snapBeats)
            {
                _snapBeats = _settings.beatsPerCycle;
                _spread.Snaps = SpreadSnaps(_spread.Limit, _snapBeats);
            }

            _spread.SetValueWithoutNotify(_settings.spread * 100f);
            _spreadBeats.SetValueWithoutNotify(_settings.spreadBeats);
        }

        /// <summary>
        /// Snap points in percent, most useful first: zero, the full trip that closes the
        /// wave on itself, then where the trip lands on a musical division, then the half
        /// trip. The beat marks move with the speed, which is what makes a percentage land
        /// on the beat at all.
        /// </summary>
        internal static float[] SpreadSnaps(Vector2 limit, float beatsPerCycle)
        {
            var points = new List<float> { 0f, 100f, -100f };
            if (beatsPerCycle > 0f)
            {
                foreach (var beats in BeatSnaps)
                {
                    points.Add(beats / beatsPerCycle * 100f);
                    points.Add(-beats / beatsPerCycle * 100f);
                }
            }

            points.Add(50f);
            points.Add(-50f);
            return KeepSnaps(points, limit);
        }

        /// <summary>
        /// The first points that fit inside the limits without repeating, sorted. The rail
        /// only holds so many marks, so the tail is dropped rather than crowded.
        /// </summary>
        private static float[] KeepSnaps(List<float> points, Vector2 limit)
        {
            var kept = new List<float>();
            foreach (var point in points)
            {
                if (point <= limit.x || point >= limit.y || kept.Count >= AlpsSnapPoints.MaxTicks)
                {
                    continue;
                }

                var duplicate = false;
                foreach (var already in kept)
                {
                    duplicate |= Mathf.Abs(already - point) < 0.01f;
                }

                if (!duplicate)
                {
                    kept.Add(point);
                }
            }

            kept.Sort();
            return kept.ToArray();
        }

        private void Changed()
        {
            Refresh();
            _onChanged?.Invoke();
        }

        internal static void Show(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
