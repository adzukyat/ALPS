using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Shared settings: graph preview, mode, easing, ping-pong ratio, fixture group size,
    /// spread, speed, invert.
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

        /// <summary>Musical divisions the trip across the group can be made to land on.</summary>
        private static readonly float[] BeatSnaps = { 1f, 2f, 4f, 0.5f };

        private readonly AlpsPhaseSettings _settings;
        private readonly Action _onChanged;

        private readonly AlpsPhaseGraph _graph;
        private readonly AlpsEasingGrid _easing;
        private readonly AlpsValueSlider _pingPongRatio;
        private readonly AlpsValueSlider _spread;
        private readonly AlpsStepper _spreadBeats;
        private readonly AlpsRangeFlag _beatsFlag;
        private readonly AlpsToggleSwitch _inverse;

        /// <summary>The speed the delay's marks were last built for. NaN forces the first build.</summary>
        private float _snapBeats = float.NaN;

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

            _graph = new AlpsPhaseGraph();
            if (compact)
            {
                _graph.AddToClassList("alps-graph--compact");
            }

            Add(_graph);

            var mode = new AlpsSegmentedControl("モード", "順送り", "ピンポン", "ランダム");
            if (compact)
            {
                mode.AddToClassList("alps-seg--compact");
            }

            mode.SetValueWithoutNotify((int)settings.mode);
            mode.RegisterValueChangedCallback(evt =>
            {
                settings.mode = (AlpsPhaseMode)evt.newValue;
                Refresh();
                Changed();
            });
            mixed?.Bind(mode, settings, nameof(AlpsPhaseSettings.mode));
            Add(mode);

            _easing = new AlpsEasingGrid("イージング");
            if (compact)
            {
                _easing.AddToClassList("alps-easegrid--compact");
            }

            _easing.SetValueWithoutNotify((int)settings.ease);
            _easing.RegisterValueChangedCallback(evt =>
            {
                settings.ease = (AlpsEaseType)evt.newValue;
                Changed();
            });
            mixed?.Bind(_easing, settings, nameof(AlpsPhaseSettings.ease));
            Add(_easing);

            // 50% spends the same time going out and coming back.
            _pingPongRatio = new AlpsValueSlider("往復比", new Vector2(0f, 100f), "%", "0") 
            {
                Snaps = new[] { 50f },
                DefaultValue = defaults.pingPongRatio * 100f,
            };
            _pingPongRatio.SetValueWithoutNotify(settings.pingPongRatio * 100f);
            _pingPongRatio.RegisterValueChangedCallback(evt =>
            {
                settings.pingPongRatio = Mathf.Clamp01(evt.newValue / 100f);
                Changed();
            });
            mixed?.Bind(_pingPongRatio, settings, nameof(AlpsPhaseSettings.pingPongRatio));
            Add(_pingPongRatio);

            var group = new AlpsValueSlider("灯体単位", new Vector2(1f, 16f), "灯", "0") { DefaultValue = defaults.fixtureGroupSize };
            group.SetValueWithoutNotify(settings.fixtureGroupSize);
            group.RegisterValueChangedCallback(evt =>
            {
                settings.fixtureGroupSize = Mathf.Max(1, Mathf.RoundToInt(evt.newValue));
                group.SetValueWithoutNotify(settings.fixtureGroupSize);
                Changed();
            });
            mixed?.Bind(group, settings, nameof(AlpsPhaseSettings.fixtureGroupSize));
            Add(group);

            // The delay is read over the whole group, so 100% walks the wave over every
            // fixture in exactly one cycle however many there are. Negative runs the order
            // backwards, for example from the edges in. The 拍 flag reads the same trip in
            // beats instead, which then holds when the speed changes. Beats are counted, not
            // dragged, so that unit gets the same stepper as the speed below.
            _spread = new AlpsValueSlider("ディレイ", new Vector2(-SpreadLimit, SpreadLimit), "%", "0.##") { DefaultValue = 0f };
            _spread.AddToClassList("alps-animatable__field");
            _spread.RegisterValueChangedCallback(evt =>
            {
                settings.spread = evt.newValue / 100f;
                Changed();
            });

            _spreadBeats = new AlpsStepper("ディレイ", "拍", 1f)
            {
                Minimum = -SpreadBeatsLimit,
                Maximum = SpreadBeatsLimit,
            };
            // Growing like the slider keeps the flag on the right edge in either unit.
            _spreadBeats.AddToClassList("alps-animatable__field");
            _spreadBeats.RegisterValueChangedCallback(evt =>
            {
                settings.spreadBeats = evt.newValue;
                // Kept in step, so a speed of zero falls back to the same look.
                settings.spread = evt.newValue / settings.beatsPerCycle;
                Changed();
            });

            _beatsFlag = new AlpsRangeFlag("拍", "拍で指定");
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

            var speed = new AlpsStepper("速度", "拍", 1f, compact ? null : "/ 1周期") { Minimum = 0f };
            speed.SetValueWithoutNotify(settings.beatsPerCycle);
            speed.RegisterValueChangedCallback(evt =>
            {
                settings.beatsPerCycle = Mathf.Max(0f, evt.newValue);
                Changed();
            });
            mixed?.Bind(speed, settings, nameof(AlpsPhaseSettings.beatsPerCycle));
            Add(speed);

            _inverse = new AlpsToggleSwitch("反転");
            _inverse.SetValueWithoutNotify(settings.inverse);
            _inverse.RegisterValueChangedCallback(evt =>
            {
                settings.inverse = evt.newValue;
                Changed();
            });
            mixed?.Bind(_inverse, settings, nameof(AlpsPhaseSettings.inverse));
            Add(_inverse);

            Refresh();
        }

        /// <summary>Re-applies the conditional visibility rules and repaints the graph.</summary>
        public void Refresh()
        {
            var isRandom = _settings.mode == AlpsPhaseMode.Random;
            Show(_easing, !isRandom);
            Show(_inverse, !isRandom);
            Show(_pingPongRatio, _settings.mode == AlpsPhaseMode.PingPong);
            RefreshSpread();
            _graph.SetSettings(_settings);
        }

        /// <summary>
        /// Shows the delay in the unit the settings are read in. This runs on every edit,
        /// including each frame of a drag on the slider, and setting the snaps rebuilds the
        /// tick elements, so the marks are only rebuilt when the speed they come from moved.
        /// </summary>
        private void RefreshSpread()
        {
            var inBeats = _settings.UsesSpreadBeats;
            _beatsFlag.SetEnabled(_settings.beatsPerCycle > 0f);
            _beatsFlag.SetValueWithoutNotify(inBeats);
            Show(_spread, !inBeats);
            Show(_spreadBeats, inBeats);

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
            RefreshSpread();
            _graph.SetSettings(_settings);
            _onChanged?.Invoke();
        }

        internal static void Show(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
