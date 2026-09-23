using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The distribution row: label | one wave cycle with three draggable corners, and the
    /// share of each part under it.
    ///
    /// The cycle is drawn as its plain envelope (rise, high hold, fall, low hold) without the
    /// ease, since this row only decides how long each part lasts. The graph above it shows
    /// the eased result. <see cref="BaseField{T}.value"/> is (rise, high hold, fall), each a
    /// share of the cycle. The low hold is what they leave over.
    /// </summary>
    public class AlpsShareBar : BaseField<Vector3>
    {
        public new static readonly string ussClassName = "alps-shares";

        /// <summary>Corner identifiers, in cycle order.</summary>
        public const int RiseEnd = 0;
        public const int HighEnd = 1;
        public const int FallEnd = 2;

        /// <summary>Corners stick to every eighth of the cycle, which is where beats land.</summary>
        public const int SnapDivisions = 8;

        /// <summary>How close, in pixels, the pointer has to come to a corner to pick it up.</summary>
        private const float PickRadius = 8f;

        /// <summary>Keeps the thumbs at 0% and 100% inside the well: half the 10px thumb and a pixel.</summary>
        private const float Inset = 6f;

        private static readonly string[] PartNames = { "上り", "上で停止", "下り", "下で停止" };
        private static readonly string[] CornerNames = { "上りの終わり", "下りの始まり", "下りの終わり" };

        private static readonly Color TraceColor = new Color32(0x6E, 0xA8, 0xDC, 0xFF);
        private static readonly Color AreaColor = new Color32(0x6E, 0xA8, 0xDC, 0x2E);
        private static readonly Color QuarterColor = new Color32(0x3A, 0x3A, 0x3A, 0xFF);
        private static readonly Color EighthColor = new Color32(0x2C, 0x2C, 0x2C, 0xFF);

        private readonly VisualElement _well;
        private readonly VisualElement[] _thumbs = new VisualElement[3];
        private readonly VisualElement _legend;
        private readonly Label[] _shares = new Label[4];

        /// <summary>Corner positions in 0..1, cumulative: end of rise, end of high hold, end of fall.</summary>
        private readonly float[] _corners = new float[3];

        private int _dragging = -1;

        /// <summary>Coincident corners wait for the first move to learn which one is meant.</summary>
        private readonly List<int> _pending = new List<int>();

        private float _pressX;
        private float _grabOffset;

        public AlpsShareBar(string label)
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            AddToClassList("alps-row--top");

            var container = this.Q(className: BaseField<Vector3>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _well = new VisualElement();
            _well.AddToClassList(ussClassName + "__well");
            _well.generateVisualContent += OnGenerateVisualContent;
            _well.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _well.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _well.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _well.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            container.Add(_well);

            for (var i = 0; i < _thumbs.Length; i++)
            {
                var thumb = new VisualElement { tooltip = CornerNames[i] };
                thumb.AddToClassList(ussClassName + "__thumb");
                _well.Add(thumb);
                _thumbs[i] = thumb;
            }

            _legend = new VisualElement();
            _legend.AddToClassList(ussClassName + "__legend");
            for (var i = 0; i < _shares.Length; i++)
            {
                var share = new Label { pickingMode = PickingMode.Position };
                share.AddToClassList(ussClassName + "__share");
                _legend.Add(share);
                _shares[i] = share;
            }

            _legend.RegisterCallback<GeometryChangedEvent>(_ => RefreshLegend());
            container.Add(_legend);

            SetValueWithoutNotify(new Vector3(0.5f, 0f, 0.5f));
        }

        /// <summary>What a double click on a corner restores it toward. Null leaves the corners as they are.</summary>
        public Vector3? DefaultValue { get; set; }

        /// <summary>The corner positions, for tests: end of rise, end of high hold, end of fall.</summary>
        public float Corner(int corner)
        {
            return _corners[corner];
        }

        /// <summary>Committing while mixed always reaches the other clips. See <see cref="AlpsMixedField"/>.</summary>
        public override Vector3 value
        {
            get => base.value;
            set => AlpsMixedField.Set(this, value, v => base.value = v);
        }

        public override void SetValueWithoutNotify(Vector3 newValue)
        {
            var rise = Mathf.Clamp01(newValue.x);
            var high = Mathf.Clamp(newValue.y, 0f, 1f - rise);
            var fall = Mathf.Clamp(newValue.z, 0f, 1f - rise - high);
            base.SetValueWithoutNotify(new Vector3(rise, high, fall));

            _corners[RiseEnd] = rise;
            _corners[HighEnd] = rise + high;
            _corners[FallEnd] = rise + high + fall;
            Refresh();
        }

        protected override void UpdateMixedValueContent()
        {
            _well.EnableInClassList(ussClassName + "__well--mixed", showMixedValue);
            RefreshLegend();
            _well.MarkDirtyRepaint();
        }

        /// <summary>
        /// Moves one corner to <paramref name="position"/>, kept between its neighbours, and
        /// commits the new shares. The drag and the tests go through here.
        /// </summary>
        public void MoveCorner(int corner, float position)
        {
            var low = corner == RiseEnd ? 0f : _corners[corner - 1];
            var high = corner == FallEnd ? 1f : _corners[corner + 1];
            var corners = (float[])_corners.Clone();
            corners[corner] = Mathf.Clamp(position, low, high);
            value = FromCorners(corners);
        }

        private static Vector3 FromCorners(float[] corners)
        {
            return new Vector3(
                corners[RiseEnd],
                corners[HighEnd] - corners[RiseEnd],
                corners[FallEnd] - corners[HighEnd]);
        }

        // ------------------------------------------------------------ geometry

        private float Width => Mathf.Max(1f, _well.contentRect.width - 2f * Inset);

        private float XOf(float position)
        {
            return Inset + position * Width;
        }

        private float PositionAt(float x)
        {
            return (x - Inset) / Width;
        }

        /// <summary>The high corners sit on the top line and the fall's end on the bottom one.</summary>
        private float YOf(int corner)
        {
            return corner == FallEnd ? _well.contentRect.height - Inset : Inset;
        }

        private void Refresh()
        {
            if (_well.contentRect.width <= 0f)
            {
                return;
            }

            for (var i = 0; i < _thumbs.Length; i++)
            {
                _thumbs[i].style.left = XOf(_corners[i]);
                _thumbs[i].style.top = YOf(i);
            }

            RefreshLegend();
            _well.MarkDirtyRepaint();
        }

        /// <summary>
        /// Writes each part's share under it. A part too narrow for its number shows none, and
        /// its name and share stay in the tooltip.
        /// </summary>
        private void RefreshLegend()
        {
            var width = _legend.contentRect.width;
            var starts = new[] { 0f, _corners[RiseEnd], _corners[HighEnd], _corners[FallEnd] };
            var ends = new[] { _corners[RiseEnd], _corners[HighEnd], _corners[FallEnd], 1f };

            for (var i = 0; i < _shares.Length; i++)
            {
                var share = ends[i] - starts[i];
                var text = showMixedValue ? AlpsMixedValues.MixedText : (share * 100f).ToString("0.#") + "%";
                var label = _shares[i];
                label.text = text;
                label.tooltip = PartNames[i] + " " + text;
                label.style.left = Length.Percent(starts[i] * 100f);
                label.style.width = Length.Percent(share * 100f);

                var needed = width <= 0f
                    ? 0f
                    : label.MeasureTextSize(text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined).x;
                var fits = share > 0f && width > 0f && needed <= share * width;
                label.style.display = fits ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            var rect = _well.contentRect;
            if (rect.width <= 2f * Inset || rect.height <= 2f * Inset)
            {
                return;
            }

            var painter = context.painter2D;
            var top = Inset;
            var bottom = rect.height - Inset;

            for (var i = 1; i < SnapDivisions; i++)
            {
                var x = XOf(i / (float)SnapDivisions);
                painter.strokeColor = i % 2 == 0 ? QuarterColor : EighthColor;
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 1f));
                painter.LineTo(new Vector2(x, rect.height - 1f));
                painter.Stroke();
            }

            var points = new[]
            {
                new Vector2(XOf(0f), bottom),
                new Vector2(XOf(_corners[RiseEnd]), top),
                new Vector2(XOf(_corners[HighEnd]), top),
                new Vector2(XOf(_corners[FallEnd]), bottom),
                new Vector2(XOf(1f), bottom),
            };

            painter.fillColor = AreaColor;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (var i = 1; i < points.Length; i++)
            {
                painter.LineTo(points[i]);
            }

            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = TraceColor;
            painter.lineWidth = 1.5f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (var i = 1; i < points.Length; i++)
            {
                painter.LineTo(points[i]);
            }

            painter.Stroke();
        }

        // ---------------------------------------------------------------- drag

        /// <summary>
        /// Every corner within reach of <paramref name="local"/> that is as close as the
        /// nearest one. More than one means they sit on the same spot.
        /// </summary>
        private List<int> CornersAt(Vector2 local)
        {
            var nearest = new List<int>();
            var best = PickRadius;
            for (var i = 0; i < _corners.Length; i++)
            {
                var distance = Vector2.Distance(local, new Vector2(XOf(_corners[i]), YOf(i)));
                if (distance > best + 0.5f)
                {
                    continue;
                }

                if (distance < best - 0.5f)
                {
                    nearest.Clear();
                    best = distance;
                }

                nearest.Add(i);
            }

            return nearest;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            var corners = CornersAt(evt.localPosition);
            if (corners.Count == 0)
            {
                return;
            }

            if (evt.clickCount == 2)
            {
                ResetCorners(corners);
                evt.StopPropagation();
                return;
            }

            _pending.Clear();
            _pressX = evt.localPosition.x;
            if (corners.Count == 1)
            {
                StartDrag(corners[0], evt.localPosition.x);
            }
            else
            {
                _pending.AddRange(corners);
            }

            _well.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void StartDrag(int corner, float x)
        {
            _dragging = corner;
            // Grabbing a corner off centre keeps its value, so a click alone never moves it.
            _grabOffset = _corners[corner] - PositionAt(x);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_well.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            var x = evt.localPosition.x;
            if (_dragging < 0 && _pending.Count > 0)
            {
                if (Mathf.Approximately(x, _pressX))
                {
                    return;
                }

                // Stacked corners part in the direction of the drag: left takes the first,
                // right the last, since the others could not move that way anyway.
                StartDrag(x < _pressX ? _pending[0] : _pending[_pending.Count - 1], _pressX);
                _pending.Clear();
            }

            if (_dragging < 0)
            {
                return;
            }

            var position = PositionAt(x) + _grabOffset;
            MoveCorner(_dragging, Snap(position));
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_well.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            _dragging = -1;
            _pending.Clear();
            _well.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private float Snap(float position)
        {
            var snaps = new float[SnapDivisions + 1];
            for (var i = 0; i <= SnapDivisions; i++)
            {
                snaps[i] = i / (float)SnapDivisions;
            }

            return AlpsSliderTrack.Snap(position, snaps, AlpsSliderTrack.SnapDistance / Width);
        }

        /// <summary>Double click: the picked corners go back toward where the defaults put them.</summary>
        private void ResetCorners(List<int> corners)
        {
            if (!DefaultValue.HasValue)
            {
                return;
            }

            var defaults = DefaultValue.Value;
            var targets = new[] { defaults.x, defaults.x + defaults.y, defaults.x + defaults.y + defaults.z };
            var moved = (float[])_corners.Clone();

            // Picked corners are always neighbours, and none of them may pass the corners
            // around them that stay put.
            var first = corners[0];
            var last = corners[corners.Count - 1];
            var low = first == RiseEnd ? 0f : moved[first - 1];
            var high = last == FallEnd ? 1f : moved[last + 1];
            for (var i = first; i <= last; i++)
            {
                moved[i] = Mathf.Clamp(targets[i], low, high);
                low = moved[i];
            }

            value = FromCorners(moved);
        }
    }
}
