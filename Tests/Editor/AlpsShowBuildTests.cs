using System.Collections;
using System.IO;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using VRC.Udon;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Builds the preview smoke scene into an asset bundle the way VRChat builds a world, with
    /// the preflight first and the show applied while the scene is processed, removes the
    /// build timelines the way the end of a build does, and loads the bundle back in play mode.
    /// The shared materials and targets and the cone length copies are never saved, so this
    /// holds that the build writes them into the scene, and that the bundle needs no generated
    /// asset once it is built. Explicit, since it builds a bundle and enters play mode.
    /// </summary>
    [Explicit]
    public class AlpsShowBuildTests
    {
        private const string ScenePath = AlpsPreviewSmokeFixtureBuilder.FolderPath + "/ShowBundle.unity";
        private const string VolumetricMaterial = "Packages/com.acchosen.vr-stage-lighting/Runtime/Materials/Lights/DMX/MoverLight - Spotlight/VRSL-StandardMover-VolumetricMat-HQ-Horizontal.mat";
        private const string BundleFolder = "Temp/AlpsShowBundle";
        private const string BundleName = "show";

        [UnityTest]
        public IEnumerator SceneBundle_KeepsWhatApplyMadeInMemory()
        {
            if (!Application.isPlaying)
            {
                BuildBundle();
                yield return new EnterPlayMode();
            }

            var bundle = AssetBundle.LoadFromFile(Path.Combine(BundleFolder, BundleName));
            Assert.NotNull(bundle, "The bundle was not built.");
            var path = bundle.GetAllScenePaths()[0];
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);

            var scene = SceneManager.GetSceneByPath(path);
            var behaviours = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<UdonBehaviour>(true)).ToList();
            var player = behaviours.FirstOrDefault(b => b.name == AlpsShowSetup.PlayerName);
            var director = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayableDirector>(true)).First();
            var coneLength = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null && material.HasProperty(AlpsShowPlayer.ConeLengthViaDmxProperty))
                .Select(material => material.GetFloat(AlpsShowPlayer.ConeLengthViaDmxProperty))
                .ToList();

            var grid = player != null ? player.GetProgramVariable("grid") as RenderTexture : null;
            var frames = player != null ? player.GetProgramVariable("frames") as RenderTexture : null;
            var framesMaterial = player != null ? player.GetProgramVariable("framesMaterial") as Material : null;
            var gridMaterial = player != null ? player.GetProgramVariable("gridMaterial") as Material : null;
            var framesShader = framesMaterial != null ? framesMaterial.shader.name : null;
            var gridShader = gridMaterial != null ? gridMaterial.shader.name : null;
            var timeline = director.playableAsset as TimelineAsset;
            var tracks = timeline != null ? timeline.GetRootTracks().Count() : 0;

            yield return SceneManager.UnloadSceneAsync(scene);
            bundle.Unload(true);
            FileUtil.DeleteFileOrDirectory(BundleFolder);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(AlpsShowSetup.GeneratedFolder);

            Assert.NotNull(player, "The scene holds the show player.");
            Assert.NotNull(grid, "The player keeps the grid made in memory.");
            Assert.AreEqual(AlpsShowPlayer.GpuGridWidth, grid.width);
            Assert.AreEqual(AlpsShowPlayer.GpuGridHeight, grid.height);
            Assert.NotNull(frames, "The player keeps the frames target made in memory.");
            Assert.AreEqual(AlpsGpuAssets.FrameRows, frames.height);
            Assert.AreEqual(AlpsPreviewDriver.GpuFramesShader, framesShader, "The player keeps the frames material made in memory.");
            Assert.AreEqual(AlpsPreviewDriver.GpuGridShader, gridShader);
            Assert.NotNull(timeline, "The director keeps the build timeline after it was removed from the project.");
            Assert.Greater(tracks, 0);
            Assert.IsNotEmpty(coneLength);
            Assert.That(coneLength, Is.All.EqualTo(1f), "The volumetric materials draw with cone length via DMX on.");
            yield return new ExitPlayMode();
        }

        private static void BuildBundle()
        {
            var context = AlpsPreviewSmokeFixtureBuilder.OpenFreshScene();
            var volumetric = AssetDatabase.LoadAssetAtPath<Material>(VolumetricMaterial);
            Assert.NotNull(volumetric, VolumetricMaterial);
            foreach (var fixture in context.Fixtures)
            {
                fixture.objRenderers[0].sharedMaterial = volumetric;
            }

            EditorSceneManager.SaveScene(context.Director.gameObject.scene, ScenePath, true);
            Assert.IsTrue(AlpsBuildPreflight.Run(), "The preflight passes.");

            Directory.CreateDirectory(BundleFolder);
            var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { ScenePath } };
            BuildPipeline.BuildAssetBundles(BundleFolder, new[] { build }, BuildAssetBundleOptions.ForceRebuildAssetBundle, EditorUserBuildSettings.activeBuildTarget);
            AlpsBuildTimeline.DeleteAll();
            Assert.IsFalse(AssetDatabase.IsValidFolder(AlpsBuildTimeline.Folder), "The build timelines are gone.");
            if (AssetDatabase.IsValidFolder(AlpsShowSetup.GeneratedFolder))
            {
                Assert.IsEmpty(AssetDatabase.FindAssets("t:Material t:RenderTexture", new[] { AlpsShowSetup.GeneratedFolder }), "Nothing else was saved.");
            }

            // Play mode loads the scene from the bundle alone.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
