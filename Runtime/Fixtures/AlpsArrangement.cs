using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// A container that lays its direct children out on a line, a circle or arc, a polygon,
    /// a rectangle or a grid. Children keep the hierarchy order, inactive ones included, and
    /// the editor lays them out again whenever the settings or the children change, so their
    /// positions and rotations belong to the container.
    ///
    /// It only arranges the scene while editing. The build and play mode copies of the
    /// scene drop it and keep the transforms it left.
    ///
    /// On the same object as an <see cref="AlpsFixtureGroup"/>, the group's fixture list
    /// follows the children's order.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Adzuki Live Performance System/ALPS Arrangement")]
    public class AlpsArrangement : MonoBehaviour
    {
        public AlpsArrangementSettings settings = new AlpsArrangementSettings();

        /// <summary>
        /// False until the editor has taken the first layout, which starts from the children
        /// as they are and the fixture group's order. Reset clears it again.
        /// </summary>
        [SerializeField, HideInInspector] private bool initialized;

        /// <summary>
        /// Whether the fixture group on this object follows the children's order. Off when
        /// the children could not be reordered to keep the group's order, until the user
        /// chooses to sync anyway.
        /// </summary>
        [SerializeField, HideInInspector] private bool syncFixtureGroup = true;

        public bool Initialized
        {
            get => initialized;
            set => initialized = value;
        }

        public bool SyncFixtureGroup
        {
            get => syncFixtureGroup;
            set => syncFixtureGroup = value;
        }

        public int SlotCount => transform.childCount;

        public Transform GetSlot(int index)
        {
            return transform.GetChild(index);
        }

        // Gives the component its enabled checkbox. Turning it off pauses the layout and the
        // fixture group sync, so children can be adjusted by hand.
        private void OnEnable()
        {
        }
    }
}
