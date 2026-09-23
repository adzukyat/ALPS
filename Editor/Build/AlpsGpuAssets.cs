using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRSL;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The assets GPU playback needs in a build: the materials and targets every show player
    /// shares, and copies of VRSL's volumetric materials with cone length via DMX turned on.
    /// Udon cannot create render targets, and a build cannot create assets, so the preflights
    /// make them beforehand under <see cref="AssetFolder"/>.
    /// </summary>
    public static class AlpsGpuAssets
    {
        public const string AssetFolder = AlpsShowSetup.GeneratedFolder + "/Gpu";
        public const string MaterialFolder = AssetFolder + "/Materials";

        /// <summary>Rows of the shared frames targets, one per fixture of a show.</summary>
        public const int FrameRows = 128;

        /// <summary>The materials and targets every show player shares.</summary>
        public sealed class Shared
        {
            public Material framesMaterial;
            public Material gridMaterial;
            public RenderTexture frames;
            public RenderTexture framesPrevious;
            public RenderTexture grid;
            public RenderTexture spin;

            public bool IsComplete =>
                framesMaterial != null && gridMaterial != null && frames != null && framesPrevious != null && grid != null && spin != null;
        }

        /// <summary>
        /// The shared materials and targets. Their size never depends on the show, so one set
        /// covers every scene. Missing ones are created when <paramref name="create"/> allows
        /// it, which a running build does not.
        /// </summary>
        public static Shared GetShared(bool create)
        {
            var shared = new Shared
            {
                framesMaterial = GetMaterial(create, "ALPS Frames", AlpsPreviewDriver.GpuFramesShader),
                gridMaterial = GetMaterial(create, "ALPS DMX Grid", AlpsPreviewDriver.GpuGridShader),
                frames = GetTarget(create, "ALPS Frames", AlpsShowPlayer.GpuFrameTexels, FrameRows),
                framesPrevious = GetTarget(create, "ALPS Frames Previous", AlpsShowPlayer.GpuFrameTexels, FrameRows),
                grid = GetTarget(create, "ALPS DMX Grid", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight),
                spin = GetTarget(create, "ALPS DMX Spin", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight),
            };

            if (create)
            {
                AssetDatabase.SaveAssets();
            }

            return shared;
        }

        /// <summary>
        /// Makes the cone length copies of every VRSL material the shows in
        /// <paramref name="scene"/> use, refreshed from their source each time.
        /// </summary>
        public static void PrepareConeLengthMaterials(Scene scene)
        {
            foreach (var director in AlpsShowApplier.FindShowDirectors(scene))
            {
                foreach (var fixture in AlpsShowCompiler.Compile(director).fixtures)
                {
                    if (fixture is AlpsVRSLFixture vrslFixture && vrslFixture.ResolveTarget() != null)
                    {
                        foreach (var material in UsedMaterials(vrslFixture.ResolveTarget()))
                        {
                            ConeLengthMaterial(material, true);
                        }
                    }
                }
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Points a VRSL fixture's renderers at the cone length copies of their materials.
        /// Only ever run on a scene copy.
        /// </summary>
        public static void UseConeLengthMaterials(VRStageLighting_DMX_Static fixture, bool create, List<string> errors)
        {
            if (fixture.objRenderers == null)
            {
                return;
            }

            foreach (var renderer in fixture.objRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (!NeedsConeLength(materials[i]))
                    {
                        continue;
                    }

                    var copy = ConeLengthMaterial(materials[i], create);
                    if (copy == null)
                    {
                        errors.Add($"The cone length copy of material '{materials[i].name}' on '{renderer.name}' is missing.");
                        continue;
                    }

                    materials[i] = copy;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        private static IEnumerable<Material> UsedMaterials(VRStageLighting_DMX_Static fixture)
        {
            if (fixture.objRenderers == null)
            {
                yield break;
            }

            foreach (var renderer in fixture.objRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                foreach (var material in renderer.sharedMaterials)
                {
                    if (NeedsConeLength(material))
                    {
                        yield return material;
                    }
                }
            }
        }

        /// <summary>A VRSL volumetric material whose cone length via DMX is off.</summary>
        private static bool NeedsConeLength(Material material)
        {
            return material != null
                && material.HasProperty(AlpsShowPlayer.ConeLengthViaDmxProperty)
                && material.GetFloat(AlpsShowPlayer.ConeLengthViaDmxProperty) < 0.5f;
        }

        /// <summary>
        /// The copy of <paramref name="source"/> with cone length via DMX on, or null when it
        /// is missing and <paramref name="create"/> does not allow making it. A material that
        /// is not an asset lives in the scene copy alone, so it is turned on in place.
        /// </summary>
        private static Material ConeLengthMaterial(Material source, bool create)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                EnableConeLength(source);
                return source;
            }

            var guid = AssetDatabase.AssetPathToGUID(sourcePath);
            var path = $"{MaterialFolder}/{source.name} {guid.Substring(0, 8)}.mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!create)
            {
                return copy;
            }

            if (copy == null)
            {
                AlpsShowSetup.EnsureFolder(MaterialFolder);
                copy = new Material(source) { name = source.name };
                EnableConeLength(copy);
                AssetDatabase.CreateAsset(copy, path);
                return copy;
            }

            // Follows edits to the source since the copy was made.
            copy.shader = source.shader;
            copy.CopyPropertiesFromMaterial(source);
            EnableConeLength(copy);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static void EnableConeLength(Material material)
        {
            material.SetFloat(AlpsShowPlayer.ConeLengthViaDmxProperty, 1f);
            material.EnableKeyword("_ENABLEEXTRACHANNELS_ON");
        }

        private static Material GetMaterial(bool create, string name, string shaderName)
        {
            var path = AssetFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null || !create)
            {
                return material;
            }

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[ALPS] The shader '{shaderName}' is missing.");
                return null;
            }

            AlpsShowSetup.EnsureFolder(AssetFolder);
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static RenderTexture GetTarget(bool create, string name, int width, int height)
        {
            var path = AssetFolder + "/" + name + ".renderTexture";
            var target = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (target != null || !create)
            {
                return target;
            }

            AlpsShowSetup.EnsureFolder(AssetFolder);
            target = AlpsPreviewDriver.CreateGpuTarget(name, width, height);
            target.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(target, path);
            return target;
        }
    }
}
