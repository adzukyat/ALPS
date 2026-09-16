using System;
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

        private readonly VisualElement _fill;
        private readonly VisualElement _minThumb;
        private readonly VisualElement _maxThumb;
        private readonly bool _isRange;

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
