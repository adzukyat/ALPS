using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The experimental GPU playback: the show is evaluated by a shader and handed to VRSL as
    /// its DMX grid, for the preview, play mode and builds alike. Switched per machine from
    /// the ALPS menu while it is being tried out.
    /// </summary>
    [InitializeOnLoad]
    public static class AlpsGpuPlayback
    {
        private const string PreferenceKey = "AdzukiSoft.ALPS.GpuPlayback";
        private const string MenuPath = "ALPS/Experimental/GPU Playback";
        public const string AssetFolder = AlpsShowSetup.GeneratedFolder + "/Gpu";

        /// <summary>Rows of the shared frames target, one per fixture.</summary>
        public const int FrameRows = 128;

        static AlpsGpuPlayback()
        {
            AlpsPreviewDriver.UseGpu = Enabled;
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PreferenceKey, false);
            set
            {
                EditorPrefs.SetBool(PreferenceKey, value);
                AlpsPreviewDriver.UseGpu = value;
                AlpsPreviewDriver.MarkDirty();
            }
        }

        [MenuItem(MenuPath, priority = 200)]
        private static void Toggle()
        {
            Enabled = !Enabled;
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>
        /// The materials and targets every GPU player shares. Their size never depends on the
        /// show, so one set covers every scene. Missing ones are created when
        /// <paramref name="create"/> allows it, which a running build does not.
        /// </summary>
        public static bool GetAssets(
            bool create,
            out Material framesMaterial,
            out Material gridMaterial,
            out RenderTexture frames,
            out RenderTexture grid,
            out RenderTexture spin)
        {
            framesMaterial = GetMaterial(create, "ALPS Frames", AlpsPreviewDriver.GpuFramesShader);
            gridMaterial = GetMaterial(create, "ALPS DMX Grid", AlpsPreviewDriver.GpuGridShader);
            frames = GetTarget(create, "ALPS Frames", AlpsShowPlayer.GpuFrameTexels, FrameRows);
            grid = GetTarget(create, "ALPS DMX Grid", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight);
            spin = GetTarget(create, "ALPS DMX Spin", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight);
            if (create)
            {
                AssetDatabase.SaveAssets();
            }

            return framesMaterial != null && gridMaterial != null && frames != null && grid != null && spin != null;
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
