using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// What GPU playback needs in a build: the materials and targets every show player of a
    /// scene shares. Udon cannot create render targets, so Apply makes them on the scene copy, in memory
    /// like <see cref="AlpsRenderOrder"/>'s copies: a build writes them into the scene, and the
    /// project gains no assets.
    /// </summary>
    public static class AlpsGpuAssets
    {
        /// <summary>Where older versions wrote these, which the preflights remove (<see cref="AlpsGeneratedCleanup"/>).</summary>
        public const string AssetFolder = AlpsShowSetup.GeneratedFolder + "/Gpu";

        /// <summary>
        /// Rows of the shared frames targets, one per fixture of a show: what VRSL's grid
        /// holds, and every slot of the penlight areas a show can have.
        /// </summary>
        public const int FrameRows = AlpsShowPlayer.GpuMaxFixtures + AlpsShowPlayer.GpuMaxPenlightAreas * AlpsShowPlayer.GpuMaxPenlightSlots;

        /// <summary>The materials and targets every show player of a scene shares.</summary>
        public sealed class Shared
        {
            public Material framesMaterial;
            public Material gridMaterial;
            public Material penlightMaterial;
            public RenderTexture frames;
            public RenderTexture framesPrevious;
            public RenderTexture grid;
            public RenderTexture spin;
            public RenderTexture penlight;

            public bool IsComplete =>
                framesMaterial != null && gridMaterial != null && penlightMaterial != null
                && frames != null && framesPrevious != null && grid != null && spin != null && penlight != null;
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
                penlightMaterial = CreateMaterial("ALPS Penlight", AlpsPenlight.GridShader),
                frames = CreateTarget("ALPS Frames", AlpsShowPlayer.GpuFrameTexels, FrameRows),
                framesPrevious = CreateTarget("ALPS Frames Previous", AlpsShowPlayer.GpuFrameTexels, FrameRows),
                grid = CreateTarget("ALPS DMX Grid", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight),
                spin = CreateTarget("ALPS DMX Spin", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight),
                penlight = CreateTarget("ALPS Penlight", AlpsShowPlayer.GpuPenlightWidth, AlpsShowPlayer.GpuMaxPenlightAreas),
            };

            return shared;
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
