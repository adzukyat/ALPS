using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Runs before VRChat starts a build: validates every open show, compiles the Udon
    /// player, and writes the build timelines and the GPU assets the scene processor will
    /// switch to.
    /// </summary>
    public class AlpsBuildPreflight : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 0;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene)
            {
                return true;
            }

            return Run();
        }

        public static bool Run()
        {
            AssetDatabase.SaveAssets();

            var errors = new List<string>();
            var warnings = new List<string>();
            var hasShow = false;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                AlpsShowApplier.Validate(scene, errors, warnings);
                foreach (var director in AlpsShowApplier.FindShowDirectors(scene))
                {
                    hasShow = true;
                    AlpsBuildTimeline.Generate((UnityEngine.Timeline.TimelineAsset)director.playableAsset, errors);
                }
            }

            if (hasShow)
            {
                AlpsShowSetup.EnsureProgramAsset();
                if (!AlpsGpuAssets.GetShared(true).IsComplete)
                {
                    errors.Add("The show player's materials and targets could not be created.");
                }

                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded)
                    {
                        AlpsGpuAssets.PrepareConeLengthMaterials(scene);
                    }
                }
            }

            foreach (var warning in warnings)
            {
                Debug.LogWarning("[ALPS] " + warning);
            }

            foreach (var error in errors)
            {
                Debug.LogError("[ALPS] " + error);
            }

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// Makes the player's Udon program before play mode starts, since the scene processor
    /// creates the player while Unity is already loading the play mode scene.
    /// </summary>
    [InitializeOnLoad]
    internal static class AlpsPlayModePreflight
    {
        static AlpsPlayModePreflight()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && AlpsShowApplier.FindShowDirectors(scene).Count > 0)
                {
                    // Assets cannot be made once the play mode scene is being processed.
                    AlpsShowSetup.EnsureProgramAsset();
                    AlpsGpuAssets.GetShared(true);
                    for (var j = 0; j < SceneManager.sceneCount; j++)
                    {
                        var other = SceneManager.GetSceneAt(j);
                        if (other.isLoaded)
                        {
                            AlpsGpuAssets.PrepareConeLengthMaterials(other);
                        }
                    }

                    return;
                }
            }
        }
    }

    /// <summary>
    /// Applies the show to the scene copy Unity builds, and to the scene when entering
    /// play mode so ClientSim plays exactly what VRChat will. Runs before Udon serializes
    /// its programs, so the player's filled arrays are part of the build.
    /// </summary>
    public class AlpsBuildSceneProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => -100;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            var isBuild = report != null;
            if (!isBuild && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var errors = new List<string>();
            AlpsShowApplier.Apply(scene, generateTimelines: !isBuild, errors);
            if (errors.Count == 0)
            {
                return;
            }

            foreach (var error in errors)
            {
                Debug.LogError("[ALPS] " + error);
            }

            if (isBuild)
            {
                throw new BuildFailedException("[ALPS] The lighting show could not be applied. See the errors above.");
            }
        }
    }
}
