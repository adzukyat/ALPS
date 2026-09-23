using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The easing rows: a segmented 上り / 下り switch under the row label, and the tile grid
    /// of the picked side under it in the input column. Each side is its own grid bound to its
    /// own field, so a mixed selection and carrying a pick to the other clips work per side.
    /// The switch only picks which grid shows.
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

        public AlpsEasingPicker(string label, bool compact)
        {
            AddToClassList(ussClassName);

            Side = new AlpsSegmentedControl(label, "上り", "下り");
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

            // The grid has no label of its own. A label wide spacer keeps it in the input column.
            var tiles = new VisualElement();
            tiles.AddToClassList("alps-row");
            tiles.AddToClassList("alps-row--top");
            tiles.AddToClassList(ussClassName + "__tiles");

            var spacer = new VisualElement();
            spacer.AddToClassList("alps-row__label");
            tiles.Add(spacer);

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

        private void RefreshSide()
        {
            var fall = Side.value == FallSide;
            AlpsPhaseSettingsView.Show(Rise, !fall);
            AlpsPhaseSettingsView.Show(Fall, fall);
        }
    }
}
