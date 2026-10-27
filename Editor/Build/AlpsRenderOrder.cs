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
    /// too, so the world's opaque queues move below <see cref="ProjectionQueue"/>
    /// (<see cref="Remap"/>), keeping their order, and the projections sit there between the
    /// two. The renderers switch to copies of their materials made in memory. Nothing saves
    /// them, so the project gains no assets: a build writes them into the scene, which is the
    /// only thing that refers to them. A material a script or an animation swaps in while the
    /// world runs is the original, so it draws after the projections again.
    /// </summary>
    public static class AlpsRenderOrder
    {
        public const int ProjectionQueue = (int)RenderQueue.Geometry - 50;

        /// <summary>VRChat's Player, PlayerLocal and MirrorReflection layers.</summary>
        private const int PlayerLayers = (1 << 9) | (1 << 10) | (1 << 18);

        /// <summary>
        /// Points every renderer of the scene but players at copies of its materials: VRSL
        /// projections at <see cref="ProjectionQueue"/>, opaque ones below it in the order they
        /// had. An avatar placed in the scene to look at counts as a player, so it stays after
        /// the projections the way a player's will. Only ever run on a scene copy.
        /// </summary>
        public static void Apply(Scene scene)
        {
            var renderers = new List<Renderer>();
            var queues = new List<int>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (IsPlayer(renderer))
                    {
                        continue;
                    }

                    renderers.Add(renderer);
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (IsOpaque(material))
                        {
                            queues.Add(material.renderQueue);
                        }
                    }
                }
            }

            var remap = Remap(queues);
            var copies = new Dictionary<Material, Material>();
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var copy = Reordered(materials[i], remap, copies);
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

        /// <summary>
        /// Where each of the world's opaque queues moves so all of them draw before
        /// <see cref="ProjectionQueue"/>. Only their order decides what draws first, so from the
        /// highest down each keeps its own value or takes one below the queue above it,
        /// whichever is lower: queues low enough already stay where they are, the ones from
        /// the projections up close in below them, and equal queues stay equal. They run out of
        /// room only with more different queues than there are below the projections.
        /// </summary>
        public static Dictionary<int, int> Remap(IEnumerable<int> queues)
        {
            var distinct = new SortedSet<int>(queues);
            var remap = new Dictionary<int, int>();
            var next = ProjectionQueue - 1;
            foreach (var queue in distinct.Reverse())
            {
                var moved = Mathf.Max(0, Mathf.Min(queue, next));
                remap.Add(queue, moved);
                next = moved - 1;
            }

            return remap;
        }

        private static bool IsPlayer(Renderer renderer)
        {
            return (PlayerLayers & (1 << renderer.gameObject.layer)) != 0
                || renderer.GetComponentInParent<VRC_AvatarDescriptor>(true) != null;
        }

        private static bool IsProjection(Material material)
        {
            return material.shader.name.StartsWith("VRSL/") && material.shader.name.EndsWith("/Projection");
        }

        private static bool IsOpaque(Material material)
        {
            return material != null && material.shader != null && !IsProjection(material)
                && material.renderQueue <= (int)RenderQueue.GeometryLast;
        }

        private static Material Reordered(Material material, Dictionary<int, int> remap, Dictionary<Material, Material> copies)
        {
            if (material == null || material.shader == null)
            {
                return material;
            }

            if (copies.TryGetValue(material, out var copy))
            {
                return copy;
            }

            int reordered;
            if (IsProjection(material))
            {
                reordered = ProjectionQueue;
            }
            else if (IsOpaque(material) && remap[material.renderQueue] != material.renderQueue)
            {
                reordered = remap[material.renderQueue];
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
