using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>Angle / track user: the wide icon+label tab strip inside a card.</summary>
    public class AlpsTabControl : BaseField<int>
    {
        public new static readonly string ussClassName = "alps-tabs";

        private readonly List<VisualElement> _items = new List<VisualElement>();

        public AlpsTabControl(params (string Label, string[] Icon)[] tabs)
            : base(null, new VisualElement())
        {
            AddToClassList(ussClassName);

            var container = this.Q(className: BaseField<int>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            for (var i = 0; i < tabs.Length; i++)
            {
                var index = i;
                var item = new VisualElement();
                item.AddToClassList(ussClassName + "__item");
                if (i == 0)
                {
                    item.AddToClassList(ussClassName + "__item--first");
                }

                if (i == tabs.Length - 1)
                {
                    item.AddToClassList(ussClassName + "__item--last");
                }

                var icon = new AlpsVectorIcon();
                icon.AddToClassList(ussClassName + "__icon");
                icon.SetPaths(tabs[i].Icon);
                item.Add(icon);
                item.Add(new Label(tabs[i].Label));

                item.RegisterCallback<PointerDownEvent>(_ => value = index);
                container.Add(item);
                _items.Add(item);
            }

            SetValueWithoutNotify(0);
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(Mathf.Clamp(newValue, 0, Mathf.Max(0, _items.Count - 1)));

            for (var i = 0; i < _items.Count; i++)
            {
                var selected = i == value;
                _items[i].EnableInClassList(ussClassName + "__item--selected", selected);
                var icon = _items[i].Q<AlpsVectorIcon>();
                if (icon != null)
                {
                    icon.IconColor = selected ? Color.white : new Color(0.741f, 0.741f, 0.741f);
                }
            }
        }
    }
}
