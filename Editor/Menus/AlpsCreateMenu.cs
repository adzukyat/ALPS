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
        public const string PenlightAreaName = "ALPS Penlight Area";

        private const string Root = "GameObject/ALPS/";

        // Up to 49 the items also show in the Hierarchy's context menu. The gap between the
        // two groups draws a separator.
        private const int ContainerPriority = 10;
        private const int PenlightAreaPriority = 11;
        private const int FixturePriority = 30;

        private const string PrefabFolder = "Packages/me.adzuki.live-performance-system/Runtime/VRSL/Prefabs/DMX/Horizontal Mode/";

        // VRSL makes its movers at 1.25, which is a little large beside the other fixtures.
        private const float MoverScale = 1f;

        public static readonly AlpsVrslPrefab MoverSpotlight = new AlpsVrslPrefab(
            "Mover Spotlight", "fixture.moverSpotlight", "a6ed1d9204338028a4e8bf3f8b764097", PrefabFolder + "VRSL-DMX-Mover-Spotlight-H-13CH.prefab", MoverScale);

        public static readonly AlpsVrslPrefab MoverWashLight = new AlpsVrslPrefab(
            "Mover Wash Light", "fixture.moverWashLight", "da85438bbc9c4379fc29b28fc7fe825d", PrefabFolder + "VRSL-DMX-Mover-WashLight-H-13CH.prefab", MoverScale);

        public static readonly AlpsVrslPrefab ParLight = new AlpsVrslPrefab(
            "Par Light", "fixture.parLight", "e04a6d9f5d1bfd384330c455a80f6928", PrefabFolder + "VRSL-DMX-Static-ParLight-H-13CH.prefab");

        public static readonly AlpsVrslPrefab Blinder = new AlpsVrslPrefab(
            "Blinder", "fixture.blinder", "6c33a58100fa3f6fac43ef8ac7564c1c", PrefabFolder + "VRSL-DMX-Static-Blinder-H-13CH.prefab");

        public static readonly AlpsVrslPrefab LightBar = new AlpsVrslPrefab(
            "Light Bar", "fixture.lightBar", "220324829ef5ebbe155cbec7bbba8951", PrefabFolder + "VRSL-DMX-Static-LightBar-H-13CH.prefab");

        public static readonly AlpsVrslPrefab MultiLightBar = new AlpsVrslPrefab(
            "Multi Light Bar", "fixture.multiLightBar", "bf66b05d4625a9ff51c75ec1c75e1675", PrefabFolder + "VRSL-DMX-Static-MultiLightBar-H-15CH.prefab",
            segmented: true);

        public static readonly AlpsVrslPrefab Laser = new AlpsVrslPrefab(
            "Laser", "fixture.laser", "411e679ed1f72e7c2129a3d46442cb22", PrefabFolder + "VRSL-DMX-Static-Laser-H-13CH.prefab");

        public static readonly AlpsVrslPrefab Flasher = new AlpsVrslPrefab(
            "Flasher", "fixture.flasher", "d96f8e7d1bbad9e865fdef616329fbfa", PrefabFolder + "VRSL-DMX-Static-Flasher-H-1CH.prefab");

        public static readonly AlpsVrslPrefab StrobePanel = new AlpsVrslPrefab(
            "Strobe Panel", "fixture.strobePanel", "6759aaefb3ad09258c2a4f61448cc3a8", PrefabFolder + "5-Channel Statics/VRSL-DMX-Static-6x4StrobeLight-H-5CH.prefab");

        public static readonly AlpsVrslPrefab MirrorBall = new AlpsVrslPrefab(
            "Mirror Ball", "fixture.mirrorBall", "762f5dd7e916bed8acb04514d672190b", PrefabFolder + "VRSL-DMX-Static-DiscoBall-H-1CH.prefab");

        /// <summary>Every fixture the menu offers, in menu order.</summary>
        public static IReadOnlyList<AlpsVrslPrefab> Fixtures { get; } = new[]
        {
            MoverSpotlight, MoverWashLight, ParLight, Blinder, LightBar, MultiLightBar, Laser,
            Flasher, StrobePanel, MirrorBall,
        };

        /// <summary>An object with a container, ready for fixtures to be added under it.</summary>
        public static GameObject CreateContainer(GameObject context)
        {
            var gameObject = new GameObject(ContainerName);
            gameObject.AddComponent<AlpsContainer>();
            Place(gameObject, context);
            return gameObject;
        }

        /// <summary>An object with a penlight area and its slots, ready for a lighting track to be bound to.</summary>
        public static GameObject CreatePenlightArea(GameObject context)
        {
            var gameObject = new GameObject(PenlightAreaName);
            var area = gameObject.AddComponent<AlpsPenlightArea>();
            Place(gameObject, context);
            AlpsPenlightSlots.Sync(area, recordUndo: false);
            return gameObject;
        }

        /// <summary>
        /// An instance of the fixture's prefab with an ALPS VRSL Fixture on it, or for a
        /// multi light bar with the bar and its twelve segments, which are the fixtures a
        /// show drives. Null when the prefab is missing.
        /// </summary>
        public static GameObject CreateFixture(AlpsVrslPrefab fixture, GameObject context)
        {
            var prefab = fixture.Load();
            if (prefab == null)
            {
                Debug.LogError($"[ALPS] The prefab for {fixture.Name} was not found at {fixture.Path}.");
                return null;
            }

            var gameObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var bar = fixture.Segmented ? gameObject.AddComponent<AlpsVRSLMultiLightBar>() : null;
            if (bar == null)
            {
                gameObject.AddComponent<AlpsVRSLFixture>();
            }

            if (fixture.Scale > 0f)
            {
                gameObject.transform.localScale = Vector3.one * fixture.Scale;
            }

            Place(gameObject, context);
            AlpsMultiLightBarSegments.Sync(bar, recordUndo: false);
            return gameObject;
        }

        /// <summary>
        /// Puts a new object where the menu was opened, the way Unity's create items do, and
        /// registers it for undo. A container above it lays it out when the creation is
        /// published.
        ///
        /// Unity aligns an object it places under a parent to that parent, which would drop
        /// the rotation and scale the object arrives with. On a VRSL prefab the rotation is
        /// what hangs a mover with its pan axis upright and points a static light, and the
        /// scale is the fixture's size (about 0.05 on the multi light bar, whose mesh is over
        /// 20 m wide without it). Both are put back, so a fixture comes out the same wherever
        /// it is created. A container that arranges its children writes their rotation
        /// afterwards, and the first layout keeps the one they share.
        /// </summary>
        internal static void Place(GameObject gameObject, GameObject context)
        {
            var rotation = gameObject.transform.localRotation;
            var scale = gameObject.transform.localScale;

            var parent = context;
            var after = -1;
            if (context != null && (context.GetComponent<AlpsFixture>() != null || context.GetComponent<AlpsVRSLMultiLightBar>() != null))
            {
                parent = context.transform.parent != null ? context.transform.parent.gameObject : null;
                after = context.transform.GetSiblingIndex();
            }

            ObjectFactory.PlaceGameObject(gameObject, parent);
            gameObject.transform.localRotation = rotation;
            gameObject.transform.localScale = scale;
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

        [MenuItem(Root + "Penlight Area", false, PenlightAreaPriority)]
        private static void CreatePenlightAreaItem(MenuCommand command) => Select(CreatePenlightArea(command.context as GameObject));

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

        [MenuItem(Root + "Flasher", false, FixturePriority + 7)]
        private static void CreateFlasher(MenuCommand command) => Select(CreateFixture(Flasher, command.context as GameObject));

        [MenuItem(Root + "Flasher", true)]
        private static bool CanCreateFlasher() => Flasher.Load() != null;

        [MenuItem(Root + "Strobe Panel", false, FixturePriority + 8)]
        private static void CreateStrobePanel(MenuCommand command) => Select(CreateFixture(StrobePanel, command.context as GameObject));

        [MenuItem(Root + "Strobe Panel", true)]
        private static bool CanCreateStrobePanel() => StrobePanel.Load() != null;

        [MenuItem(Root + "Mirror Ball", false, FixturePriority + 9)]
        private static void CreateMirrorBall(MenuCommand command) => Select(CreateFixture(MirrorBall, command.context as GameObject));

        [MenuItem(Root + "Mirror Ball", true)]
        private static bool CanCreateMirrorBall() => MirrorBall.Load() != null;
    }

    /// <summary>
    /// A VR Stage Lighting DMX Static prefab the create menu offers. It is found by GUID, so
    /// it still resolves where VRSL sits in Assets rather than Packages, and by path after that.
    /// </summary>
    public sealed class AlpsVrslPrefab
    {
        public AlpsVrslPrefab(string name, string labelKey, string guid, string path, float scale = 0f, bool segmented = false)
        {
            Scale = scale;
            Segmented = segmented;
            Name = name;
            LabelKey = labelKey;
            Guid = guid;
            Path = path;
        }

        /// <summary>The English name the Hierarchy's menu shows.</summary>
        public string Name { get; }

        /// <summary>The <see cref="AlpsStrings"/> key of the name the container's fixture list shows.</summary>
        public string LabelKey { get; }

        public string Guid { get; }

        public string Path { get; }

        /// <summary>The scale a new fixture gets, or 0 for the prefab's own.</summary>
        public float Scale { get; }

        /// <summary>True for the multi light bar, whose segments are the fixtures a show drives.</summary>
        public bool Segmented { get; }

        public GameObject Load()
        {
            var byGuid = AssetDatabase.GUIDToAssetPath(Guid);
            var prefab = string.IsNullOrEmpty(byGuid) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(byGuid);
            return prefab != null ? prefab : AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        }
    }
}
