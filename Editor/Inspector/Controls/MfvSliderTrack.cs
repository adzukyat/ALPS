using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// The bare rail / fill / thumb geometry, in one or two thumb form.
    ///
    /// Unity's own Slider and MinMaxSlider carry a fixed internal hierarchy that the
    /// flat 4px rail and 11x14 rounded thumbs do not map onto cleanly, so the
    /// track is drawn directly and the drag handling lives here.
    /// </summary>
    public class MfvSliderTrack : VisualElement
    {
        public new static readonly string ussClassName = "mfv-slider";

        /// <summary>How close, in pixels, a dragged thumb has to come to a snap point to land on it.</summary>
        public const float SnapDistance = 5f;

        private readonly VisualElement _fill;
        private readonly VisualElement _minThumb;
        private readonly VisualElement _maxThumb;
        private readonly bool _isRange;
        private readonly List<VisualElement> _ticks = new List<VisualElement>();
        private float[] _snaps = new float[0];

        private float _low;
        private float _high = 1f;
        private float _origin;
        private int _draggingThumb = -1;

        public MfvSliderTrack(bool isRange)
        {
            _isRange = isRange;
            AddToClassList(ussClassName + "__track");

            var rail = new VisualElement();
            rail.AddToClassList(ussClassName + "__rail");
            Add(rail);

            _fill = new VisualElement();
            _fill.AddToClassList(ussClassName + "__fill");
            Add(_fill);

            _minThumb = new VisualElement();
            _minThumb.AddToClassList(ussClassName + "__thumb");
            Add(_minThumb);

            if (isRange)
            {
                _maxThumb = new VisualElement();
                _maxThumb.AddToClassList(ussClassName + "__thumb");
                Add(_maxThumb);
            }

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
        }

        /// <summary>Normalized 0..1 positions. For a single-thumb track only <see cref="High"/> is used.</summary>
        public float Low
        {
            get => _low;
            set { _low = Mathf.Clamp01(value); Refresh(); }
        }

        public float High
        {
            get => _high;
            set { _high = Mathf.Clamp01(value); Refresh(); }
        }

        /// <summary>
        /// Normalized point a single-thumb fill grows from. 0 fills from the left edge, and a
        /// slider that spans zero puts it at zero so negative values fill to the left.
        /// </summary>
        public float Origin
        {
            get => _origin;
            set { _origin = Mathf.Clamp01(value); Refresh(); }
        }

        /// <summary>Raised while dragging with the new normalized (low, high) pair.</summary>
        public event Action<float, float> Changed;

        /// <summary>
        /// Normalized points a dragged thumb sticks to, each marked by a tick under the rail.
        /// Typed values are not affected.
        /// </summary>
        public void SetSnaps(float[] normalized)
        {
            _snaps = normalized ?? new float[0];

            foreach (var tick in _ticks)
            {
                tick.RemoveFromHierarchy();
            }

            _ticks.Clear();

            // Right after the rail, so the fill and the thumbs draw over the ticks.
            var index = 1;
            foreach (var snap in _snaps)
            {
                var tick = new VisualElement { pickingMode = PickingMode.Ignore };
                tick.AddToClassList(ussClassName + "__tick");
                tick.style.left = Length.Percent(snap * 100f);
                Insert(index++, tick);
                _ticks.Add(tick);
            }
        }

        /// <summary>
        /// The nearest snap point when it lies within <paramref name="threshold"/> of
        /// <paramref name="t"/>, otherwise <paramref name="t"/> itself.
        /// </summary>
        public static float Snap(float t, float[] snaps, float threshold)
        {
            var result = t;
            var best = threshold;
            foreach (var snap in snaps)
            {
                var distance = Mathf.Abs(t - snap);
                if (distance <= best)
                {
                    best = distance;
                    result = snap;
                }
            }

            return result;
        }

        /// <summary>
        /// The snap distance in normalized units. Close points shrink it so the rail between
        /// two neighbouring ticks never becomes entirely sticky.
        /// </summary>
        private float SnapThreshold()
        {
            var width = contentRect.width;
            if (_snaps.Length == 0 || width <= 0f)
            {
                return 0f;
            }

            var gap = 1f;
            var previous = 0f;
            foreach (var snap in _snaps)
            {
                gap = Mathf.Min(gap, snap - previous);
                previous = snap;
            }

            gap = Mathf.Min(gap, 1f - previous);
            return Mathf.Min(SnapDistance / width, gap * 0.3f);
        }

        public void SetWithoutNotify(float low, float high)
        {
            _low = Mathf.Clamp01(low);
            _high = Mathf.Clamp01(high);
            Refresh();
        }

        private void Refresh()
        {
            if (_isRange)
            {
                var lo = Mathf.Min(_low, _high);
                var hi = Mathf.Max(_low, _high);
                _fill.style.left = Length.Percent(lo * 100f);
                _fill.style.right = Length.Percent((1f - hi) * 100f);
                _minThumb.style.left = Length.Percent(lo * 100f);
                _maxThumb.style.left = Length.Percent(hi * 100f);
            }
            else
            {
                var lo = Mathf.Min(_origin, _high);
                var hi = Mathf.Max(_origin, _high);
                _fill.style.left = Length.Percent(lo * 100f);
                _fill.style.right = Length.Percent((1f - hi) * 100f);
                _minThumb.style.left = Length.Percent(_high * 100f);
            }
        }

        private float NormalizedAt(Vector2 localPosition)
        {
            var width = contentRect.width;
            return width <= 0f ? 0f : Mathf.Clamp01(localPosition.x / width);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            var t = NormalizedAt(evt.localPosition);

            if (_isRange)
            {
                _draggingThumb = Mathf.Abs(t - _low) <= Mathf.Abs(t - _high) ? 0 : 1;
            }
            else
            {
                _draggingThumb = 1;
            }

            this.CapturePointer(evt.pointerId);
            Apply(t);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_draggingThumb < 0 || !this.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            Apply(NormalizedAt(evt.localPosition));
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_draggingThumb < 0)
            {
                return;
            }

            _draggingThumb = -1;
            this.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void Apply(float t)
        {
            t = Snap(t, _snaps, SnapThreshold());

            if (_isRange && _draggingThumb == 0)
            {
                _low = Mathf.Min(t, _high);
            }
            else if (_isRange)
            {
                _high = Mathf.Max(t, _low);
            }
            else
            {
                _high = t;
            }

            Refresh();
            Changed?.Invoke(_low, _high);
        }
    }
}
