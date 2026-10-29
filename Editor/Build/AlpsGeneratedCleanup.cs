using UnityEditor;
using VRC.SDK3.Editor;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Removes what builds and play mode generate once nothing needs it. The build timelines
    /// go when VRChat's build finishes, its bundle written, and when play mode ends. The
    /// preflights remove what a build that never finished left, and the files older versions
    /// wrote under <see cref="AlpsGpuAssets.AssetFolder"/>, which Apply now makes in memory.
    /// </summary>
    [InitializeOnLoad]
    public static class AlpsGeneratedCleanup
    {
        static AlpsGeneratedCleanup()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode)
                {
                    AlpsBuildTimeline.DeleteAll();
                }
            };

            VRCSdkControlPanel.OnSdkPanelEnable += (_, __) =>
            {
                if (VRCSdkControlPanel.TryGetBuilder<IVRCSdkWorldBuilderApi>(out var builder))
                {
                    builder.OnSdkBuildFinish -= OnBuildFinished;
                    builder.OnSdkBuildFinish += OnBuildFinished;
                }
            };
        }

        public static void RemoveLeftovers()
        {
            AlpsBuildTimeline.DeleteAll();
            if (AssetDatabase.IsValidFolder(AlpsGpuAssets.AssetFolder))
            {
                AssetDatabase.DeleteAsset(AlpsGpuAssets.AssetFolder);
                AlpsShowSetup.DeleteFolderIfEmpty(AlpsShowSetup.GeneratedFolder);
            }
        }

        private static void OnBuildFinished(object sender, string message)
        {
            AlpsBuildTimeline.DeleteAll();
        }
    }
}
