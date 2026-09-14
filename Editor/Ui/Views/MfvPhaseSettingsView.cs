using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Ui.Editor
{
    /// <summary>
    /// Shared settings: graph preview, mode, easing, ping-pong ratio, fixture group size,
    /// delay, speed, invert.
    ///
    /// The same view is reused verbatim under a parameter that turns own phase on,
    /// which is why it takes a plain <see cref="MfvPhaseSettings"/> rather than reaching
    /// for the clip.
    /// </summary>
    public class MfvPhaseSettingsView : VisualElement
    {
        private readonly MfvPhaseSettings _settings;
        private readonly Action _onChanged;
        private readonly bool _compact;

        private readonly MfvPhaseGraph _graph;
        private readonly MfvEasingGrid _easing;
        private readonly MfvValueSlider _pingPongRatio;
        private readonly MfvToggleSwitch _inverse;

        public MfvPhaseSettingsView(MfvPhaseSettings settings, Action onChanged, bool compact = false)
        {
            _settings = settings;
            _onChanged = onChanged;
            _compact = compact;

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

            _easing = new MfvEasingGrid("イージング", compact ? 4 : 9);
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

            _pingPongRatio = new MfvValueSlider("往復比", new Vector2(0f, 100f), "%", "0");
            _pingPongRatio.SetValueWithoutNotify(settings.pingPongRatio * 100f);
            _pingPongRatio.RegisterValueChangedCallback(evt =>
            {
                settings.pingPongRatio = Mathf.Clamp01(evt.newValue / 100f);
                Changed();
            });
            Add(_pingPongRatio);

            var group = new MfvValueSlider("灯体単位", new Vector2(1f, 16f), "灯", "0");
            group.SetValueWithoutNotify(settings.fixtureGroupSize);
            group.RegisterValueChangedCallback(evt =>
            {
                settings.fixtureGroupSize = Mathf.Max(1, Mathf.RoundToInt(evt.newValue));
                group.SetValueWithoutNotify(settings.fixtureGroupSize);
                Changed();
            });
            Add(group);

            var delay = new MfvValueSlider("ディレイ", new Vector2(0f, 1f), string.Empty, "0.###");
            delay.SetValueWithoutNotify(settings.delay);
            delay.RegisterValueChangedCallback(evt =>
            {
                settings.delay = evt.newValue;
                Changed();
            });
            Add(delay);

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
            _graph.SetSettings(_settings);
        }

        private void Changed()
        {
            _graph.SetSettings(_settings);
            _onChanged?.Invoke();
        }

        internal static void Show(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
