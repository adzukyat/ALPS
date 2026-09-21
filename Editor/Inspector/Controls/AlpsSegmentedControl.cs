using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Order / mode / timing: a joined row of mutually exclusive options.
    ///
    /// Unity 2022.3 has no ToggleButtonGroup (2023.2+), so this is a BaseField<int>
    /// over the selected index, which keeps it bindable to a serialized enum.
    /// </summary>
    public class AlpsSegmentedControl : BaseField<int>
    {
        public new static readonly string ussClassName = "alps-seg";

        private readonly List<Label> _items = new List<Label>();
        private readonly VisualElement _container;

        public AlpsSegmentedControl(string label, params string[] options)
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);

            _container = this.Q(className: BaseField<int>.inputUssClassName);
            _container.AddToClassList(ussClassName + "__input");

            for (var i = 0; i < options.Length; i++)
            {
                var index = i;
                var item = new Label(options[i]);
                item.AddToClassList(ussClassName + "__item");
                if (i == 0)
                {
                    item.AddToClassList(ussClassName + "__item--first");
                }

                item.RegisterCallback<PointerDownEvent>(_ => value = index);
                _container.Add(item);
                _items.Add(item);
            }

            SetValueWithoutNotify(0);
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(Mathf.Clamp(newValue, 0, Mathf.Max(0, _items.Count - 1)));
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            for (var i = 0; i < _items.Count; i++)
            {
                _items[i].EnableInClassList(ussClassName + "__item--selected", i == value);
            }
        }
    }
}
