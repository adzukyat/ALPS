using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// Easing picker tile grid listing every curve. Each thumbnail plots the actual
    /// <see cref="MfvEase"/> curve, so a tile can never drift from what plays back.
    /// </summary>
    public class MfvEasingGrid : BaseField<int>
    {
        public new static readonly string ussClassName = "mfv-easegrid";

        private readonly List<VisualElement> _tiles = new List<VisualElement>();
        private readonly List<MfvEaseType> _types = new List<MfvEaseType>();

        public MfvEasingGrid(string label)
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            AddToClassList("mfv-row--top");

            var container = this.Q(className: BaseField<int>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            foreach (MfvEaseType type in Enum.GetValues(typeof(MfvEaseType)))
            {
                var easeType = type;
                var tile = new VisualElement { tooltip = easeType.ToString() };
                tile.AddToClassList(ussClassName + "__tile");
                tile.Add(BuildCurve(easeType));
                tile.RegisterCallback<PointerDownEvent>(_ => value = (int)easeType);

                container.Add(tile);
                _tiles.Add(tile);
                _types.Add(easeType);
            }

            SetValueWithoutNotify((int)MfvEaseType.Linear);
        }

        private static VisualElement BuildCurve(MfvEaseType easeType)
        {
            // 24x18 view box with a 2px inset.
            var points = new List<Vector2>();
            const int samples = 24;
            for (var i = 0; i <= samples; i++)
            {
                var t = i / (float)samples;
                var v = MfvEase.Evaluate(easeType, t);
                points.Add(new Vector2(2f + t * 20f, 16f - v * 14f));
            }

            var icon = new MfvVectorIcon { StrokeWidth = 1.5f };
            icon.AddToClassList(ussClassName + "__curve");
            icon.SetViewBox(new Vector2(24f, 18f));
            icon.SetContours(new[] { points });
            return icon;
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(newValue);
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            for (var i = 0; i < _tiles.Count; i++)
            {
                var selected = (int)_types[i] == value;
                _tiles[i].EnableInClassList(ussClassName + "__tile--selected", selected);
                var icon = _tiles[i].Q<MfvVectorIcon>();
                if (icon != null)
                {
                    icon.IconColor = selected ? Color.white : new Color(0.847f, 0.847f, 0.847f);
                }
            }
        }
    }
}
