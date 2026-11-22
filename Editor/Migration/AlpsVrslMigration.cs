using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using AdzukiSoft.ALPS.Vrsl;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>What a fixture of the VR Stage Lighting package is before it is converted.</summary>
    public enum AlpsVrslMigrationSource
    {
        /// <summary>An ALPS fixture an older ALPS made from the package's DMX prefab.</summary>
        AlpsFixture,
        /// <summary>A DMX fixture of the package that ALPS does not drive yet.</summary>
        DmxFixture,
        /// <summary>An AudioLink fixture of the package, which no DMX show can drive.</summary>
        AudioLinkFixture,
    }

    /// <summary>One fixture <see cref="AlpsVrslMigration"/> can convert.</summary>
    public sealed class AlpsVrslMigrationItem
    {
        public GameObject Root;

        /// <summary>The fixture it becomes, null where it becomes an emissive mesh.</summary>
        public AlpsVrslPrefab Target;
        public AlpsVrslMigrationSource Source;

        /// <summary>
        /// The package prefab the instance is made from, or for an object that is no prefab
        /// instance the one whose meshes it has.
        /// </summary>
        public GameObject SourcePrefab;

        /// <summary>
        /// True for an object that is no prefab instance and carries the package's fixture
        /// script: an unpacked fixture, or one put together by hand.
        /// </summary>
        public bool Loose;

        /// <summary>
        /// True for a loose fixture with meshes of the world's own, which stays and glows as
        /// an emissive mesh (<see cref="AlpsEmissiveMesh"/>).
        /// </summary>
        public bool Emissive;

        /// <summary>The <see cref="AlpsStrings"/> key of what it becomes.</summary>
        public string LabelKey => Emissive ? "addFixture.emissiveMesh" : Target.LabelKey;

        /// <summary>
        /// True when the instance only changes the prefab it is made from, which keeps the
        /// object and everything on it. Otherwise a new instance takes its place.
        /// </summary>
        public bool KeepsObject;
    }

    /// <summary>
    /// Moves fixtures made from the VR Stage Lighting package over to the fixtures ALPS comes
    /// with (<c>Runtime/VRSL</c>), for shows made with an older ALPS and for the placement of
    /// AudioLink fixtures another system used.
    ///
    /// The package is found by the names of its prefabs and scripts, never by type, so this
    /// compiles and runs with any version of it, and without it.
    ///
    /// The fixtures ALPS comes with are copies whose GUID is derived from the original's
    /// (<c>scripts/vendor-vrsl.py</c>). An instance of the very prefab a copy was made from
    /// has its prefab swapped, so the object, its overrides, the ALPS fixture on it and
    /// every reference to them stay. Anything else, an AudioLink fixture or another mode or
    /// channel count of the same light, is replaced by a new instance in its place.
    ///
    /// A fixture that is no prefab instance, unpacked or put together by hand, has no prefab
    /// to tell what it is. It is found by the package's fixture script on it and replaced by
    /// the fixture whose meshes it has. With meshes of the world's own it keeps its object
    /// and becomes an emissive mesh.
    /// </summary>
    public static class AlpsVrslMigration
    {
        private const string GuidSalt = "alps-vrsl:";
        private const string DmxPrefix = "VRSL-DMX-";
        private const string AudioLinkPrefix = "VRSL-AudioLink-";
        private const string PackageNamespace = "VRSL.";
        private const string PackageShaders = "VRSL/";

        /// <summary>Parts of the package's prefab names, in the order they are tried.</summary>
        private static readonly KeyValuePair<string, AlpsVrslPrefab>[] Names =
        {
            new KeyValuePair<string, AlpsVrslPrefab>("mover-spotlight", AlpsCreateMenu.MoverSpotlight),
            new KeyValuePair<string, AlpsVrslPrefab>("mover-washlight", AlpsCreateMenu.MoverWashLight),
            new KeyValuePair<string, AlpsVrslPrefab>("parlight", AlpsCreateMenu.ParLight),
            new KeyValuePair<string, AlpsVrslPrefab>("blinder", AlpsCreateMenu.Blinder),
            new KeyValuePair<string, AlpsVrslPrefab>("multilightbar", AlpsCreateMenu.MultiLightBar),
            new KeyValuePair<string, AlpsVrslPrefab>("lightbar", AlpsCreateMenu.LightBar),
            new KeyValuePair<string, AlpsVrslPrefab>("laser", AlpsCreateMenu.Laser),
            new KeyValuePair<string, AlpsVrslPrefab>("flasher", AlpsCreateMenu.Flasher),
            new KeyValuePair<string, AlpsVrslPrefab>("strobelight", AlpsCreateMenu.StrobePanel),
            new KeyValuePair<string, AlpsVrslPrefab>("discoball", AlpsCreateMenu.MirrorBall),
        };

        /// <summary>The GUID of the copy ALPS comes with of the package asset with <paramref name="guid"/>.</summary>
        public static string CopyGuid(string guid)
        {
            using (var md5 = MD5.Create())
            {
                var text = new StringBuilder(32);
                foreach (var b in md5.ComputeHash(Encoding.UTF8.GetBytes(GuidSalt + guid)))
                {
                    text.Append(b.ToString("x2"));
                }

                return text.ToString();
            }
        }

        /// <summary>
        /// Every fixture of the package in the prefab that is open, or else in the loaded
        /// scenes, in hierarchy order.
        /// </summary>
        public static List<AlpsVrslMigrationItem> Find()
        {
            // The project may have changed since the last look.
            _packagePrefabs = null;
            var items = new List<AlpsVrslMigrationItem>();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                Collect(stage.prefabContentsRoot, items);
                return items;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    Collect(root, items);
                }
            }

            return items;
        }

        private static void Collect(GameObject root, List<AlpsVrslMigrationItem> items)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var item = Describe(transform.gameObject);
                if (item != null)
                {
                    items.Add(item);
                }
            }
        }

        /// <summary>What converting <paramref name="gameObject"/> would do, or null when it is not a fixture of the package.</summary>
        public static AlpsVrslMigrationItem Describe(GameObject gameObject)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                return DescribeLoose(gameObject);
            }

            // An instance inside another prefab's instance belongs to that prefab, where it
            // is converted with the prefab open.
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject))
            {
                return null;
            }

            var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(gameObject);
            if (original == null)
            {
                return null;
            }

            var name = original.name;
            var dmx = name.StartsWith(DmxPrefix, StringComparison.OrdinalIgnoreCase);
            if (!dmx && !name.StartsWith(AudioLinkPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var target = Match(name);
            var prefab = target != null ? target.Load() : null;
            if (prefab == null || prefab == original)
            {
                return null;
            }

            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));
            return new AlpsVrslMigrationItem
            {
                Root = gameObject,
                Target = target,
                SourcePrefab = original,
                Source = gameObject.GetComponent<AlpsFixture>() != null
                    ? AlpsVrslMigrationSource.AlpsFixture
                    : dmx ? AlpsVrslMigrationSource.DmxFixture : AlpsVrslMigrationSource.AudioLinkFixture,
                // A variant keeps its own prefab, so only a direct instance can swap.
                KeepsObject = PrefabUtility.GetCorrespondingObjectFromSource(gameObject) == original
                    && CopyGuid(guid) == target.Guid,
            };
        }

        /// <summary>
        /// What converting an object that is no prefab instance would do: nothing without
        /// the package's fixture script on it. It becomes the fixture whose meshes it has,
        /// or with none of them an emissive mesh, where it has a mesh at all and ALPS does
        /// not drive it yet.
        /// </summary>
        private static AlpsVrslMigrationItem DescribeLoose(GameObject gameObject)
        {
            var script = PackageFixtureOn(gameObject);
            if (script == null)
            {
                return null;
            }

            var audioLink = script.GetType().Name.IndexOf("AudioLink", StringComparison.OrdinalIgnoreCase) >= 0;
            var item = new AlpsVrslMigrationItem
            {
                Root = gameObject,
                Loose = true,
                Source = gameObject.GetComponent<AlpsFixture>() != null
                    ? AlpsVrslMigrationSource.AlpsFixture
                    : audioLink ? AlpsVrslMigrationSource.AudioLinkFixture : AlpsVrslMigrationSource.DmxFixture,
            };

            var original = MatchByMeshes(gameObject, audioLink);
            if (original != null)
            {
                item.SourcePrefab = original.Prefab;
                item.Target = original.Target;
                return item;
            }

            if (gameObject.GetComponent<AlpsTarget>() != null || !AlpsEmissiveMesh.CanConvert(gameObject))
            {
                return null;
            }

            item.Emissive = true;
            return item;
        }

        /// <summary>A fixture prefab of the package, with what tells an object made from it.</summary>
        private sealed class PackagePrefab
        {
            public GameObject Prefab;
            public AlpsVrslPrefab Target;
            public bool AudioLink;
            public readonly HashSet<Mesh> Meshes = new HashSet<Mesh>();
            public readonly HashSet<Material> Materials = new HashSet<Material>();
        }

        private static List<PackagePrefab> _packagePrefabs;

        /// <summary>
        /// The package's fixture prefabs ALPS has a fixture for, in the order of their paths.
        /// The copies ALPS comes with have the same names and are left out by their script.
        /// </summary>
        private static List<PackagePrefab> PackagePrefabs()
        {
            if (_packagePrefabs != null)
            {
                return _packagePrefabs;
            }

            _packagePrefabs = new List<PackagePrefab>();
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab VRSL"))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            paths.Sort(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var audioLink = name.StartsWith(AudioLinkPrefix, StringComparison.OrdinalIgnoreCase);
                var target = audioLink || name.StartsWith(DmxPrefix, StringComparison.OrdinalIgnoreCase) ? Match(name) : null;
                if (target == null || target.Load() == null)
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || FindPackageFixture(prefab) == null)
                {
                    continue;
                }

                var found = new PackagePrefab { Prefab = prefab, Target = target, AudioLink = audioLink };
                Signature(prefab, found.Meshes, found.Materials);
                if (found.Meshes.Count > 0)
                {
                    _packagePrefabs.Add(found);
                }
            }

            return _packagePrefabs;
        }

        /// <summary>
        /// The package prefab that shares the most meshes with <paramref name="gameObject"/>,
        /// null when none shares one. Prefabs with the same meshes (the DMX and the AudioLink
        /// one of a light, and its modes) are told by the kind of script and then by a shared
        /// material.
        /// </summary>
        private static PackagePrefab MatchByMeshes(GameObject gameObject, bool audioLink)
        {
            var meshes = new HashSet<Mesh>();
            var materials = new HashSet<Material>();
            Signature(gameObject, meshes, materials);
            if (meshes.Count == 0)
            {
                return null;
            }

            PackagePrefab best = null;
            var bestScore = 0;
            foreach (var candidate in PackagePrefabs())
            {
                var shared = 0;
                foreach (var mesh in candidate.Meshes)
                {
                    shared += meshes.Contains(mesh) ? 1 : 0;
                }

                if (shared == 0)
                {
                    continue;
                }

                var score = shared * 4 + (candidate.AudioLink == audioLink ? 2 : 0) + (candidate.Materials.Overlaps(materials) ? 1 : 0);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static void Signature(GameObject root, HashSet<Mesh> meshes, HashSet<Material> materials)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (IsProjectMesh(filter.sharedMesh))
                {
                    meshes.Add(filter.sharedMesh);
                }
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        materials.Add(material);
                    }
                }
            }
        }

        /// <summary>
        /// True for a mesh of the project. Unity's own cube and sphere, which the package's
        /// mirror ball has too, say nothing about what an object is.
        /// </summary>
        private static bool IsProjectMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(mesh);
            return path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal);
        }

        private static AlpsVrslPrefab Match(string prefabName)
        {
            var lower = prefabName.ToLowerInvariant();
            foreach (var pair in Names)
            {
                if (lower.Contains(pair.Key))
                {
                    return pair.Value;
                }
            }

            return null;
        }

        /// <summary>Converts the fixtures as one undo step and returns the fixtures they became.</summary>
        public static List<GameObject> Convert(IEnumerable<AlpsVrslMigrationItem> items)
        {
            var converted = new List<GameObject>();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Migrate VRSL Fixtures");
            var group = Undo.GetCurrentGroup();
            foreach (var item in items)
            {
                if (item.Root == null)
                {
                    continue;
                }

                if (item.Emissive)
                {
                    if (MakeEmissive(item.Root))
                    {
                        converted.Add(item.Root);
                    }

                    continue;
                }

                var prefab = item.Target.Load();
                if (prefab == null)
                {
                    continue;
                }

                var root = item.KeepsObject ? Swap(item, prefab) : Replace(item, prefab);
                if (item.Target.Segmented)
                {
                    UseSegments(root);
                }

                converted.Add(root);
            }

            Undo.SetCurrentGroupName("Migrate VRSL Fixtures");
            Undo.CollapseUndoOperations(group);
            AlpsPreviewDriver.MarkDirty();
            return converted;
        }

        /// <summary>
        /// Takes the package's script off a fixture made of the world's own meshes and makes
        /// the object an emissive mesh, which draws them with the material ALPS comes with.
        /// </summary>
        private static bool MakeEmissive(GameObject root)
        {
            foreach (var behaviour in root.GetComponents<MonoBehaviour>())
            {
                if (!IsPackageFixture(behaviour))
                {
                    continue;
                }

                // An UdonSharp script goes with the Udon behaviour that runs it.
                if (behaviour is UdonSharpBehaviour udonSharp)
                {
                    UdonSharpUndo.DestroyImmediate(udonSharp);
                }
                else
                {
                    Undo.DestroyObjectImmediate(behaviour);
                }
            }

            return AlpsEmissiveMesh.Convert(root) != null;
        }

        /// <summary>Points the instance at the copy of its prefab, keeping the object.</summary>
        private static GameObject Swap(AlpsVrslMigrationItem item, GameObject prefab)
        {
            var root = item.Root;
            var values = CaptureValues(FindPackageFixture(root));
            var settings = new PrefabReplacingSettings
            {
                changeRootNameToAssetName = false,
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                prefabOverridesOptions = PrefabOverridesOptions.KeepAllPossibleOverrides,
            };
            PrefabUtility.ReplacePrefabAssetOfPrefabInstance(root, prefab, settings, InteractionMode.UserAction);

            RemovePackageOverrides(root, prefab);
            RevertPackageMaterials(root);
            var fresh = root.GetComponentInChildren<VRStageLighting_DMX_Static>(true);
            ApplyValues(fresh, values);
            var fixture = root.GetComponent<AlpsVRSLFixture>();
            if (fixture != null)
            {
                Undo.RecordObject(fixture, "Migrate VRSL Fixtures");
                fixture.target = fresh;
                EditorUtility.SetDirty(fixture);
            }

            return root;
        }

        /// <summary>
        /// Drops the overrides the swap could not hand over, which still name objects of the
        /// package's prefab: the ones on its fixture script, whose values
        /// <see cref="ApplyValues"/> sets on the new script instead. Left in, they would keep
        /// the scene referring to the package.
        /// </summary>
        private static void RemovePackageOverrides(GameObject root, GameObject prefab)
        {
            var modifications = PrefabUtility.GetPropertyModifications(root);
            if (modifications == null)
            {
                return;
            }

            var path = AssetDatabase.GetAssetPath(prefab);
            var kept = new List<PropertyModification>();
            foreach (var modification in modifications)
            {
                if (modification.target != null && AssetDatabase.GetAssetPath(modification.target) == path)
                {
                    kept.Add(modification);
                }
            }

            if (kept.Count != modifications.Length)
            {
                PrefabUtility.SetPropertyModifications(root, kept.ToArray());
            }
        }

        /// <summary>
        /// Gives the renderers the materials of the prefab back where an override holds one
        /// drawn with a shader of the package, which reads the grid the package's way, or
        /// one that is missing.
        /// </summary>
        private static void RevertPackageMaterials(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var held = false;
                foreach (var material in renderer.sharedMaterials)
                {
                    var shader = material != null && material.shader != null ? material.shader.name : "";
                    held |= material == null || shader.StartsWith(PackageShaders, StringComparison.Ordinal);
                }

                if (held)
                {
                    var materials = new SerializedObject(renderer).FindProperty("m_Materials");
                    PrefabUtility.RevertPropertyOverride(materials, InteractionMode.UserAction);
                }
            }
        }

        /// <summary>
        /// Puts a new instance where the fixture is and removes the fixture. The two prefabs
        /// rest in different rotations and scales, so the instance takes the fixture's pose
        /// relative to the prefab it came from.
        /// </summary>
        private static GameObject Replace(AlpsVrslMigrationItem item, GameObject prefab)
        {
            var old = item.Root;
            var from = old.transform;
            var parent = from.parent;
            var instance = parent != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)
                : (GameObject)PrefabUtility.InstantiatePrefab(prefab, old.scene);
            Undo.RegisterCreatedObjectUndo(instance, "Migrate VRSL Fixtures");

            var source = item.SourcePrefab.transform;
            var target = prefab.transform;
            var to = instance.transform;
            to.localPosition = from.localPosition;
            var rotation = item.Loose ? RestRotation(from, source) : from.localRotation;
            to.localRotation = rotation * Quaternion.Inverse(source.localRotation) * target.localRotation;
            to.localScale = Vector3.Scale(Divide(from.localScale, source.localScale), target.localScale);
            to.SetSiblingIndex(from.GetSiblingIndex());
            instance.name = old.name;
            instance.layer = old.layer;
            instance.SetActive(old.activeSelf);

            ApplyValues(instance.GetComponentInChildren<VRStageLighting_DMX_Static>(true), CaptureValues(FindPackageFixture(old)));
            var fixture = Undo.AddComponent<AlpsVRSLFixture>(instance);
            var oldFixture = old.GetComponent<AlpsFixture>();
            if (oldFixture != null)
            {
                Rebind(oldFixture, fixture);
            }

            Undo.DestroyObjectImmediate(old);
            return instance;
        }

        /// <summary>
        /// The rotation the package's prefab would have with its meshes turned the way the
        /// loose fixture's are. A fixture put together by hand may hold its meshes turned
        /// another way below its root than the prefab does, so the first mesh of the prefab
        /// the fixture has too is what the two are lined up by.
        /// </summary>
        private static Quaternion RestRotation(Transform from, Transform source)
        {
            var held = from.GetComponentsInChildren<MeshFilter>(true);
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!IsProjectMesh(filter.sharedMesh))
                {
                    continue;
                }

                foreach (var own in held)
                {
                    if (own.sharedMesh != filter.sharedMesh)
                    {
                        continue;
                    }

                    var inFixture = Quaternion.Inverse(from.rotation) * own.transform.rotation;
                    var inPrefab = Quaternion.Inverse(source.rotation) * filter.transform.rotation;
                    return from.localRotation * inFixture * Quaternion.Inverse(inPrefab);
                }
            }

            return from.localRotation;
        }

        private static Vector3 Divide(Vector3 value, Vector3 by)
        {
            return new Vector3(
                Mathf.Approximately(by.x, 0f) ? value.x : value.x / by.x,
                Mathf.Approximately(by.y, 0f) ? value.y : value.y / by.y,
                Mathf.Approximately(by.z, 0f) ? value.z : value.z / by.z);
        }

        /// <summary>Hands the tracks bound to a fixture that is going away to the one taking its place.</summary>
        /// <summary>
        /// A multi light bar is driven segment by segment, so the single fixture an older
        /// version put on it gives way to the bar and its segments. Tracks bound to that
        /// fixture are bound to the bar, and drive its twelve segments from then on.
        /// </summary>
        private static void UseSegments(GameObject root)
        {
            var bar = root.GetComponent<AlpsVRSLMultiLightBar>();
            if (bar == null)
            {
                bar = Undo.AddComponent<AlpsVRSLMultiLightBar>(root);
            }

            bar.target = root.GetComponentInChildren<VRStageLighting_DMX_Static>(true);
            AlpsMultiLightBarSegments.Sync(bar, recordUndo: true);
            var fixture = root.GetComponent<AlpsVRSLFixture>();
            if (fixture != null)
            {
                Rebind(fixture, bar);
                Undo.DestroyObjectImmediate(fixture);
            }

            EditorUtility.SetDirty(bar);
        }

        private static void Rebind(AlpsTarget from, AlpsTarget to)
        {
            foreach (var director in UnityEngine.Object.FindObjectsOfType<PlayableDirector>(true))
            {
                var timeline = director.playableAsset as TimelineAsset;
                if (timeline == null)
                {
                    continue;
                }

                foreach (var track in timeline.GetOutputTracks())
                {
                    if (director.GetGenericBinding(track) == from)
                    {
                        Undo.RecordObject(director, "Migrate VRSL Fixtures");
                        director.SetGenericBinding(track, to);
                        EditorUtility.SetDirty(director);
                    }
                }
            }
        }

        /// <summary>The package's own fixture script on the instance, null where the package is gone.</summary>
        private static MonoBehaviour FindPackageFixture(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (IsPackageFixture(behaviour))
                {
                    return behaviour;
                }
            }

            return null;
        }

        /// <summary>The package's fixture script on the object itself.</summary>
        private static MonoBehaviour PackageFixtureOn(GameObject gameObject)
        {
            foreach (var behaviour in gameObject.GetComponents<MonoBehaviour>())
            {
                if (IsPackageFixture(behaviour))
                {
                    return behaviour;
                }
            }

            return null;
        }

        private static bool IsPackageFixture(MonoBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return false;
            }

            var type = behaviour.GetType().FullName;
            return type != null && type.StartsWith(PackageNamespace + "VRStageLighting_", StringComparison.Ordinal);
        }

        /// <summary>
        /// The plain values of the package's fixture script, each as a setter for a field of
        /// the same name and kind. The look of a fixture (tint, cone, intensity, base pan and
        /// tilt) has the same names on the DMX and the AudioLink script, so it carries over,
        /// and what only one of them has is left behind.
        /// </summary>
        private static List<Action<SerializedObject>> CaptureValues(MonoBehaviour behaviour)
        {
            var values = new List<Action<SerializedObject>>();
            if (behaviour == null)
            {
                return values;
            }

            var property = new SerializedObject(behaviour).GetIterator();
            for (var enter = true; property.Next(enter); enter = false)
            {
                var path = property.propertyPath;
                if (path.StartsWith("m_", StringComparison.Ordinal))
                {
                    continue;
                }

                switch (property.propertyType)
                {
                    case SerializedPropertyType.Float:
                        var number = property.floatValue;
                        values.Add(target => Set(target, path, SerializedPropertyType.Float, p => p.floatValue = number));
                        break;
                    case SerializedPropertyType.Integer:
                        var whole = property.intValue;
                        values.Add(target => Set(target, path, SerializedPropertyType.Integer, p => p.intValue = whole));
                        break;
                    case SerializedPropertyType.Boolean:
                        var flag = property.boolValue;
                        values.Add(target => Set(target, path, SerializedPropertyType.Boolean, p => p.boolValue = flag));
                        break;
                    case SerializedPropertyType.Color:
                        var color = property.colorValue;
                        values.Add(target => Set(target, path, SerializedPropertyType.Color, p => p.colorValue = color));
                        break;
                }
            }

            return values;
        }

        private static void Set(SerializedObject target, string path, SerializedPropertyType type, Action<SerializedProperty> set)
        {
            var property = target.FindProperty(path);
            if (property != null && property.propertyType == type)
            {
                set(property);
            }
        }

        private static void ApplyValues(VRStageLighting_DMX_Static fixture, List<Action<SerializedObject>> values)
        {
            if (fixture == null || values.Count == 0)
            {
                return;
            }

            var target = new SerializedObject(fixture);
            foreach (var value in values)
            {
                value(target);
            }

            target.ApplyModifiedProperties();
        }
    }
}
