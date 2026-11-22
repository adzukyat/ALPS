using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Moving fixtures of the VR Stage Lighting package over to the fixtures ALPS comes with.
    /// The tests need the package in the project, which the test project has for this.
    /// </summary>
    public class AlpsVrslMigrationTests
    {
        private const string PackagePrefabs = "Packages/com.acchosen.vr-stage-lighting/Runtime/Prefabs/";
        private const string DmxMover = PackagePrefabs + "DMX/Horizontal Mode/VRSL-DMX-Mover-Spotlight-H-13CH.prefab";
        private const string DmxMultiLightBar = PackagePrefabs + "DMX/Horizontal Mode/VRSL-DMX-Static-MultiLightBar-H-15CH.prefab";
        private const string AudioLinkMover = PackagePrefabs + "AudioLink/VRSL-AudioLink-Mover-Spotlight.prefab";

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

        private static GameObject Instantiate(string path, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Assert.Ignore("The VR Stage Lighting package is not in the project.");
            }

            return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        }

        private static MonoBehaviour PackageFixture(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().FullName.StartsWith("VRSL.VRStageLighting_"))
                {
                    return behaviour;
                }
            }

            return null;
        }

        [Test]
        public void Copies_HaveTheGuidTheMigrationDerives()
        {
            var guid = AssetDatabase.AssetPathToGUID(DmxMover);
            if (string.IsNullOrEmpty(guid))
            {
                Assert.Ignore("The VR Stage Lighting package is not in the project.");
            }

            Assert.AreEqual(AlpsCreateMenu.MoverSpotlight.Guid, AlpsVrslMigration.CopyGuid(guid));
        }

        [Test]
        public void MultiLightBar_IsDrivenBySegmentsAfterwards()
        {
            var rig = new GameObject("Rig");
            var old = Instantiate(DmxMultiLightBar, rig.transform);
            old.name = "Bar 1";
            var fixture = old.AddComponent<AlpsVRSLFixture>();

            // A track bound to the one fixture older versions put on a bar.
            var timeline = ScriptableObject.CreateInstance<UnityEngine.Timeline.TimelineAsset>();
            var director = new GameObject("Show").AddComponent<UnityEngine.Playables.PlayableDirector>();
            director.playableAsset = timeline;
            var track = timeline.CreateTrack<AlpsLightingTrack>(null, "Bar");
            director.SetGenericBinding(track, fixture);

            var items = AlpsVrslMigration.Find();
            Assert.AreEqual(1, items.Count);
            var converted = AlpsVrslMigration.Convert(items)[0];

            Assert.AreEqual(AlpsCreateMenu.MultiLightBar.Path, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(converted));
            Assert.IsNull(converted.GetComponent<AlpsFixture>(), "The single fixture gave way to the bar.");
            var bar = converted.GetComponent<AlpsVRSLMultiLightBar>();
            Assert.NotNull(bar);
            Assert.NotNull(bar.ResolveTarget());
            Assert.AreEqual(AlpsVRSLMultiLightBar.SegmentCount, bar.Fixtures().Count);
            Assert.AreSame(bar, director.GetGenericBinding(track), "The track drives the bar's segments now.");
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void AlpsFixture_KeepsItsObjectAndItsOverrides()
        {
            var rig = new GameObject("Rig");
            var old = Instantiate(DmxMover, rig.transform);
            old.name = "Left 1";
            var fixture = old.AddComponent<AlpsVRSLFixture>();
            var settings = new SerializedObject(PackageFixture(old));
            settings.FindProperty("coneWidth").floatValue = 4.25f;
            settings.FindProperty("invertPan").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            // A material of the package held as an override, as on a fixture given one by hand.
            var renderer = old.GetComponentInChildren<Renderer>(true);
            renderer.sharedMaterial = new Material(renderer.sharedMaterial);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            // And one whose material is gone.
            var missing = old.GetComponentsInChildren<Renderer>(true)[1];
            missing.sharedMaterial = null;
            PrefabUtility.RecordPrefabInstancePropertyModifications(missing);

            var items = AlpsVrslMigration.Find();
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(AlpsVrslMigrationSource.AlpsFixture, items[0].Source);
            Assert.IsTrue(items[0].KeepsObject, "An instance of the prefab the copy was made from only swaps its prefab.");

            var converted = AlpsVrslMigration.Convert(items);

            Assert.AreSame(old, converted[0]);
            Assert.AreEqual("Left 1", old.name);
            Assert.AreSame(fixture, old.GetComponent<AlpsVRSLFixture>(), "The ALPS fixture is the same component, so tracks stay bound.");
            Assert.AreEqual(AlpsCreateMenu.MoverSpotlight.Path, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old));
            Assert.IsNull(PackageFixture(old), "The package's script is gone.");
            foreach (var drawn in old.GetComponentsInChildren<Renderer>(true))
            {
                Assert.NotNull(drawn.sharedMaterial, $"{drawn.name} has no material.");
                StringAssert.StartsWith("ALPS/VRSL/", drawn.sharedMaterial.shader.name, $"{drawn.name} still draws with the package's shader.");
            }
            var target = fixture.ResolveTarget();
            Assert.NotNull(target);
            Assert.AreEqual(4.25f, target.coneWidth, 1e-4f);
            Assert.IsTrue(target.invertPan);
            foreach (var modification in PrefabUtility.GetPropertyModifications(old))
            {
                Assert.AreEqual(AlpsCreateMenu.MoverSpotlight.Path, AssetDatabase.GetAssetPath(modification.target),
                    $"The override of {modification.propertyPath} still names the package's prefab.");
            }

            Assert.IsEmpty(AlpsVrslMigration.Find(), "Nothing is left to convert.");
        }

        [TestCase("DMX/Horizontal Mode/VRSL-DMX-Static-Flasher-H-1CH.prefab", "Flasher")]
        [TestCase("DMX/Horizontal Mode/5-Channel Statics/VRSL-DMX-Static-6x4StrobeLight-H-5CH.prefab", "Strobe Panel")]
        [TestCase("DMX/Horizontal Mode/VRSL-DMX-Static-DiscoBall-H-1CH.prefab", "Mirror Ball")]
        public void StaticFixture_SwapsToTheCopyOfItsPrefab(string path, string fixture)
        {
            var expected = AlpsCreateMenu.Fixtures.First(candidate => candidate.Name == fixture);
            var rig = new GameObject("Rig");
            var old = Instantiate(PackagePrefabs + path, rig.transform);
            var alps = old.AddComponent<AlpsVRSLFixture>();
            Assert.AreEqual(expected.Guid, AlpsVrslMigration.CopyGuid(AssetDatabase.AssetPathToGUID(PackagePrefabs + path)));

            var items = AlpsVrslMigration.Find();
            Assert.AreEqual(1, items.Count);
            Assert.AreSame(expected, items[0].Target);
            Assert.IsTrue(items[0].KeepsObject, "An instance of the prefab the copy was made from only swaps its prefab.");

            var converted = AlpsVrslMigration.Convert(items);

            Assert.AreSame(old, converted[0]);
            Assert.AreSame(alps, old.GetComponent<AlpsVRSLFixture>());
            Assert.AreEqual(expected.Path, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old));
            Assert.IsTrue(alps.IsReady);
            Assert.IsEmpty(AlpsVrslMigration.Find(), "Nothing is left to convert.");
        }

        [TestCase("AudioLink/VRSL-AudioLink-Static-Flasher.prefab", "Flasher")]
        [TestCase("AudioLink/VRSL-AudioLink-DiscoBall.prefab", "Mirror Ball")]
        public void AudioLinkStaticFixture_BecomesItsDmxCopy(string path, string fixture)
        {
            var expected = AlpsCreateMenu.Fixtures.First(candidate => candidate.Name == fixture);
            Instantiate(PackagePrefabs + path, new GameObject("Rig").transform);

            var items = AlpsVrslMigration.Find();
            Assert.AreEqual(1, items.Count);
            Assert.AreSame(expected, items[0].Target);
            Assert.AreEqual(AlpsVrslMigrationSource.AudioLinkFixture, items[0].Source);

            var created = AlpsVrslMigration.Convert(items)[0];

            Assert.AreEqual(expected.Path, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(created));
            Assert.IsTrue(created.GetComponent<AlpsVRSLFixture>().IsReady);
        }

        [Test]
        public void AudioLinkFixture_IsReplacedWhereItStands()
        {
            var rig = new GameObject("Rig");
            var before = new GameObject("Before");
            before.transform.SetParent(rig.transform);
            var old = Instantiate(AudioLinkMover, rig.transform);
            old.name = "Spot";
            old.transform.localPosition = new Vector3(1f, 2f, 3f);

            var items = AlpsVrslMigration.Find();
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(AlpsVrslMigrationSource.AudioLinkFixture, items[0].Source);
            Assert.IsFalse(items[0].KeepsObject);

            var created = AlpsVrslMigration.Convert(items)[0];

            Assert.IsTrue(old == null, "The AudioLink fixture is removed.");
            Assert.AreSame(rig.transform, created.transform.parent);
            Assert.AreEqual(1, created.transform.GetSiblingIndex());
            Assert.AreEqual("Spot", created.name);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), created.transform.localPosition);
            var prefab = AlpsCreateMenu.MoverSpotlight.Load();
            Assert.Less(Quaternion.Angle(prefab.transform.localRotation, created.transform.localRotation), 0.01f,
                "A fixture at its prefab's own rotation ends at the new prefab's own rotation.");
            Assert.AreEqual(AlpsCreateMenu.MoverSpotlight.Path, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(created));
            var fixture = created.GetComponent<AlpsVRSLFixture>();
            Assert.NotNull(fixture);
            Assert.IsTrue(fixture.IsReady);
        }
    }
}
