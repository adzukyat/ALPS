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

        /// <summary>Committing while mixed always reaches the other clips. See <see cref="AlpsMixedField"/>.</summary>
        public override bool value
        {
            get => base.value;
            set => AlpsMixedField.Set(this, value, v => base.value = v);
        }

        public override void SetValueWithoutNotify(bool newValue)
        {
            base.SetValueWithoutNotify(newValue);
            RefreshState();
        }

        /// <summary>Mixed parks the knob in the middle, neither on nor off.</summary>
        protected override void UpdateMixedValueContent()
        {
            RefreshState();
        }

        private void RefreshState()
        {
            var on = value && !showMixedValue;
            _track.EnableInClassList(ussClassName + "__track--on", on);
            _knob.EnableInClassList(ussClassName + "__knob--on", on);
            _knob.EnableInClassList(ussClassName + "__knob--mixed", showMixedValue);
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

        /// <summary>Committing while mixed always reaches the other clips. See <see cref="AlpsMixedField"/>.</summary>
        public override bool value
        {
            get => base.value;
            set => AlpsMixedField.Set(this, value, v => base.value = v);
        }

        public override void SetValueWithoutNotify(bool newValue)
        {
            base.SetValueWithoutNotify(newValue);
            RefreshState();
        }

        protected override void UpdateMixedValueContent()
        {
            RefreshState();
        }

        private void RefreshState()
        {
            _box.EnableInClassList(ussClassName + "__box--on", value && !showMixedValue);
            _box.EnableInClassList(ussClassName + "__box--mixed", showMixedValue);
        }
    }
}
