using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VRC.SDKBase;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Keeps players out of VRSL's projections by drawing them between the world and the
    /// avatars. A projection only brightens the colour already drawn, so an avatar drawn after
    /// it covers its own pixels with its own colour. The depth texture is drawn before every
    /// opaque object, so the projection still finds where it lands that early, and VRSL's
    /// projection shaders have no shadow caster pass, so the cones stay out of it.
    ///
    /// Avatars draw their opaque parts from Geometry (2000) on, which is where the world draws
    /// too, so the world's opaque materials move <see cref="WorldShift"/> earlier, keeping
    /// their order, and the projections sit at <see cref="ProjectionQueue"/> between them.
    /// The renderers switch to copies of their materials made in memory. Nothing saves them,
    /// so the project gains no assets: a build writes them into the scene, which is the only
    /// thing that refers to them. A material a script or an animation swaps in while the world
    /// runs is the original, so it draws after the projections again.
    /// </summary>
    public static class AlpsRenderOrder
    {
        public const int WorldShift = 1000;
        public const int ProjectionQueue = (int)RenderQueue.Geometry - 50;

        /// <summary>VRChat's Player, PlayerLocal and MirrorReflection layers.</summary>
        private const int PlayerLayers = (1 << 9) | (1 << 10) | (1 << 18);

        /// <summary>
        /// Points every renderer of the scene but players at copies of its materials: VRSL
        /// projections at <see cref="ProjectionQueue"/>, opaque ones <see cref="WorldShift"/>
        /// earlier. An avatar placed in the scene to look at counts as a player, so it stays
        /// after the projections the way a player's will. Only ever run on a scene copy.
        /// </summary>
        public static void Apply(Scene scene)
        {
            var copies = new Dictionary<Material, Material>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (IsPlayer(renderer))
                    {
                        continue;
                    }

                    var materials = renderer.sharedMaterials;
                    var changed = false;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        var copy = Reordered(materials[i], copies);
                        if (copy != materials[i])
                        {
                            materials[i] = copy;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = materials;
                    }
                }
            }
        }

        private static bool IsPlayer(Renderer renderer)
        {
            return (PlayerLayers & (1 << renderer.gameObject.layer)) != 0
                || renderer.GetComponentInParent<VRC_AvatarDescriptor>(true) != null;
        }

        private static Material Reordered(Material material, Dictionary<Material, Material> copies)
        {
            if (material == null || material.shader == null)
            {
                return material;
            }

            if (copies.TryGetValue(material, out var copy))
            {
                return copy;
            }

            var queue = material.renderQueue;
            int reordered;
            if (material.shader.name.StartsWith("VRSL/") && material.shader.name.EndsWith("/Projection"))
            {
                reordered = ProjectionQueue;
            }
            else if (queue <= (int)RenderQueue.GeometryLast)
            {
                reordered = Mathf.Max(0, queue - WorldShift);
            }
            else
            {
                copies.Add(material, material);
                return material;
            }

            copy = new Material(material) { name = material.name, renderQueue = reordered };
            copies.Add(material, copy);
            return copy;
        }
    }
}
