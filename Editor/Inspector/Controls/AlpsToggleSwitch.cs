using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>Invert / own phase / blackout on return: a pill-shaped switch.</summary>
    public class AlpsToggleSwitch : BaseField<bool>
    {
        public new static readonly string ussClassName = "alps-switch";

        private readonly VisualElement _track;
        private readonly VisualElement _knob;

        public AlpsToggleSwitch(string label)
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
    /// Letter toggle: the small square that sits right of a slider. "R" widens the
    /// parameter into a min/max pair, "S" turns on spread.
    /// </summary>
    public class AlpsRangeFlag : BaseField<bool>
    {
        public new static readonly string ussClassName = "alps-rangeflag";

        private readonly Label _box;

        public AlpsRangeFlag(string letter = "R", string tip = "レンジ")
            : base(null, new VisualElement())
        {
            AddToClassList(ussClassName);
            tooltip = tip;

            var container = this.Q(className: BaseField<bool>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            _box = new Label(letter);
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
