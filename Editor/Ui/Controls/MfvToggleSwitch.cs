using UnityEngine.UIElements;

namespace ManeuverForVRC.Ui.Editor
{
    /// <summary>Invert / cancel on return / own phase: a pill-shaped switch.</summary>
    public class MfvToggleSwitch : BaseField<bool>
    {
        public new static readonly string ussClassName = "mfv-switch";

        private readonly VisualElement _track;
        private readonly VisualElement _knob;

        public MfvToggleSwitch(string label)
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);

            var container = this.Q(className: BaseField<bool>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _track = new VisualElement();
            _track.AddToClassList(ussClassName + "__track");

            _knob = new VisualElement();
            _knob.AddToClassList(ussClassName + "__knob");
            _track.Add(_knob);
            container.Add(_track);

            _track.RegisterCallback<PointerDownEvent>(_ => value = !value);
            SetValueWithoutNotify(false);
        }

        public override void SetValueWithoutNotify(bool newValue)
        {
            base.SetValueWithoutNotify(newValue);
            _track.EnableInClassList(ussClassName + "__track--on", newValue);
            _knob.EnableInClassList(ussClassName + "__knob--on", newValue);
        }
    }

    /// <summary>
    /// Range toggle: the small square "R" that sits immediately right of a
    /// slider and widens the parameter into a min/max pair.
    /// </summary>
    public class MfvRangeFlag : BaseField<bool>
    {
        public new static readonly string ussClassName = "mfv-rangeflag";

        private readonly Label _box;

        public MfvRangeFlag()
            : base(null, new VisualElement())
        {
            AddToClassList(ussClassName);
            tooltip = "Range";

            var container = this.Q(className: BaseField<bool>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _box = new Label("R");
            _box.AddToClassList(ussClassName + "__box");
            container.Add(_box);

            _box.RegisterCallback<PointerDownEvent>(_ => value = !value);
            SetValueWithoutNotify(false);
        }

        public override void SetValueWithoutNotify(bool newValue)
        {
            base.SetValueWithoutNotify(newValue);
            _box.EnableInClassList(ussClassName + "__box--on", newValue);
        }
    }
}
