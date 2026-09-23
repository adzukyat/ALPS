using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Groups objects as its children for ALPS tracks to drive. A lighting track bound here
    /// drives every fixture below it, in hierarchy order, inactive ones included, and that
    /// order is the fixture number the order modes and every per fixture offset count.
    ///
    /// It can also lay its direct children out on a line, a circle or arc, a polygon, a
    /// rectangle or a grid. The shape starts off, which leaves the children where they are.
    /// With a shape on, the editor lays them out again whenever the settings or the children
    /// change, so their positions and rotations belong to the container.
    ///
    /// It only exists while editing. The build and play mode copies of the scene drop it and
    /// keep the transforms it left.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Adzuki Live Performance System/ALPS Container")]
    public class AlpsContainer : AlpsTarget
    {
        public AlpsArrangementSettings settings = new AlpsArrangementSettings();

        /// <summary>
        /// False until the editor has laid the children out for the first time, which starts
        /// the line and the rotation from the children as they are. Reset clears it again.
        /// </summary>
        [SerializeField, HideInInspector] private bool initialized;

        /// <summary>
        /// The fixture list of the fixture group this component used to be. The editor orders
        /// the children the way it listed them, then clears it, since the order now comes from
        /// the hierarchy.
        /// </summary>
        [FormerlySerializedAs("fixtures")]
        [SerializeField, HideInInspector] private List<AlpsFixture> legacyFixtures = new List<AlpsFixture>();

        public bool Initialized
        {
            get => initialized;
            set => initialized = value;
        }

        /// <summary>The fixture group list still waiting to be turned into the children's order.</summary>
        public List<AlpsFixture> LegacyFixtures => legacyFixtures;

        /// <summary>True while the children are laid out, false while the shape is off.</summary>
        public bool Arranges => settings != null && settings.shape != AlpsArrangementShape.Off;

        public int SlotCount => transform.childCount;

        public Transform GetSlot(int index)
        {
            return transform.GetChild(index);
        }

        public override void CollectFixtures(List<AlpsFixture> into)
        {
            into.AddRange(GetComponentsInChildren<AlpsFixture>(true));
        }
    }
}
