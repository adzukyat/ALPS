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
                StringAssert.StartsWith("ALPS/VRSL/", drawn.sharedMaterial.shader.name, $"{drawn.name} still draws with the package's shader.");
            }
            var target = fixture.ResolveTarget();
            Assert.NotNull(target);
            Assert.AreEqual(4.25f, target.coneWidth, 1e-4f);
            Assert.IsTrue(target.invertPan);
            Assert.IsEmpty(AlpsVrslMigration.Find(), "Nothing is left to convert.");
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
