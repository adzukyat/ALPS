using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Easing picker tile grid listing every curve. Each thumbnail plots the actual
    /// <see cref="AlpsEase"/> curve, so a tile can never drift from what plays back. A grid
    /// for the fall draws each curve on its way down, the way the fall plays it.
    ///
    /// Collapsed, it lists only the <see cref="Featured"/> curves on one row, and a picked
    /// curve that is not one of them after them, so what plays is always on show.
    /// </summary>
    public class AlpsEasingGrid : BaseField<int>
    {
        public new static readonly string ussClassName = "alps-easegrid";

        /// <summary>The curves a collapsed grid lists, in the order they show.</summary>
        public static readonly AlpsEaseType[] Featured =
        {
            AlpsEaseType.Linear,
            AlpsEaseType.InOutSine,
            AlpsEaseType.InCubic,
            AlpsEaseType.OutCubic,
        };

        private readonly VisualElement _container;
        private bool _collapsed;

        private readonly List<VisualElement> _tiles = new List<VisualElement>();
        private readonly List<AlpsEaseType> _types = new List<AlpsEaseType>();

        public AlpsEasingGrid(string label, bool falling = false)
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);
            AddToClassList("alps-row--top");
            Falling = falling;

            var container = this.Q(className: BaseField<int>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");
            _container = container;

            foreach (AlpsEaseType type in Enum.GetValues(typeof(AlpsEaseType)))
            {
                var easeType = type;
                var tile = new VisualElement { tooltip = easeType.ToString() };
                tile.AddToClassList(ussClassName + "__tile");
                tile.Add(BuildCurve(easeType, falling));
                tile.RegisterCallback<PointerDownEvent>(_ => value = (int)easeType);

                container.Add(tile);
                _tiles.Add(tile);
                _types.Add(easeType);
            }

            SetValueWithoutNotify((int)AlpsEaseType.Linear);
        }

        /// <summary>True when the tiles draw the fall, from the top down.</summary>
        public bool Falling { get; }

        /// <summary>Lists only the featured curves and the picked one. See <see cref="Featured"/>.</summary>
        public bool Collapsed
        {
            get => _collapsed;
            set
            {
                _collapsed = value;
                EnableInClassList(ussClassName + "--collapsed", value);
                RefreshTiles();
            }
        }

        /// <summary>The curves the grid shows, in the order it shows them. For tests.</summary>
        public IEnumerable<AlpsEaseType> ShownTypes()
        {
            foreach (var tile in _container.Children())
            {
                if (tile.style.display.value != DisplayStyle.None)
                {
                    yield return _types[_tiles.IndexOf(tile)];
                }
            }
        }

        /// <summary>
        /// The curve a tile draws, in 0..1 over the part it shapes: the ease itself for the
        /// rise, and one minus it for the fall, matching <see cref="AlpsPhaseCurve.Wave"/>.
        /// </summary>
        public static float TileValue(AlpsEaseType easeType, bool falling, float t)
        {
            var v = AlpsEase.Evaluate(easeType, t);
            return falling ? 1f - v : v;
        }

        private static VisualElement BuildCurve(AlpsEaseType easeType, bool falling)
        {
            // 24x18 view box with a 2px inset.
            var points = new List<Vector2>();
            const int samples = 24;
            for (var i = 0; i <= samples; i++)
            {
                var t = i / (float)samples;
                var v = TileValue(easeType, falling, t);
                points.Add(new Vector2(2f + t * 20f, 16f - v * 14f));
            }

            var icon = new AlpsVectorIcon { StrokeWidth = 1.5f };
            icon.AddToClassList(ussClassName + "__curve");
            icon.SetViewBox(new Vector2(24f, 18f));
            icon.SetContours(new[] { points });
            return icon;
        }

        /// <summary>Committing while mixed always reaches the other clips. See <see cref="AlpsMixedField"/>.</summary>
        public override int value
        {
            get => base.value;
            set => AlpsMixedField.Set(this, value, v => base.value = v);
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(newValue);
            RefreshSelection();
            RefreshTiles();
        }

        /// <summary>Mixed selects no tile, since no single curve holds for every clip.</summary>
        protected override void UpdateMixedValueContent()
        {
            RefreshSelection();
            RefreshTiles();
        }

        /// <summary>
        /// Shows every tile in the list's order, or collapsed the featured ones in theirs and
        /// the picked curve after them when it is not one of them.
        /// </summary>
        private void RefreshTiles()
        {
            if (_container == null)
            {
                return;
            }

            var picked = showMixedValue ? -1 : value;
            _container.Sort((a, b) => Rank(_types[_tiles.IndexOf(a)]).CompareTo(Rank(_types[_tiles.IndexOf(b)])));
            for (var i = 0; i < _tiles.Count; i++)
            {
                var shown = !_collapsed || Array.IndexOf(Featured, _types[i]) >= 0 || (int)_types[i] == picked;
                _tiles[i].style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Where a curve sorts: in list order expanded, and the featured ones first collapsed.</summary>
        private int Rank(AlpsEaseType type)
        {
            if (!_collapsed)
            {
                return (int)type;
            }

            var featured = Array.IndexOf(Featured, type);
            return featured >= 0 ? featured : Featured.Length + (int)type;
        }

        private void RefreshSelection()
        {
            for (var i = 0; i < _tiles.Count; i++)
            {
                var selected = (int)_types[i] == value && !showMixedValue;
                _tiles[i].EnableInClassList(ussClassName + "__tile--selected", selected);
                var icon = _tiles[i].Q<AlpsVectorIcon>();
                if (icon != null)
                {
                    icon.IconColor = selected ? Color.white : new Color(0.847f, 0.847f, 0.847f);
                }
            }
        }
    }
}
