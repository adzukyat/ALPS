using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// Runs before VRChat starts a build: validates every open show, compiles the Udon
    /// player, and writes the build timelines the scene processor will switch to.
    /// </summary>
    public class MfvBuildPreflight : IVRCSDKBuildRequestedCallback
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

                MfvShowApplier.Validate(scene, errors, warnings);
                foreach (var director in MfvShowApplier.FindShowDirectors(scene))
                {
                    hasShow = true;
                    MfvBuildTimeline.Generate((UnityEngine.Timeline.TimelineAsset)director.playableAsset, errors);
                }
            }

            if (hasShow)
            {
                MfvShowSetup.EnsureProgramAsset();
            }

            foreach (var warning in warnings)
            {
                Debug.LogWarning("[ManeuverForVRC] " + warning);
            }

            foreach (var error in errors)
            {
                Debug.LogError("[ManeuverForVRC] " + error);
            }

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// Applies the show to the scene copy Unity builds, and to the scene when entering
    /// play mode so ClientSim plays exactly what VRChat will. Runs before Udon serializes
    /// its programs, so the player's filled arrays are part of the build.
    /// </summary>
    public class MfvBuildSceneProcessor : IProcessSceneWithReport
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
            MfvShowApplier.Apply(scene, generateTimelines: !isBuild, errors);
            if (errors.Count == 0)
            {
                return;
            }

            foreach (var error in errors)
            {
                Debug.LogError("[ManeuverForVRC] " + error);
            }

            if (isBuild)
            {
                throw new BuildFailedException("[ManeuverForVRC] The lighting show could not be applied. See the errors above.");
            }
        }
    }
}
