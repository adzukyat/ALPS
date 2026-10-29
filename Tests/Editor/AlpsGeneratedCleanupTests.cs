using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>What the preflights remove before they generate anything (<see cref="AlpsGeneratedCleanup"/>).</summary>
    public class AlpsGeneratedCleanupTests
    {
        private const string ShowPath = AlpsShowSetup.GeneratedFolder + "/Shows/Cleanup Show.playable";

        [TearDown]
        public void DeleteGenerated()
        {
            if (AssetDatabase.IsValidFolder(AlpsShowSetup.GeneratedFolder))
            {
                AssetDatabase.DeleteAsset(AlpsShowSetup.GeneratedFolder);
            }
        }

        [Test]
        public void RemoveLeftovers_KeepsWhatTheScenesAndTheUserHold()
        {
            var timeline = AlpsShowSetup.GeneratedFolder + "/Generated/Left_00000000_Build.playable";
            var gpuMaterial = AlpsGpuAssets.AssetFolder + "/ALPS Frames.mat";
            var coneMaterial = AlpsGpuAssets.AssetFolder + "/Materials/Volumetric 00000000.mat";
            var fixtureMaterial = AlpsFixtureMaterials.Folder + "/Fixture.mat";
            var shader = AlpsProjectionLight.Variant(Shader.Find("VRSL/Standard Mover/Projection"), true);
            Create(ScriptableObject.CreateInstance<TimelineAsset>(), timeline);
            Create(new Material(Shader.Find("Unlit/Color")), gpuMaterial);
            Create(new Material(Shader.Find("Unlit/Color")), coneMaterial);
            Create(new Material(shader), fixtureMaterial);
            Create(ScriptableObject.CreateInstance<TimelineAsset>(), ShowPath);

            AlpsGeneratedCleanup.RemoveLeftovers();

            Assert.IsFalse(AssetDatabase.IsValidFolder(AlpsBuildTimeline.Folder), "A build timeline a build left goes.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(AlpsGpuAssets.AssetFolder), "What older versions wrote goes.");
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GetAssetPath(shader)), "The projection shaders stay.");
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<Material>(fixtureMaterial), "The fixtures' materials stay.");
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<TimelineAsset>(ShowPath), "The shows are the user's.");
        }

        private static void Create(Object asset, string path)
        {
            AlpsShowSetup.EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
