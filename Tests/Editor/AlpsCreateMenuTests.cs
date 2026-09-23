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
        public void CreateArrangement_HoldsFixturesInTheGroup()
        {
            var arrangementObject = AlpsCreateMenu.CreateArrangement(null);
            var arrangement = arrangementObject.GetComponent<AlpsArrangement>();
            var group = arrangementObject.GetComponent<AlpsFixtureGroup>();
            Assert.NotNull(arrangement);
            Assert.NotNull(group);
            Assert.AreEqual(AlpsCreateMenu.ArrangementName, arrangementObject.name);

            var first = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MoverSpotlight, arrangementObject);
            var second = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MoverSpotlight, first);

            // The watcher does this once the creation is published.
            AlpsArrangementLayout.Refresh(arrangement, recordUndo: false);

            CollectionAssert.AreEqual(
                new AlpsFixture[] { first.GetComponent<AlpsFixture>(), second.GetComponent<AlpsFixture>() },
                group.fixtures);
            Assert.AreNotEqual(first.transform.localPosition, second.transform.localPosition, "The arrangement lays them out.");
        }
    }
}
