using System.Collections;
using System.IO;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Builds a scene into an asset bundle, the way VRChat builds a world, with
    /// <see cref="AlpsRenderOrder"/> run while the scene is processed, and loads it back in
    /// play mode. The copies it makes are never saved, so this holds that the build writes them
    /// into the scene. Explicit, since it builds a bundle and enters play mode.
    /// </summary>
    [Explicit]
    public class AlpsRenderOrderBuildTests
    {
        private const string Folder = "Assets/AlpsTestFixtures";
        private const string ScenePath = Folder + "/RenderOrderBundle.unity";
        private const string WorldMaterialPath = Folder + "/RenderOrderBundleWorld.mat";
        private const string PlayerMaterialPath = Folder + "/RenderOrderBundlePlayer.mat";
        private const string BundleFolder = "Temp/AlpsRenderOrderBundle";
        private const string BundleName = "renderorder";

        [UnityTest]
        public IEnumerator SceneBundle_KeepsTheCopiesInTheScene()
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
            int worldQueue = -1, playerQueue = -1;
            foreach (var root in scene.GetRootGameObjects())
            {
                var queue = root.GetComponent<Renderer>().sharedMaterial.renderQueue;
                if (root.name == "World")
                {
                    worldQueue = queue;
                }
                else if (root.name == "Player")
                {
                    playerQueue = queue;
                }
            }

            yield return SceneManager.UnloadSceneAsync(scene);
            bundle.Unload(true);
            AssetDatabase.DeleteAsset(WorldMaterialPath);
            AssetDatabase.DeleteAsset(PlayerMaterialPath);
            FileUtil.DeleteFileOrDirectory(BundleFolder);

            Assert.AreEqual(AlpsRenderOrder.ProjectionQueue - 1, worldQueue, "The world draws with the copy made while the scene was processed.");
            Assert.AreEqual((int)RenderQueue.Geometry, playerQueue, "The player keeps its material.");
            yield return new ExitPlayMode();
        }

        private static void BuildBundle()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Cube("World", 0, WorldMaterialPath);
            Cube("Player", 9, PlayerMaterialPath);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Directory.CreateDirectory(BundleFolder);
            var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { ScenePath } };
            RenderOrderProbe.Enabled = true;
            try
            {
                BuildPipeline.BuildAssetBundles(BundleFolder, new[] { build }, BuildAssetBundleOptions.ForceRebuildAssetBundle, EditorUserBuildSettings.activeBuildTarget);
            }
            finally
            {
                RenderOrderProbe.Enabled = false;
            }

            // Play mode loads the scene from the bundle alone.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScenePath);
        }

        private static void Cube(string name, int layer, string materialPath)
        {
            var material = new Material(Shader.Find("Unlit/Color"));
            AssetDatabase.CreateAsset(material, materialPath);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.layer = layer;
            cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>Runs the render order on the scene the test builds, and on no other build.</summary>
        private class RenderOrderProbe : IProcessSceneWithReport
        {
            public static bool Enabled;

            public int callbackOrder => 0;

            public void OnProcessScene(Scene scene, BuildReport report)
            {
                if (Enabled && scene.path == ScenePath)
                {
                    AlpsRenderOrder.Apply(scene);
                }
            }
        }
    }
}
