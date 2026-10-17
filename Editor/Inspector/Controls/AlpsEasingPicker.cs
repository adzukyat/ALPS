using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The easing rows: a segmented 上り / 下り switch under the row label, and the tile grid
    /// of the picked side under it in the input column. Each side is its own grid bound to its
    /// own field, so a mixed selection and carrying a pick to the other clips work per side.
    /// The switch only picks which grid shows. A wave that has only one of the two sides
    /// shows that grid alone, with the row label beside it in place of the switch.
    /// </summary>
    public class AlpsEasingPicker : VisualElement
    {
        public static readonly string ussClassName = "alps-easepicker";

        public const int RiseSide = 0;
        public const int FallSide = 1;

        /// <summary>
        /// The side last picked, kept across rebuilds of the view so an undo or a paste does
        /// not throw the user back to the rise.
        /// </summary>
        private static int s_side = RiseSide;

        private readonly string _label;
        private readonly Label _tileLabel;

        /// <summary>The one side shown without the switch, or -1 while both are.</summary>
        private int _only = -1;

        public AlpsEasingPicker(string label, bool compact)
        {
            _label = label;
            AddToClassList(ussClassName);

            Side = new AlpsSegmentedControl(label, AlpsStrings.Tr("common.rise"), AlpsStrings.Tr("common.fall"));
            if (compact)
            {
                Side.AddToClassList("alps-seg--compact");
            }

            Side.SetValueWithoutNotify(s_side);
            Side.RegisterValueChangedCallback(evt =>
            {
                s_side = evt.newValue;
                RefreshSide();
            });
            Add(Side);

            // The grid has no label of its own. A label wide spacer keeps it in the input column,
            // and takes the row label while the switch is hidden.
            var tiles = new VisualElement();
            tiles.AddToClassList("alps-row");
            tiles.AddToClassList("alps-row--top");
            tiles.AddToClassList(ussClassName + "__tiles");

            _tileLabel = new Label();
            _tileLabel.AddToClassList("alps-row__label");
            tiles.Add(_tileLabel);

            Rise = new AlpsEasingGrid(null);
            Fall = new AlpsEasingGrid(null, true);
            foreach (var grid in new[] { Rise, Fall })
            {
                grid.AddToClassList(ussClassName + "__grid");
                if (compact)
                {
                    grid.AddToClassList("alps-easegrid--compact");
                }

                tiles.Add(grid);
            }

            Add(tiles);
            RefreshSide();
        }

        /// <summary>Which side the grid below edits. UI state only, never saved in the model.</summary>
        public AlpsSegmentedControl Side { get; }

        public AlpsEasingGrid Rise { get; }

        public AlpsEasingGrid Fall { get; }

        /// <summary>
        /// Shows both sides behind the switch, or only <paramref name="side"/> without it.
        /// Pass -1 for both. The side the switch had picked is kept for when it comes back.
        /// </summary>
        public void ShowOnly(int side)
        {
            _only = side;
            AlpsPhaseSettingsView.Show(Side, side < 0);
            _tileLabel.text = side < 0 ? string.Empty : _label;
            EnableInClassList(ussClassName + "--single", side >= 0);
            RefreshSide();
        }

        private void RefreshSide()
        {
            var fall = (_only < 0 ? Side.value : _only) == FallSide;
            AlpsPhaseSettingsView.Show(Rise, !fall);
            AlpsPhaseSettingsView.Show(Fall, fall);
        }
    }
}
