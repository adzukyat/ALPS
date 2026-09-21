using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// Shared settings: graph preview, mode, easing, ping-pong ratio, fixture group size,
    /// spread, speed, invert.
    ///
    /// The same view is reused verbatim under a parameter that turns own phase on,
    /// which is why it takes a plain <see cref="MfvPhaseSettings"/> rather than reaching
    /// for the clip.
    /// </summary>
    public class MfvPhaseSettingsView : VisualElement
    {
        /// <summary>Two full trips across the group in one cycle, which is as far as a wave reads.</summary>
        private const float SpreadLimit = 200f;

        /// <summary>The delay in beats reaches four bars of four, far past any cycle it staggers.</summary>
        private const float SpreadBeatsLimit = 16f;

        /// <summary>Musical divisions the trip across the group can be made to land on.</summary>
        private static readonly float[] BeatSnaps = { 1f, 2f, 4f, 0.5f };

        private readonly MfvPhaseSettings _settings;
        private readonly Action _onChanged;

        private readonly MfvPhaseGraph _graph;
        private readonly MfvEasingGrid _easing;
        private readonly MfvValueSlider _pingPongRatio;
        private readonly MfvValueSlider _spread;
        private readonly MfvStepper _spreadBeats;
        private readonly MfvRangeFlag _beatsFlag;
        private readonly MfvToggleSwitch _inverse;

        /// <summary>The speed the delay's marks were last built for. NaN forces the first build.</summary>
        private float _snapBeats = float.NaN;

        public MfvPhaseSettingsView(MfvPhaseSettings settings, Action onChanged, bool compact = false)
        {
            _settings = settings;
            _onChanged = onChanged;

            var defaults = new MfvPhaseSettings();

            if (compact)
            {
                AddToClassList("mfv-nested");
            }
            else
            {
                // The view sits inside a card body, so its rows carry the body's 9px rhythm.
                AddToClassList("mfv-flow9");
            }

            _graph = new MfvPhaseGraph();
            if (compact)
            {
                _graph.AddToClassList("mfv-graph--compact");
            }

            Add(_graph);

            var mode = new MfvSegmentedControl("モード", "順送り", "ピンポン", "ランダム");
            if (compact)
            {
                mode.AddToClassList("mfv-seg--compact");
            }

            mode.SetValueWithoutNotify((int)settings.mode);
            mode.RegisterValueChangedCallback(evt =>
            {
                settings.mode = (MfvPhaseMode)evt.newValue;
                Refresh();
                Changed();
            });
            Add(mode);

            _easing = new MfvEasingGrid("イージング");
            if (compact)
            {
                _easing.AddToClassList("mfv-easegrid--compact");
            }

            _easing.SetValueWithoutNotify((int)settings.ease);
            _easing.RegisterValueChangedCallback(evt =>
            {
                settings.ease = (MfvEaseType)evt.newValue;
                Changed();
            });
            Add(_easing);

            // 50% spends the same time going out and coming back.
            _pingPongRatio = new MfvValueSlider("往復比", new Vector2(0f, 100f), "%", "0") 
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
            Add(_pingPongRatio);

            var group = new MfvValueSlider("灯体単位", new Vector2(1f, 16f), "灯", "0") { DefaultValue = defaults.fixtureGroupSize };
            group.SetValueWithoutNotify(settings.fixtureGroupSize);
            group.RegisterValueChangedCallback(evt =>
            {
                settings.fixtureGroupSize = Mathf.Max(1, Mathf.RoundToInt(evt.newValue));
                group.SetValueWithoutNotify(settings.fixtureGroupSize);
                Changed();
            });
            Add(group);

            // The delay is read over the whole group, so 100% walks the wave over every
            // fixture in exactly one cycle however many there are. Negative runs the order
            // backwards, for example from the edges in. The 拍 flag reads the same trip in
            // beats instead, which then holds when the speed changes. Beats are counted, not
            // dragged, so that unit gets the same stepper as the speed below.
            _spread = new MfvValueSlider("ディレイ", new Vector2(-SpreadLimit, SpreadLimit), "%", "0.##") { DefaultValue = 0f };
            _spread.AddToClassList("mfv-animatable__field");
            _spread.RegisterValueChangedCallback(evt =>
            {
                settings.spread = evt.newValue / 100f;
                Changed();
            });

            _spreadBeats = new MfvStepper("ディレイ", "拍", 1f)
            {
                Minimum = -SpreadBeatsLimit,
                Maximum = SpreadBeatsLimit,
            };
            // Growing like the slider keeps the flag on the right edge in either unit.
            _spreadBeats.AddToClassList("mfv-animatable__field");
            _spreadBeats.RegisterValueChangedCallback(evt =>
            {
                settings.spreadBeats = evt.newValue;
                // Kept in step, so a speed of zero falls back to the same look.
                settings.spread = evt.newValue / settings.beatsPerCycle;
                Changed();
            });

            _beatsFlag = new MfvRangeFlag("拍", "拍で指定");
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

            var spreadRow = new VisualElement();
            spreadRow.AddToClassList("mfv-animatable__row");
            spreadRow.Add(_spread);
            spreadRow.Add(_spreadBeats);
            spreadRow.Add(_beatsFlag);
            Add(spreadRow);

            var speed = new MfvStepper("速度", "拍", 1f, compact ? null : "/ 1周期") { Minimum = 0f };
            speed.SetValueWithoutNotify(settings.beatsPerCycle);
            speed.RegisterValueChangedCallback(evt =>
            {
                settings.beatsPerCycle = Mathf.Max(0f, evt.newValue);
                Changed();
            });
            Add(speed);

            _inverse = new MfvToggleSwitch("反転");
            _inverse.SetValueWithoutNotify(settings.inverse);
            _inverse.RegisterValueChangedCallback(evt =>
            {
                settings.inverse = evt.newValue;
                Changed();
            });
            Add(_inverse);

            Refresh();
        }

        /// <summary>Re-applies the conditional visibility rules and repaints the graph.</summary>
        public void Refresh()
        {
            var isRandom = _settings.mode == MfvPhaseMode.Random;
            Show(_easing, !isRandom);
            Show(_inverse, !isRandom);
            Show(_pingPongRatio, _settings.mode == MfvPhaseMode.PingPong);
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
                if (point <= limit.x || point >= limit.y || kept.Count >= MfvSnapPoints.MaxTicks)
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
