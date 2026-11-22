using System.Collections.Generic;
using AdzukiSoft.ALPS.Vrsl;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Makes meshes of the world's own glow as a fixture, in the colour and at the brightness
    /// a show gives it: a sign, a strip along the edge of a stage, the panels of a wall.
    ///
    /// Such a mesh is drawn with the one surface material ALPS comes with (VRSL's surface
    /// shader, the shader of the lit faces, reading a fixture's row), and its object gets
    /// VRSL's DMX Static script, which tells the material which row to read, and an ALPS VRSL
    /// Fixture, so a show drives it the way it drives a light bar. Every mesh shares that
    /// material, so nothing is made in the project.
    ///
    /// An object becomes one fixture, which lights its own mesh and the meshes below it. The
    /// meshes of another fixture or container below it are left to that one.
    ///
    /// The Hierarchy's ALPS menu does it to the selection. It also makes a cube that glows,
    /// as the container's fixture list does: something to see a show on at once, and a
    /// placeholder to swap the mesh of.
    /// </summary>
    public static class AlpsEmissiveMesh
    {
        public const string ObjectName = "ALPS Emissive Mesh";
        public const string MaterialGuid = "3e8705bf35e810c2351434d281ebaff1";
        public const string MaterialPath = "Packages/me.adzuki.live-performance-system/Runtime/VRSL/Materials/Lights/DMX/VRSL-Horizontal-ExampleSurfaceShader-Cube.mat";

        private const string Root = "GameObject/ALPS/";
        private const string UndoName = "Make Emissive Mesh";

        // After the fixtures of the create menu, and low enough to show in the Hierarchy's
        // context menu too.
        private const int CreatePriority = 45;
        private const int ConvertPriority = 46;

        /// <summary>
        /// The material every emissive mesh is drawn with. It is found by GUID, so it still
        /// resolves where the package sits in Assets, and by path after that.
        /// </summary>
        public static Material LoadMaterial()
        {
            var byGuid = AssetDatabase.GUIDToAssetPath(MaterialGuid);
            var material = string.IsNullOrEmpty(byGuid) ? null : AssetDatabase.LoadAssetAtPath<Material>(byGuid);
            return material != null ? material : AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }

        /// <summary>
        /// The meshes <paramref name="root"/> lights as a fixture: its own and those below
        /// it, in hierarchy order, without the ones below another fixture, container or VRSL
        /// script on the way down.
        /// </summary>
        public static List<MeshRenderer> Renderers(GameObject root)
        {
            var renderers = new List<MeshRenderer>();
            if (root != null)
            {
                Collect(root.transform, renderers, true);
            }

            return renderers;
        }

        private static void Collect(Transform transform, List<MeshRenderer> into, bool isRoot)
        {
            if (!isRoot && IsDriven(transform))
            {
                return;
            }

            var renderer = transform.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                into.Add(renderer);
            }

            foreach (Transform child in transform)
            {
                Collect(child, into, false);
            }
        }

        private static bool IsDriven(Transform transform)
        {
            return transform.GetComponent<AlpsTarget>() != null || transform.GetComponent<VRStageLighting_DMX_Static>() != null;
        }

        /// <summary>
        /// True when <paramref name="root"/> can become an emissive mesh: it has a mesh on it
        /// or below it, is not driven already, and is no part of a fixture. Inside a
        /// container is fine, which is where a fixture belongs.
        /// </summary>
        public static bool CanConvert(GameObject root)
        {
            if (root == null || IsDriven(root.transform) || LoadMaterial() == null)
            {
                return false;
            }

            for (var parent = root.transform.parent; parent != null; parent = parent.parent)
            {
                var target = parent.GetComponent<AlpsTarget>();
                if ((target != null && !(target is AlpsContainer)) || parent.GetComponent<VRStageLighting_DMX_Static>() != null)
                {
                    return false;
                }
            }

            return Renderers(root).Count > 0;
        }

        /// <summary>
        /// The objects of <paramref name="selection"/> that become a fixture each: those that
        /// can, without the ones below another of them, whose meshes that one lights.
        /// </summary>
        public static List<GameObject> Roots(GameObject[] selection)
        {
            var convertible = new List<GameObject>();
            foreach (var gameObject in selection)
            {
                if (CanConvert(gameObject))
                {
                    convertible.Add(gameObject);
                }
            }

            var roots = new List<GameObject>();
            foreach (var gameObject in convertible)
            {
                var nested = false;
                foreach (var other in convertible)
                {
                    nested |= other != gameObject && gameObject.transform.IsChildOf(other.transform);
                }

                if (!nested)
                {
                    roots.Add(gameObject);
                }
            }

            return roots;
        }

        /// <summary>
        /// Makes <paramref name="root"/> a fixture that lights its meshes: every material
        /// slot of each takes the emissive material, and the object gets VRSL's script with
        /// those meshes and an ALPS VRSL Fixture that drives it. One undo step takes it all
        /// back. Returns null when <see cref="CanConvert"/> says it cannot.
        /// </summary>
        public static AlpsVRSLFixture Convert(GameObject root)
        {
            if (!CanConvert(root))
            {
                return null;
            }

            var material = LoadMaterial();
            var renderers = Renderers(root);
            Undo.SetCurrentGroupName(UndoName);
            foreach (var renderer in renderers)
            {
                Undo.RecordObject(renderer, UndoName);
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (var i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials;
            }

            // The script does nothing without a first mesh, which CanConvert made sure of.
            var light = UdonSharpUndo.AddComponent<VRStageLighting_DMX_Static>(root);
            light.objRenderers = renderers.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(light);

            var fixture = Undo.AddComponent<AlpsVRSLFixture>(root);
            fixture.target = light;
            return fixture;
        }

        /// <summary>
        /// A cube that glows, placed where the menu was opened like the fixtures of the
        /// create menu: something to see a show on at once, and to swap the mesh of. Null
        /// when the material is missing.
        /// </summary>
        public static GameObject Create(GameObject context)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = ObjectName;
            if (Convert(gameObject) == null)
            {
                Object.DestroyImmediate(gameObject);
                Debug.LogError($"[ALPS] The material for an emissive mesh was not found at {MaterialPath}.");
                return null;
            }

            AlpsCreateMenu.Place(gameObject, context);
            return gameObject;
        }

        // --- Menu items -----------------------------------------------------------------------

        [MenuItem(Root + "Emissive Mesh", false, CreatePriority)]
        private static void CreateItem(MenuCommand command)
        {
            var created = Create(command.context as GameObject);
            if (created != null)
            {
                Selection.activeGameObject = created;
            }
        }

        [MenuItem(Root + "Emissive Mesh", true)]
        private static bool CanCreate() => LoadMaterial() != null;

        [MenuItem(Root + "Make Selection Emissive", false, ConvertPriority)]
        private static void ConvertItem(MenuCommand command)
        {
            // Unity runs the item once for every selected object. The run for the first one
            // converts the whole selection, so it is asked about once and undone in one step.
            var selection = Selection.gameObjects;
            if (command.context != null && selection.Length > 1 && command.context != selection[0])
            {
                return;
            }

            var roots = Roots(selection);
            var meshes = 0;
            var below = false;
            foreach (var root in roots)
            {
                var renderers = Renderers(root);
                meshes += renderers.Count;
                below |= renderers.Count > 1 || renderers[0].gameObject != root;
            }

            // Replacing the materials of what was not selected itself is worth a question:
            // picked on the root of a venue, this would take every mesh in it.
            if (below && !EditorUtility.DisplayDialog(
                    AlpsStrings.Tr("emissiveMesh.confirm.title"),
                    string.Format(AlpsStrings.Tr("emissiveMesh.confirm.message"), meshes),
                    AlpsStrings.Tr("emissiveMesh.confirm.ok"),
                    AlpsStrings.Tr("emissiveMesh.confirm.cancel")))
            {
                return;
            }

            foreach (var root in roots)
            {
                Convert(root);
            }
        }

        [MenuItem(Root + "Make Selection Emissive", true)]
        private static bool CanConvertSelection() => Roots(Selection.gameObjects).Count > 0;
    }
}
