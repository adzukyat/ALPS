using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The materials ALPS gives the fixtures it adds, so what ALPS changes about VRSL's look
    /// reaches those fixtures alone and fixtures already in a world keep theirs. Their
    /// projections draw as added light (<see cref="AlpsProjectionLight"/>). The materials are
    /// assets under <see cref="Folder"/>, one per VRSL material, so the fixtures share them the
    /// way they shared VRSL's, the preview shows what a build plays, and VRSL's control panel
    /// finds them in the scene like its own.
    /// </summary>
    public static class AlpsFixtureMaterials
    {
        public const string Folder = AlpsShowSetup.GeneratedFolder + "/Materials";

        /// <summary>
        /// Points every renderer below <paramref name="root"/> at ALPS's material for each of
        /// VRSL's it draws with, recording undo when <paramref name="undo"/> is set. Whether
        /// anything changed.
        /// </summary>
        public static bool Apply(GameObject root, bool undo)
        {
            var changed = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var swapped = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = For(materials[i]);
                    if (material != materials[i])
                    {
                        materials[i] = material;
                        swapped = true;
                    }
                }

                if (!swapped)
                {
                    continue;
                }

                if (undo)
                {
                    Undo.RecordObject(renderer, "Use ALPS Materials");
                }

                renderer.sharedMaterials = materials;
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }

                changed = true;
            }

            return changed;
        }

        /// <summary>Whether a renderer below <paramref name="root"/> still draws with one of VRSL's materials ALPS has its own for.</summary>
        public static bool CanApply(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (Replaces(material))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// ALPS's material for <paramref name="source"/>, made the first time and found again
        /// after, or <paramref name="source"/> itself where ALPS changes nothing about it.
        /// </summary>
        public static Material For(Material source)
        {
            if (!Replaces(source))
            {
                return source;
            }

            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            var existing = Find(guid);
            if (existing != null)
            {
                return existing;
            }

            var material = AlpsProjectionLight.CreateLight(source);
            if (material == null)
            {
                return source;
            }

            AlpsShowSetup.EnsureFolder(Folder);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{source.name}.mat");
            AssetDatabase.CreateAsset(material, path);
            // The importer remembers which of VRSL's materials it stands for.
            var importer = AssetImporter.GetAtPath(path);
            importer.userData = guid;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        /// <summary>One of VRSL's own materials, an asset outside <see cref="Folder"/>, that ALPS draws another way.</summary>
        private static bool Replaces(Material material)
        {
            if (material == null || !AlpsProjectionLight.IsVrslProjection(material.shader))
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(material);
            return !string.IsNullOrEmpty(path) && !AlpsShowSetup.IsInFolder(path, Folder) && !material.IsKeywordEnabled("_ALPHATEST_ON");
        }

        private static Material Find(string sourceGuid)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                return null;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path)?.userData == sourceGuid)
                {
                    return AssetDatabase.LoadAssetAtPath<Material>(path);
                }
            }

            return null;
        }
    }
}
