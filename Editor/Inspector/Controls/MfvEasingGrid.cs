using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// Easing picker tile grid. Each thumbnail plots the actual
    /// <see cref="MfvEase"/> curve, so a tile can never drift from what plays back.
    /// The trailing "…" tile opens the full list.
    /// </summary>
    public class MfvEasingGrid : BaseField<int>
    {
        public new static readonly string ussClassName = "mfv-easegrid";

        /// <summary>The nine curves shown up front, before the overflow tile.</summary>
        public static readonly MfvEaseType[] Featured =
        {
            MfvEaseType.Linear,
            MfvEaseType.InQuad,
            MfvEaseType.OutQuad,
            MfvEaseType.InOutCubic,
            MfvEaseType.InExpo,
            MfvEaseType.OutExpo,
            MfvEaseType.OutBack,
            MfvEaseType.OutBounce,
            MfvEaseType.Step,
        };

        private readonly List<VisualElement> _tiles = new List<VisualElement>();
        private readonly List<MfvEaseType> _shown = new List<MfvEaseType>();
        private readonly VisualElement _container;
        private readonly int _featuredCount;

        public MfvEasingGrid(string label, int featuredCount = 9)
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            AddToClassList("mfv-row--top");
            _featuredCount = Mathf.Clamp(featuredCount, 1, Featured.Length);

            _container = this.Q(className: BaseField<int>.inputUssClassName);
            _container.AddToClassList(ussClassName + "__input");

            Rebuild(false);
            SetValueWithoutNotify((int)MfvEaseType.Linear);
        }

        /// <summary>True once the "…" tile has been used and every curve is listed.</summary>
        public bool ShowAll { get; private set; }

        private void Rebuild(bool showAll)
        {
            ShowAll = showAll;
            _container.Clear();
            _tiles.Clear();
            _shown.Clear();

            var types = showAll
                ? (MfvEaseType[])Enum.GetValues(typeof(MfvEaseType))
                : SubArray(Featured, _featuredCount);

            foreach (var type in types)
            {
                var easeType = type;
                var tile = new VisualElement { tooltip = easeType.ToString() };
                tile.AddToClassList(ussClassName + "__tile");
                tile.Add(BuildCurve(easeType));
                tile.RegisterCallback<PointerDownEvent>(_ => value = (int)easeType);

                _container.Add(tile);
                _tiles.Add(tile);
                _shown.Add(easeType);
            }

            if (!showAll)
            {
                var more = new VisualElement { tooltip = "すべてのイージングを表示" };
                more.AddToClassList(ussClassName + "__tile");
                var label = new Label("…");
                label.AddToClassList(ussClassName + "__more");
                more.Add(label);
                more.RegisterCallback<PointerDownEvent>(_ =>
                {
                    Rebuild(true);
                    RefreshSelection();
                });
                _container.Add(more);
            }
        }

        private static MfvEaseType[] SubArray(MfvEaseType[] source, int count)
        {
            var result = new MfvEaseType[Mathf.Min(count, source.Length)];
            Array.Copy(source, result, result.Length);
            return result;
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
            EnsureVisible((MfvEaseType)newValue);
            RefreshSelection();
        }

        private void EnsureVisible(MfvEaseType easeType)
        {
            if (ShowAll || _shown.Contains(easeType))
            {
                return;
            }

            // A curve selected from the overflow list must stay on screen.
            Rebuild(true);
        }

        private void RefreshSelection()
        {
            for (var i = 0; i < _tiles.Count; i++)
            {
                var selected = (int)_shown[i] == value;
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
