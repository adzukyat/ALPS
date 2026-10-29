using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRSL;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// The Hierarchy's ALPS create menu: every fixture it offers comes from the VRSL package
    /// ready to drive, and new objects land where the menu was opened.
    /// </summary>
    public class AlpsCreateMenuTests
    {
        [SetUp]
        public void OpenScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void CloseScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The fixtures ALPS adds bring materials of their own.
            AssetDatabase.DeleteAsset(AlpsFixtureMaterials.Folder);
            AssetDatabase.DeleteAsset(AlpsProjectionLight.ShaderFolder);
            AlpsShowSetup.DeleteFolderIfEmpty(AlpsShowSetup.GeneratedFolder);
        }

        [Test]
        public void Fixtures_AreVrslDmxStaticPrefabs()
        {
            foreach (var fixture in AlpsCreateMenu.Fixtures)
            {
                var prefab = fixture.Load();
                Assert.NotNull(prefab, $"{fixture.Name} did not load from the VRSL package.");
                Assert.NotNull(prefab.GetComponentInChildren<VRStageLighting_DMX_Static>(true), $"{fixture.Name} is not a DMX Static fixture.");
                Assert.AreEqual(fixture.Path, AssetDatabase.GUIDToAssetPath(fixture.Guid), $"{fixture.Name}'s GUID and path disagree.");
            }
        }

        [Test]
        public void CreateFixture_AddsAReadyFixtureUnderTheContext()
        {
            var rig = new GameObject("Rig");

            var created = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MoverSpotlight, rig);

            Assert.AreSame(rig.transform, created.transform.parent);
            Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(created), "The fixture stays linked to its VRSL prefab.");
            var fixture = created.GetComponent<AlpsVRSLFixture>();
            Assert.NotNull(fixture);
            Assert.IsTrue(fixture.IsReady, "The fixture finds its VRSL target.");
        }

        [Test]
        public void CreateFixture_DrawsItsProjectionWithAlpsMaterial()
        {
            var rig = new GameObject("Rig");
            var prefab = AlpsCreateMenu.MoverSpotlight.Load();
            var stock = Projection(prefab).sharedMaterial;
            var shader = stock.shader;

            var first = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MoverSpotlight, rig);
            var second = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MoverSpotlight, rig);

            var material = Projection(first).sharedMaterial;
            Assert.AreNotSame(stock, material);
            Assert.IsTrue(AlpsShowSetup.IsInFolder(AssetDatabase.GetAssetPath(material), AlpsFixtureMaterials.Folder), "The material is an asset, so the preview draws it too.");
            Assert.AreEqual("Hidden/ALPS/" + shader.name, material.shader.name, "The projection adds its light.");
            Assert.AreEqual(stock.GetFloat("_ProjectionIntensity") * AlpsProjectionLight.IntensityScale, material.GetFloat("_ProjectionIntensity"), 1e-5f);
            Assert.AreSame(material, Projection(second).sharedMaterial, "The fixtures share it the way they shared VRSL's.");
            Assert.AreSame(shader, stock.shader, "VRSL's own material is left alone.");
            Assert.IsFalse(AlpsFixtureMaterials.CanApply(first));
            Assert.IsTrue(AlpsFixtureMaterials.CanApply(prefab), "A fixture still on VRSL's materials can switch.");
        }

        [Test]
        public void CreateFixture_HasTheSameScaleAtTheRootAsUnderAContainer()
        {
            var rig = new GameObject("Rig");
            foreach (var fixture in AlpsCreateMenu.Fixtures)
            {
                var underRig = AlpsCreateMenu.CreateFixture(fixture, rig);
                var atRoot = AlpsCreateMenu.CreateFixture(fixture, null);

                Assert.AreEqual(Vector3.one, underRig.transform.localScale, $"{fixture.Name} under a parent.");
                Assert.AreEqual(Vector3.one, atRoot.transform.localScale, $"{fixture.Name} at the scene root.");
            }
        }

        [Test]
        public void CreateFixture_OnAFixtureGoesNextToIt()
        {
            var rig = new GameObject("Rig");
            var first = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.ParLight, rig);
            var last = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.ParLight, rig);

            var between = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.Blinder, first);

            Assert.AreSame(rig.transform, between.transform.parent);
            Assert.AreEqual(1, between.transform.GetSiblingIndex());
            Assert.AreEqual(2, last.transform.GetSiblingIndex());
            Assert.AreNotEqual(first.name, last.name, "Siblings get unique names.");
        }

        [Test]
        public void CreateContainer_HoldsTheFixturesAddedUnderIt()
        {
            var containerObject = AlpsCreateMenu.CreateContainer(null);
            var container = containerObject.GetComponent<AlpsContainer>();
            Assert.NotNull(container);
            Assert.AreEqual(AlpsCreateMenu.ContainerName, containerObject.name);
            Assert.AreEqual(AlpsArrangementShape.Off, container.settings.shape);

            var first = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MoverSpotlight, containerObject);
            var second = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MoverSpotlight, first);
            CollectionAssert.AreEqual(
                new AlpsFixture[] { first.GetComponent<AlpsFixture>(), second.GetComponent<AlpsFixture>() },
                container.Fixtures());

            // Picking a shape lays out children that were created on one spot.
            container.settings.shape = AlpsArrangementShape.Line;
            AlpsArrangementLayout.CommitEdit(container);
            Assert.AreNotEqual(first.transform.localPosition, second.transform.localPosition, "The container lays them out.");
        }

        private static Renderer Projection(GameObject fixture)
        {
            foreach (var renderer in fixture.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterial != null && AlpsProjectionLight.IsProjection(renderer.sharedMaterial.shader))
                {
                    return renderer;
                }
            }

            Assert.Fail($"{fixture.name} has no projection.");
            return null;
        }
    }
}
