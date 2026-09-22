using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The bare rail / fill / thumb geometry, in one or two thumb form.
    ///
    /// Unity's own Slider and MinMaxSlider carry a fixed internal hierarchy that the
    /// flat 4px rail and 11x14 rounded thumbs do not map onto cleanly, so the
    /// track is drawn directly and the drag handling lives here.
    /// </summary>
    public class AlpsSliderTrack : VisualElement
    {
        public new static readonly string ussClassName = "alps-slider";

        /// <summary>Dims the fill and thumbs while the slider's field shows mixed values.</summary>
        public static readonly string MixedClass = ussClassName + "__track--mixed";

        /// <summary>How close, in pixels, a dragged thumb has to come to a snap point to land on it.</summary>
        public const float SnapDistance = 5f;

        /// <summary>Thumb identifiers used by <see cref="ResetRequested"/>. A single-thumb track only uses the high one.</summary>
        public const int ThumbLow = 0;
        public const int ThumbHigh = 1;

        /// <summary>Drag state for moving both thumbs of a range together.</summary>
        private const int DragBar = 2;

        /// <summary>Half of the 11px thumb in AlpsInspector.uss.</summary>
        private const float ThumbHalfWidth = 5.5f;

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
        private float _grabOffset;

        public AlpsSliderTrack(bool isRange)
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

        /// <summary>Raised when a thumb is double clicked, with <see cref="ThumbLow"/> or <see cref="ThumbHigh"/>.</summary>
        public event Action<int> ResetRequested;

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

        /// <summary>
        /// Which end a thumb element shows. The left thumb draws the smaller end, so a range
        /// stored backwards maps it to <see cref="ThumbHigh"/>. -1 when the target is not a thumb.
        /// </summary>
        private int ThumbOf(IEventHandler target)
        {
            if (!_isRange)
            {
                return target == _minThumb ? ThumbHigh : -1;
            }

            var lowIsLeft = _low <= _high;
            if (target == _minThumb)
            {
                return lowIsLeft ? ThumbLow : ThumbHigh;
            }

            if (target == _maxThumb)
            {
                return lowIsLeft ? ThumbHigh : ThumbLow;
            }

            return -1;
        }

        private float PositionOf(int thumb)
        {
            return thumb == ThumbLow ? _low : _high;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            var t = NormalizedAt(evt.localPosition);
            var thumb = ThumbOf(evt.target);

            if (evt.clickCount == 2 && thumb >= 0)
            {
                ResetRequested?.Invoke(thumb);
                evt.StopPropagation();
                return;
            }

            _grabOffset = 0f;
            if (thumb >= 0)
            {
                // Grabbing a thumb off centre keeps the value, so a click alone never moves it.
                _draggingThumb = thumb;
                _grabOffset = PositionOf(thumb) - t;
            }
            else if (_isRange && IsBetweenThumbs(evt.localPosition.x))
            {
                _draggingThumb = DragBar;
                _grabOffset = _low - t;
            }
            else if (_isRange)
            {
                _draggingThumb = Mathf.Abs(t - _low) <= Mathf.Abs(t - _high) ? ThumbLow : ThumbHigh;
            }
            else
            {
                _draggingThumb = ThumbHigh;
            }

            this.CapturePointer(evt.pointerId);
            if (thumb < 0 && _draggingThumb != DragBar)
            {
                Apply(t);
            }

            evt.StopPropagation();
        }

        /// <summary>True on the rail strictly between the two thumbs, clear of both of them.</summary>
        private bool IsBetweenThumbs(float x)
        {
            var width = contentRect.width;
            var left = Mathf.Min(_low, _high) * width + ThumbHalfWidth;
            var right = Mathf.Max(_low, _high) * width - ThumbHalfWidth;
            return x > left && x < right;
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
            t += _grabOffset;

            if (_draggingThumb == DragBar)
            {
                var slid = Slide(_low, _high, t, _snaps, SnapThreshold());
                _low = slid.x;
                _high = slid.y;
            }
            else if (_isRange)
            {
                t = Snap(Mathf.Clamp01(t), _snaps, SnapThreshold());

                // A thumb dragged past the other one takes over its end, so a closed range
                // opens in whichever direction the drag goes.
                if (_draggingThumb == ThumbLow && t > _high)
                {
                    _low = _high;
                    _draggingThumb = ThumbHigh;
                }
                else if (_draggingThumb == ThumbHigh && t < _low)
                {
                    _high = _low;
                    _draggingThumb = ThumbLow;
                }

                if (_draggingThumb == ThumbLow)
                {
                    _low = t;
                }
                else
                {
                    _high = t;
                }
            }
            else
            {
                _high = Snap(Mathf.Clamp01(t), _snaps, SnapThreshold());
            }

            Refresh();
            Changed?.Invoke(_low, _high);
        }

        /// <summary>
        /// Moves the range so its low end sits at <paramref name="low"/> while keeping its width
        /// and staying inside the track. When either end comes within
        /// <paramref name="threshold"/> of a snap point the whole range shifts onto it, and
        /// the end that needs the smaller shift wins.
        /// </summary>
        public static Vector2 Slide(float currentLow, float currentHigh, float low, float[] snaps, float threshold)
        {
            var width = currentHigh - currentLow;
            var min = Mathf.Max(0f, -width);
            var max = Mathf.Min(1f, 1f - width);
            low = Mathf.Clamp(low, min, max);

            var high = low + width;
            var lowShift = Snap(low, snaps, threshold) - low;
            var highShift = Snap(high, snaps, threshold) - high;
            var shift = lowShift;
            if (Mathf.Approximately(lowShift, 0f) || (!Mathf.Approximately(highShift, 0f) && Mathf.Abs(highShift) < Mathf.Abs(lowShift)))
            {
                shift = highShift;
            }

            low = Mathf.Clamp(low + shift, min, max);
            return new Vector2(low, low + width);
        }
    }
}
