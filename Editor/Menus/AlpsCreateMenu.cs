using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// ALPS in the Hierarchy's create menu (right click, the + button and the GameObject
    /// menu): a container to hold fixtures, and VR Stage Lighting fixtures that already carry
    /// an ALPS VRSL Fixture.
    ///
    /// New objects go under the object the menu was opened on, like Unity's own create
    /// items. Opened on a fixture, they go next to it instead, since a light never belongs
    /// inside another light, so adding one after another fills the same container.
    /// </summary>
    public static class AlpsCreateMenu
    {
        public const string ContainerName = "ALPS Container";

        private const string Root = "GameObject/ALPS/";

        // Up to 49 the items also show in the Hierarchy's context menu. The gap between the
        // two groups draws a separator.
        private const int ContainerPriority = 10;
        private const int FixturePriority = 30;

        private const string PrefabFolder = "Packages/com.acchosen.vr-stage-lighting/Runtime/Prefabs/DMX/Horizontal Mode/";

        public static readonly AlpsVrslPrefab MoverSpotlight = new AlpsVrslPrefab(
            "Mover Spotlight", "f5be3cfe3f15bfb4e9477904c5af9daf", PrefabFolder + "VRSL-DMX-Mover-Spotlight-H-13CH.prefab");

        public static readonly AlpsVrslPrefab MoverWashLight = new AlpsVrslPrefab(
            "Mover Wash Light", "b3e8ff051cc2d684aa255ceccce9b96f", PrefabFolder + "VRSL-DMX-Mover-WashLight-H-13CH.prefab");

        public static readonly AlpsVrslPrefab ParLight = new AlpsVrslPrefab(
            "Par Light", "946b3c09cfa93244c90a4b0ac7764b44", PrefabFolder + "VRSL-DMX-Static-ParLight-H-13CH.prefab");

        public static readonly AlpsVrslPrefab Blinder = new AlpsVrslPrefab(
            "Blinder", "e9dde3e86ccb8ca4bb4ecbe35a6fa7b1", PrefabFolder + "VRSL-DMX-Static-Blinder-H-13CH.prefab");

        public static readonly AlpsVrslPrefab LightBar = new AlpsVrslPrefab(
            "Light Bar", "96ffbd2a722ae324e892d303e2ee9a2a", PrefabFolder + "VRSL-DMX-Static-LightBar-H-13CH.prefab");

        public static readonly AlpsVrslPrefab MultiLightBar = new AlpsVrslPrefab(
            "Multi Light Bar", "c19e8fd46b4abdf49bb7b6cdc62acdde", PrefabFolder + "VRSL-DMX-Static-MultiLightBar-H-15CH.prefab");

        public static readonly AlpsVrslPrefab Laser = new AlpsVrslPrefab(
            "Laser", "3d6c0b40980bcd34aba9183a62ecbd21", PrefabFolder + "VRSL-DMX-Static-Laser-H-13CH.prefab");

        /// <summary>Every fixture the menu offers, in menu order.</summary>
        public static IReadOnlyList<AlpsVrslPrefab> Fixtures { get; } = new[]
        {
            MoverSpotlight, MoverWashLight, ParLight, Blinder, LightBar, MultiLightBar, Laser,
        };

        /// <summary>An object with a container, ready for fixtures to be added under it.</summary>
        public static GameObject CreateContainer(GameObject context)
        {
            var gameObject = new GameObject(ContainerName);
            gameObject.AddComponent<AlpsContainer>();
            Place(gameObject, context);
            return gameObject;
        }

        /// <summary>
        /// An instance of the fixture's VRSL prefab with an ALPS VRSL Fixture on it. Null when
        /// the VR Stage Lighting package does not have the prefab.
        /// </summary>
        public static GameObject CreateFixture(AlpsVrslPrefab fixture, GameObject context)
        {
            var prefab = fixture.Load();
            if (prefab == null)
            {
                Debug.LogError($"[ALPS] The VR Stage Lighting prefab for {fixture.Name} was not found at {fixture.Path}.");
                return null;
            }

            var gameObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            gameObject.AddComponent<AlpsVRSLFixture>();
            Place(gameObject, context);
            return gameObject;
        }

        /// <summary>
        /// Puts a new object where the menu was opened, the way Unity's create items do, and
        /// registers it for undo. A container above it lays it out when the creation is
        /// published.
        /// </summary>
        private static void Place(GameObject gameObject, GameObject context)
        {
            var parent = context;
            var after = -1;
            if (context != null && context.GetComponent<AlpsFixture>() != null)
            {
                parent = context.transform.parent != null ? context.transform.parent.gameObject : null;
                after = context.transform.GetSiblingIndex();
            }

            ObjectFactory.PlaceGameObject(gameObject, parent);
            if (after >= 0)
            {
                gameObject.transform.SetSiblingIndex(after + 1);
            }

            GameObjectUtility.EnsureUniqueNameForSibling(gameObject);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + gameObject.name);
        }

        private static void Select(GameObject gameObject)
        {
            if (gameObject != null)
            {
                Selection.activeGameObject = gameObject;
            }
        }

        // --- Menu items -----------------------------------------------------------------------

        [MenuItem(Root + "Container", false, ContainerPriority)]
        private static void CreateContainerItem(MenuCommand command) => Select(CreateContainer(command.context as GameObject));

        [MenuItem(Root + "Mover Spotlight", false, FixturePriority)]
        private static void CreateMoverSpotlight(MenuCommand command) => Select(CreateFixture(MoverSpotlight, command.context as GameObject));

        [MenuItem(Root + "Mover Spotlight", true)]
        private static bool CanCreateMoverSpotlight() => MoverSpotlight.Load() != null;

        [MenuItem(Root + "Mover Wash Light", false, FixturePriority + 1)]
        private static void CreateMoverWashLight(MenuCommand command) => Select(CreateFixture(MoverWashLight, command.context as GameObject));

        [MenuItem(Root + "Mover Wash Light", true)]
        private static bool CanCreateMoverWashLight() => MoverWashLight.Load() != null;

        [MenuItem(Root + "Par Light", false, FixturePriority + 2)]
        private static void CreateParLight(MenuCommand command) => Select(CreateFixture(ParLight, command.context as GameObject));

        [MenuItem(Root + "Par Light", true)]
        private static bool CanCreateParLight() => ParLight.Load() != null;

        [MenuItem(Root + "Blinder", false, FixturePriority + 3)]
        private static void CreateBlinder(MenuCommand command) => Select(CreateFixture(Blinder, command.context as GameObject));

        [MenuItem(Root + "Blinder", true)]
        private static bool CanCreateBlinder() => Blinder.Load() != null;

        [MenuItem(Root + "Light Bar", false, FixturePriority + 4)]
        private static void CreateLightBar(MenuCommand command) => Select(CreateFixture(LightBar, command.context as GameObject));

        [MenuItem(Root + "Light Bar", true)]
        private static bool CanCreateLightBar() => LightBar.Load() != null;

        [MenuItem(Root + "Multi Light Bar", false, FixturePriority + 5)]
        private static void CreateMultiLightBar(MenuCommand command) => Select(CreateFixture(MultiLightBar, command.context as GameObject));

        [MenuItem(Root + "Multi Light Bar", true)]
        private static bool CanCreateMultiLightBar() => MultiLightBar.Load() != null;

        [MenuItem(Root + "Laser", false, FixturePriority + 6)]
        private static void CreateLaser(MenuCommand command) => Select(CreateFixture(Laser, command.context as GameObject));

        [MenuItem(Root + "Laser", true)]
        private static bool CanCreateLaser() => Laser.Load() != null;
    }

    /// <summary>
    /// A VR Stage Lighting DMX Static prefab the create menu offers. It is found by GUID, so
    /// it still resolves where VRSL sits in Assets rather than Packages, and by path after that.
    /// </summary>
    public sealed class AlpsVrslPrefab
    {
        public AlpsVrslPrefab(string name, string guid, string path)
        {
            Name = name;
            Guid = guid;
            Path = path;
        }

        public string Name { get; }

        public string Guid { get; }

        public string Path { get; }

        public GameObject Load()
        {
            var byGuid = AssetDatabase.GUIDToAssetPath(Guid);
            var prefab = string.IsNullOrEmpty(byGuid) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(byGuid);
            return prefab != null ? prefab : AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        }
    }
}
