using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Graph preview. Plots φ(t) for the first few fixtures of a
    /// <see cref="AlpsPhaseSettings"/>, so the spread and the fixture group size are visible as the
    /// fading offset traces.
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

            for (var i = 1; i < CycleCount; i++)
            {
                var x = rect.width * i / CycleCount;
                strokes.Add(new Stroke(
                    new List<Vector2> { new Vector2(x, 0f), new Vector2(x, rect.height) },
                    GridColor,
                    1f));
            }

            var top = 6f;
            var bottom = Mathf.Max(top + 1f, rect.height - 6f);

            for (var trace = 0; trace < TraceCount; trace++)
            {
                var color = TraceColor;
                color.a = trace == 0 ? 1f : (trace == 1 ? 0.4f : 0.2f);

                // The spread is measured over the whole group, so the traces stand for a
                // fixture at the start of the order, a third along and two thirds along.
                // That needs no fixture count, which this view does not have. k stays the
                // trace index so random mode still tells the three apart.
                var k = trace;
                var offset = _settings.SpreadCycles * trace / TraceCount;

                var points = new List<Vector2>(Samples + 1);
                for (var i = 0; i <= Samples; i++)
                {
                    var t = i / (float)Samples;
                    var cycles = AlpsShowEvaluator.FixtureCycles(t * CycleCount, 1f, offset, 1, 0f);
                    var phase = AlpsShowEvaluator.Phase(
                        (int)_settings.mode,
                        (int)_settings.ease,
                        _settings.pingPongRatio,
                        _settings.pingPongHold,
                        _settings.inverse,
                        cycles,
                        k,
                        0);
                    points.Add(new Vector2(rect.width * t, Mathf.Lerp(bottom, top, Mathf.Clamp01(phase))));
                }

                strokes.Add(new Stroke(points, color, trace == 0 ? 1.5f : 1f));
            }

            return strokes;
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
