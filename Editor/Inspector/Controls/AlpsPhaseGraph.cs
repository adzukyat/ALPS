using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Graph preview. Plots φ(t) for the first few fixtures of a
    /// <see cref="AlpsPhaseSettings"/>, so the spread and the fixture group size are visible as the
    /// fading offset traces, and a fire chance below 1 as cycles that stay down. An in / out
    /// phase is plotted over one clip instead of four cycles.
    /// </summary>
    public class AlpsPhaseGraph : VisualElement
    {
        private const int Samples = 96;
        private const int TraceCount = 3;
        private const int CycleCount = 4;

        private static readonly Color TraceColor = new Color32(0x6E, 0xA8, 0xDC, 0xFF);
        private static readonly Color GridColor = new Color32(0x3A, 0x3A, 0x3A, 0xFF);

        private AlpsPhaseSettings _settings = new AlpsPhaseSettings();

        public AlpsPhaseGraph()
        {
            AddToClassList("alps-graph");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>The panel calls this on every edit. The graph is a pure view of the settings.</summary>
        public void SetSettings(AlpsPhaseSettings settings)
        {
            _settings = settings ?? new AlpsPhaseSettings();
            MarkDirtyRepaint();
        }

        /// <summary>One painter2D polyline: what is drawn is exactly what is reported.</summary>
        public readonly struct Stroke
        {
            public Stroke(List<Vector2> points, Color color, float width)
            {
                Points = points;
                Color = color;
                Width = width;
            }

            public List<Vector2> Points { get; }
            public Color Color { get; }
            public float Width { get; }
        }

        /// <summary>
        /// Everything this graph paints, in local pixels: the cycle grid first, then the
        /// traces outermost (opaque) first. Painting and the layout tests read this same
        /// list, so the tested curve is the drawn curve.
        /// </summary>
        public List<Stroke> GetLocalStrokes()
        {
            var strokes = new List<Stroke>();
            var rect = contentRect;
            if (rect.width <= 1f || rect.height <= 1f)
            {
                return strokes;
            }

            var inOut = _settings.mode == AlpsPhaseMode.InOut;
            var clipBeats = InOutClipBeats(_settings);
            if (!inOut)
            {
                for (var i = 1; i < CycleCount; i++)
                {
                    var x = rect.width * i / CycleCount;
                    strokes.Add(new Stroke(
                        new List<Vector2> { new Vector2(x, 0f), new Vector2(x, rect.height) },
                        GridColor,
                        1f));
                }
            }

            var top = 6f;
            var bottom = Mathf.Max(top + 1f, rect.height - 6f);

            for (var trace = 0; trace < TraceCount; trace++)
            {
                var color = TraceColor;
                color.a = trace == 0 ? 1f : (trace == 1 ? 0.4f : 0.2f);

                // A share is measured over the whole group, so its traces stand for a fixture
                // at the start of the order, a third along and two thirds along, which needs
                // no fixture count, which this view does not have. Beats are between
                // neighbours, so their traces stand for the first three. k stays the trace
                // index so random mode still tells the three apart. An in / out counts the
                // last trace as the last order position.
                var k = trace;
                var offset = _settings.PositionDelayCycles(TraceCount) * trace;
                var delay = _settings.InOutDelayBeats(TraceCount) * trace;

                var points = new List<Vector2>(Samples + 1);
                for (var i = 0; i <= Samples; i++)
                {
                    var t = i / (float)Samples;
                    float phase;
                    if (inOut)
                    {
                        phase = AlpsPhaseCurve.InOut(
                            (int)_settings.ease,
                            (int)_settings.fallEase,
                            _settings.PlayedInBeats,
                            _settings.PlayedOutBeats,
                            (int)_settings.inOutMode,
                            _settings.inverse,
                            t * clipBeats,
                            clipBeats,
                            delay,
                            LastDelay(_settings));
                    }
                    else
                    {
                        var cycles = AlpsPhaseCurve.FixtureCycles(t * CycleCount, 1f, offset, 1, 0f);
                        phase = AlpsPhaseCurve.Phase(
                            (int)_settings.mode,
                            (int)_settings.ease,
                            (int)_settings.fallEase,
                            _settings.rise,
                            _settings.holdHigh,
                            _settings.fall,
                            _settings.inverse,
                            _settings.fireChance,
                            cycles,
                            k,
                            0);
                    }

                    points.Add(new Vector2(rect.width * t, Mathf.Lerp(bottom, top, Mathf.Clamp01(phase))));
                }

                strokes.Add(new Stroke(points, color, trace == 0 ? 1.5f : 1f));
            }

            return strokes;
        }

        /// <summary>
        /// The clip length an in / out graph stands for, which this view does not know: room
        /// for the delay, the in and the out, and a hold as long as the longer of the two, so
        /// the top always shows.
        /// </summary>
        private static float InOutClipBeats(AlpsPhaseSettings settings)
        {
            var ramps = settings.PlayedInBeats + settings.PlayedOutBeats;
            var hold = Mathf.Max(1f, Mathf.Max(settings.PlayedInBeats, settings.PlayedOutBeats));
            return Mathf.Abs(LastDelay(settings)) + ramps + hold;
        }

        /// <summary>The in / out delay of the last trace, which stands for the last order position.</summary>
        private static float LastDelay(AlpsPhaseSettings settings)
        {
            return settings.InOutDelayBeats(TraceCount) * (TraceCount - 1);
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            var painter = context.painter2D;

            foreach (var stroke in GetLocalStrokes())
            {
                painter.strokeColor = stroke.Color;
                painter.lineWidth = stroke.Width;
                painter.BeginPath();
                painter.MoveTo(stroke.Points[0]);
                for (var i = 1; i < stroke.Points.Count; i++)
                {
                    painter.LineTo(stroke.Points[i]);
                }

                painter.Stroke();
            }
        }
    }
}
