using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The easing row: the rise's tiles on the left and the fall's on the right, split by a
    /// dotted line that runs up between their 上り / 下り headings, each centred on its tiles. Each side is its own grid
    /// bound to its own field, so a mixed selection and carrying a pick to the other clips
    /// work per side. Both grids list a few curves until the show all switch under them opens
    /// them, and a side the wave does not have stays in place, greyed out.
    /// </summary>
    public class AlpsEasingPicker : VisualElement
    {
        public static readonly string ussClassName = "alps-easepicker";

        public const int RiseSide = 0;
        public const int FallSide = 1;

        /// <summary>
        /// Whether the grids list every curve, kept across rebuilds of the view so an undo or
        /// a paste does not fold them away again.
        /// </summary>
        private static bool s_expanded;

        private readonly VisualElement[] _sides = new VisualElement[2];
        private readonly Label[] _headings = new Label[2];
        private readonly Label _more;

        private bool _inOut;

        public AlpsEasingPicker(string label)
        {
            AddToClassList(ussClassName);
            AddToClassList("alps-row");
            AddToClassList("alps-row--top");

            var rowLabel = new Label(label);
            rowLabel.AddToClassList("alps-row__label");
            Add(rowLabel);

            var body = new VisualElement();
            body.AddToClassList(ussClassName + "__body");
            Add(body);

            var sides = new VisualElement();
            sides.AddToClassList(ussClassName + "__sides");
            body.Add(sides);

            Rise = new AlpsEasingGrid(null);
            Fall = new AlpsEasingGrid(null, true);
            var grids = new[] { Rise, Fall };
            for (var i = 0; i < grids.Length; i++)
            {
                var side = new VisualElement();
                side.AddToClassList(ussClassName + "__side");
                side.AddToClassList(ussClassName + (i == RiseSide ? "__side--rise" : "__side--fall"));

                // The heading and the tiles size together, so the heading centres on them.
                var cluster = new VisualElement();
                cluster.AddToClassList(ussClassName + "__cluster");
                side.Add(cluster);

                var heading = new Label();
                heading.AddToClassList(ussClassName + "__heading");
                cluster.Add(heading);

                var grid = grids[i];
                grid.AddToClassList(ussClassName + "__grid");

                cluster.Add(grid);
                _sides[i] = side;
                _headings[i] = heading;

                sides.Add(side);
                if (i == RiseSide)
                {
                    sides.Add(new DottedLine());
                }
            }

            _more = new Label();
            _more.AddToClassList(ussClassName + "__more");
            _more.RegisterCallback<PointerDownEvent>(evt =>
            {
                s_expanded = !s_expanded;
                RefreshExpanded();
                evt.StopPropagation();
            });
            body.Add(_more);

            RefreshHeadings();
            RefreshExpanded();
        }

        public AlpsEasingGrid Rise { get; }

        public AlpsEasingGrid Fall { get; }

        /// <summary>The show all / show less switch under the grids.</summary>
        public Label More => _more;

        /// <summary>True while the grids list every curve.</summary>
        public static bool Expanded
        {
            get => s_expanded;
            internal set => s_expanded = value;
        }

        /// <summary>
        /// Greys out <paramref name="side"/>, the one the wave does not have, and keeps it from
        /// being picked. Pass -1 when both are used.
        /// </summary>
        public void SetUnused(int side)
        {
            for (var i = 0; i < _sides.Length; i++)
            {
                var unused = i == side;
                _sides[i].SetEnabled(!unused);
                _sides[i].EnableInClassList(ussClassName + "__side--unused", unused);
            }
        }

        /// <summary>Heads the sides イン / アウト for an in / out phase, and 上り / 下り otherwise.</summary>
        public void SetInOut(bool inOut)
        {
            if (_inOut == inOut)
            {
                return;
            }

            _inOut = inOut;
            RefreshHeadings();
        }

        /// <summary>The heading over <paramref name="side"/>, for tests.</summary>
        public string Heading(int side)
        {
            return _headings[side].text;
        }

        private void RefreshHeadings()
        {
            _headings[RiseSide].text = AlpsStrings.Tr(_inOut ? "phase.easing.in" : "common.rise");
            _headings[FallSide].text = AlpsStrings.Tr(_inOut ? "phase.easing.out" : "common.fall");
        }

        private void RefreshExpanded()
        {
            Rise.Collapsed = !s_expanded;
            Fall.Collapsed = !s_expanded;
            EnableInClassList(ussClassName + "--collapsed", !s_expanded);
            EnableInClassList(ussClassName + "--expanded", s_expanded);
            _more.text = AlpsStrings.Tr(s_expanded ? "phase.easing.showLess" : "phase.easing.showAll");
        }

        /// <summary>
        /// The vertical dotted line between the sides. UI Toolkit has no dotted border, so it
        /// draws its dots itself, in its own text colour so the style sheet sets it.
        /// </summary>
        private sealed class DottedLine : VisualElement
        {
            private const float Dot = 2f;
            private const float Gap = 2f;

            public DottedLine()
            {
                AddToClassList(ussClassName + "__divider");
                generateVisualContent += OnGenerateVisualContent;
            }

            private void OnGenerateVisualContent(MeshGenerationContext context)
            {
                var rect = contentRect;
                var painter = context.painter2D;
                painter.strokeColor = resolvedStyle.color;
                painter.lineWidth = 1f;
                var x = Mathf.Floor(rect.width * 0.5f) + 0.5f;
                for (var y = 0f; y < rect.height; y += Dot + Gap)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, y));
                    painter.LineTo(new Vector2(x, Mathf.Min(rect.height, y + Dot)));
                    painter.Stroke();
                }
            }
        }
    }
}
