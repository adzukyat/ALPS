using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRSL;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// What GPU playback needs in a build: the materials and targets every show player of a
    /// scene shares, and copies of VRSL's volumetric materials with cone length via DMX turned
    /// on. Udon cannot create render targets, so Apply makes them on the scene copy, in memory
    /// like <see cref="AlpsRenderOrder"/>'s copies: a build writes them into the scene, and the
    /// project gains no assets.
    /// </summary>
    public static class AlpsGpuAssets
    {
        /// <summary>Where older versions wrote these, which the preflights remove (<see cref="AlpsGeneratedCleanup"/>).</summary>
        public const string AssetFolder = AlpsShowSetup.GeneratedFolder + "/Gpu";

        /// <summary>Rows of the shared frames targets, one per fixture of a show.</summary>
        public const int FrameRows = 128;

        /// <summary>The materials and targets every show player of a scene shares.</summary>
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
        /// New shared materials and targets. Their size never depends on the show, so one set
        /// covers every show of a scene.
        /// </summary>
        public static Shared CreateShared()
        {
            var shared = new Shared
            {
                framesMaterial = CreateMaterial("ALPS Frames", AlpsPreviewDriver.GpuFramesShader),
                gridMaterial = CreateMaterial("ALPS DMX Grid", AlpsPreviewDriver.GpuGridShader),
                frames = CreateTarget("ALPS Frames", AlpsShowPlayer.GpuFrameTexels, FrameRows),
                framesPrevious = CreateTarget("ALPS Frames Previous", AlpsShowPlayer.GpuFrameTexels, FrameRows),
                grid = CreateTarget("ALPS DMX Grid", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight),
                spin = CreateTarget("ALPS DMX Spin", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight),
            };

            AlpsVrslVersion.Apply(shared.gridMaterial);
            return shared;
        }

        /// <summary>
        /// Points a VRSL fixture's renderers at copies of their volumetric materials with cone
        /// length via DMX on, one copy per material in <paramref name="copies"/>, so renderers
        /// sharing a material still share one. Only ever run on a scene copy.
        /// </summary>
        public static void UseConeLengthMaterials(VRStageLighting_DMX_Static fixture, Dictionary<Material, Material> copies)
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

                    if (!copies.TryGetValue(materials[i], out var copy))
                    {
                        copy = ConeLengthMaterial(materials[i]);
                        copies.Add(materials[i], copy);
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

        /// <summary>A VRSL volumetric material whose cone length via DMX is off.</summary>
        private static bool NeedsConeLength(Material material)
        {
            return material != null
                && material.HasProperty(AlpsShowPlayer.ConeLengthViaDmxProperty)
                && material.GetFloat(AlpsShowPlayer.ConeLengthViaDmxProperty) < 0.5f;
        }

        /// <summary>
        /// The copy of <paramref name="source"/> with cone length via DMX on. A material that is
        /// no asset lives on the scene copy alone, so it is turned on in place.
        /// </summary>
        private static Material ConeLengthMaterial(Material source)
        {
            var copy = EditorUtility.IsPersistent(source) ? new Material(source) { name = source.name } : source;
            copy.SetFloat(AlpsShowPlayer.ConeLengthViaDmxProperty, 1f);
            copy.EnableKeyword("_ENABLEEXTRACHANNELS_ON");
            return copy;
        }

        private static Material CreateMaterial(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[ALPS] The shader '{shaderName}' is missing.");
                return null;
            }

            return new Material(shader) { name = name };
        }

        private static RenderTexture CreateTarget(string name, int width, int height)
        {
            var target = AlpsPreviewDriver.CreateGpuTarget(name, width, height);
            target.hideFlags = HideFlags.None;
            return target;
        }
    }
}
