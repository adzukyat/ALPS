using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AdzukiSoft.ALPS.Vrsl;

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
        public void Fixtures_TakeTheirConeLengthFromDmx()
        {
            var found = 0;
            foreach (var fixture in AlpsCreateMenu.Fixtures)
            {
                foreach (var renderer in fixture.Load().GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material != null && material.HasProperty(AlpsShowPlayer.ConeLengthViaDmxProperty))
                        {
                            found++;
                            Assert.AreEqual(1f, material.GetFloat(AlpsShowPlayer.ConeLengthViaDmxProperty), $"{material.name} of {fixture.Name} has cone length via DMX off.");
                        }
                    }
                }
            }

            Assert.Greater(found, 0, "No fixture has a material that reads the cone length.");
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
        public void Fixtures_AddTheirProjectionsAsLight()
        {
            var found = 0;
            foreach (var fixture in AlpsCreateMenu.Fixtures)
            {
                foreach (var renderer in fixture.Load().GetComponentsInChildren<Renderer>(true))
                {
                    var material = renderer.sharedMaterial;
                    if (material == null || !material.shader.name.EndsWith("/Projection"))
                    {
                        continue;
                    }

                    found++;
                    var source = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(AssetDatabase.GetAssetPath(material.shader)));
                    StringAssert.Contains("Blend One One", source, $"{material.shader.name} does not add its light.");
                    StringAssert.DoesNotContain("DstColor", source.Substring(source.IndexOf("SubShader", System.StringComparison.Ordinal)), $"{material.shader.name} still multiplies.");
                }
            }

            Assert.Greater(found, 0, "No fixture has a projection.");
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
    }
}
