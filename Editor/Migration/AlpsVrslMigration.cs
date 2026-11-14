using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using AdzukiSoft.ALPS.Vrsl;
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

    /// <summary>One prefab instance <see cref="AlpsVrslMigration"/> can convert.</summary>
    public sealed class AlpsVrslMigrationItem
    {
        public GameObject Root;
        public AlpsVrslPrefab Target;
        public AlpsVrslMigrationSource Source;

        /// <summary>The package prefab the instance is made from.</summary>
        public GameObject SourcePrefab;

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

                var prefab = item.Target.Load();
                if (prefab == null)
                {
                    continue;
                }

                converted.Add(item.KeepsObject ? Swap(item, prefab) : Replace(item, prefab));
            }

            Undo.CollapseUndoOperations(group);
            AlpsPreviewDriver.MarkDirty();
            return converted;
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
            to.localRotation = from.localRotation * Quaternion.Inverse(source.localRotation) * target.localRotation;
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

        private static Vector3 Divide(Vector3 value, Vector3 by)
        {
            return new Vector3(
                Mathf.Approximately(by.x, 0f) ? value.x : value.x / by.x,
                Mathf.Approximately(by.y, 0f) ? value.y : value.y / by.y,
                Mathf.Approximately(by.z, 0f) ? value.z : value.z / by.z);
        }

        /// <summary>Hands the tracks bound to a fixture that is going away to the one taking its place.</summary>
        private static void Rebind(AlpsFixture from, AlpsFixture to)
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
                if (behaviour == null)
                {
                    continue;
                }

                var type = behaviour.GetType().FullName;
                if (type != null && type.StartsWith(PackageNamespace + "VRStageLighting_", StringComparison.Ordinal))
                {
                    return behaviour;
                }
            }

            return null;
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
