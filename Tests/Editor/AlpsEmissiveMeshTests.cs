using System.Collections.Generic;
using AdzukiSoft.ALPS.Editor;
using AdzukiSoft.ALPS.Vrsl;
using NUnit.Framework;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// A mesh of the world's own made to glow as a fixture: what the conversion puts on it,
    /// which meshes an object lights, and that a show lights it the way it lights the face
    /// of a fixture.
    /// </summary>
    public class AlpsEmissiveMeshTests
    {
        /// <summary>What a white face shines at 100%, in units of white, as on every fixture.</summary>
        private const float Face = 4f;
        private const float Distance = 4f;
        private const int Size = 256;

        private readonly List<Object> _created = new List<Object>();
        private bool _asyncCompilation;

        [SetUp]
        public void OpenScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // An edit mode render skips what is still compiling.
            _asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
        }

        [TearDown]
        public void CloseScene()
        {
            ShaderUtil.allowAsyncCompilation = _asyncCompilation;
            RenderTexture.active = null;
            foreach (var created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
            AlpsPreviewDriver.MarkDirty();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void TheMaterial_ReadsTheRowOfAFixture()
        {
            var material = AlpsEmissiveMesh.LoadMaterial();

            Assert.NotNull(material, AlpsEmissiveMesh.MaterialPath);
            Assert.AreEqual(AlpsEmissiveMesh.MaterialPath, AssetDatabase.GUIDToAssetPath(AlpsEmissiveMesh.MaterialGuid), "The material's GUID and path disagree.");
            StringAssert.StartsWith("ALPS/VRSL/", material.shader.name);
            Assert.IsTrue(material.IsKeywordEnabled("_13CH_MODE"), "The material does not read the thirteen channels ALPS lays a row out as.");
            Assert.IsFalse(material.IsKeywordEnabled("_5CH_MODE"));
        }

        [Test]
        public void Convert_MakesAMeshAFixtureAShowDrives()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = cube.GetComponent<MeshRenderer>();
            Assert.IsTrue(AlpsEmissiveMesh.CanConvert(cube));

            var fixture = AlpsEmissiveMesh.Convert(cube);

            Assert.NotNull(fixture);
            Assert.AreSame(cube, fixture.gameObject);
            Assert.IsTrue(fixture.IsReady, "The fixture finds its VRSL target.");
            var light = cube.GetComponent<VRStageLighting_DMX_Static>();
            Assert.AreSame(light, fixture.target);
            CollectionAssert.AreEqual(new[] { renderer }, light.objRenderers);
            Assert.NotNull(UdonSharpEditorUtility.GetBackingUdonBehaviour(light), "The script needs its Udon behaviour to set the row in a world.");
            Assert.AreSame(AlpsEmissiveMesh.LoadMaterial(), renderer.sharedMaterial);

            Assert.IsFalse(AlpsEmissiveMesh.CanConvert(cube), "It is a fixture now.");
            Assert.IsNull(AlpsEmissiveMesh.Convert(cube));
        }

        [Test]
        public void Convert_FillsEveryMaterialSlot()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = cube.GetComponent<MeshRenderer>();
            var first = Keep(new Material(Shader.Find("Standard")));
            var second = Keep(new Material(Shader.Find("Standard")));
            renderer.sharedMaterials = new[] { first, second };

            AlpsEmissiveMesh.Convert(cube);

            var material = AlpsEmissiveMesh.LoadMaterial();
            CollectionAssert.AreEqual(new[] { material, material }, renderer.sharedMaterials);
        }

        [Test]
        public void AnObject_LightsTheMeshesBelowItButNotAnotherFixtures()
        {
            var wall = new GameObject("Wall");
            var left = Child(GameObject.CreatePrimitive(PrimitiveType.Cube), wall);
            var group = Child(new GameObject("Group"), wall);
            var right = Child(GameObject.CreatePrimitive(PrimitiveType.Cube), group);
            var par = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.ParLight, wall);
            var nested = AlpsCreateMenu.CreateContainer(wall);
            var held = Child(GameObject.CreatePrimitive(PrimitiveType.Cube), nested);
            var heldMaterial = held.GetComponent<MeshRenderer>().sharedMaterial;
            var parMaterials = new List<Material>();
            foreach (var renderer in par.GetComponentsInChildren<MeshRenderer>(true))
            {
                parMaterials.Add(renderer.sharedMaterial);
            }

            var expected = new[] { left.GetComponent<MeshRenderer>(), right.GetComponent<MeshRenderer>() };
            CollectionAssert.AreEqual(expected, AlpsEmissiveMesh.Renderers(wall));

            var fixture = AlpsEmissiveMesh.Convert(wall);

            Assert.NotNull(fixture, "An object without a mesh of its own lights the ones below it.");
            CollectionAssert.AreEqual(expected, fixture.target.objRenderers);
            Assert.AreSame(heldMaterial, held.GetComponent<MeshRenderer>().sharedMaterial, "The mesh in the container below is that container's.");
            var after = new List<Material>();
            foreach (var renderer in par.GetComponentsInChildren<MeshRenderer>(true))
            {
                after.Add(renderer.sharedMaterial);
            }

            CollectionAssert.AreEqual(parMaterials, after, "The par light below keeps its materials.");
            Assert.IsFalse(AlpsEmissiveMesh.CanConvert(left), "The wall lights it already.");
        }

        [Test]
        public void CanConvert_LeavesOutWhatIsDrivenAlreadyAndWhatHasNoMesh()
        {
            var container = AlpsCreateMenu.CreateContainer(null);
            var par = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.ParLight, container);
            var inContainer = Child(GameObject.CreatePrimitive(PrimitiveType.Cube), container);

            Assert.IsFalse(AlpsEmissiveMesh.CanConvert(null));
            Assert.IsFalse(AlpsEmissiveMesh.CanConvert(new GameObject("Empty")));
            Assert.IsFalse(AlpsEmissiveMesh.CanConvert(container), "A container holds fixtures and is none.");
            Assert.IsFalse(AlpsEmissiveMesh.CanConvert(par));
            var body = par.GetComponentInChildren<MeshRenderer>(true).gameObject;
            Assert.IsFalse(AlpsEmissiveMesh.CanConvert(body), "A part of a fixture stays that fixture's.");
            Assert.IsTrue(AlpsEmissiveMesh.CanConvert(inContainer), "A mesh in a container becomes a fixture of it.");
        }

        [Test]
        public void Roots_AreTheSelectedObjectsThatBecomeAFixtureEach()
        {
            var wall = new GameObject("Wall");
            var panel = Child(GameObject.CreatePrimitive(PrimitiveType.Cube), wall);
            var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var empty = new GameObject("Empty");

            var roots = AlpsEmissiveMesh.Roots(new[] { panel, wall, sign, empty });

            CollectionAssert.AreEquivalent(new[] { wall, sign }, roots, "The wall lights its panel, and an object without a mesh is left out.");
        }

        [Test]
        public void Convert_IsTakenBackByOneUndo()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = cube.GetComponent<MeshRenderer>();
            var material = renderer.sharedMaterial;
            Undo.IncrementCurrentGroup();

            AlpsEmissiveMesh.Convert(cube);
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();

            Assert.IsNull(cube.GetComponent<AlpsVRSLFixture>());
            Assert.IsNull(cube.GetComponent<VRStageLighting_DMX_Static>());
            Assert.AreSame(material, renderer.sharedMaterial);
        }

        [Test]
        public void Create_AddsACubeThatGlowsToTheContainer()
        {
            var containerObject = AlpsCreateMenu.CreateContainer(null);
            var par = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.ParLight, containerObject);

            var created = AlpsEmissiveMesh.Create(par);

            Assert.NotNull(created);
            Assert.AreSame(containerObject.transform, created.transform.parent, "Made on a fixture, it goes next to it.");
            Assert.AreEqual(AlpsEmissiveMesh.ObjectName, created.name);
            var fixture = created.GetComponent<AlpsVRSLFixture>();
            Assert.NotNull(fixture);
            Assert.IsTrue(fixture.IsReady);
            CollectionAssert.AreEqual(
                new AlpsFixture[] { par.GetComponent<AlpsFixture>(), fixture },
                containerObject.GetComponent<AlpsContainer>().Fixtures(),
                "A track bound to the container counts it with the lights.");
        }

        [Test]
        public void AMesh_ShinesLikeTheFaceOfAFixtureAtFull()
        {
            var face = Measure(100f);

            Debug.Log($"[ALPS Emissive Mesh] at 100%: {face:0.00}");
            Assert.AreEqual(Face, face, Face * 0.1f);
        }

        [Test]
        public void AMesh_DimsByTheCurveOfTheFixtures()
        {
            var face = Measure(50f);

            // Half the level looks half as bright, which is the level to the power of 2.2 in light.
            Debug.Log($"[ALPS Emissive Mesh] at 50%: {face:0.00}");
            Assert.AreEqual(Face * Mathf.Pow(0.5f, 2.2f), face, Face * 0.02f);
        }

        [Test]
        public void AMesh_IsDarkAtNoBrightness()
        {
            Assert.AreEqual(0f, Measure(0f), 0.01f);
        }

        /// <summary>
        /// The brightest point of a cube made to glow, previewed white at a level and seen
        /// from below in a scene without any other light.
        /// </summary>
        private float Measure(float level)
        {
            AlpsGpuShow.RequireGraphics();
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;

            var container = Keep(AlpsCreateMenu.CreateContainer(null));
            var mesh = AlpsEmissiveMesh.Create(container);
            Assert.NotNull(mesh, AlpsEmissiveMesh.MaterialPath);
            mesh.transform.position = new Vector3(0f, Distance, 0f);

            var timeline = Keep(ScriptableObject.CreateInstance<TimelineAsset>());
            var director = Keep(new GameObject("Show")).AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var track = timeline.CreateTrack<AlpsLightingTrack>(null, "Rig");
            director.SetGenericBinding(track, container.GetComponent<AlpsContainer>());
            var clip = track.CreateClip<AlpsLightingClip>();
            clip.start = 0.0;
            clip.duration = 10.0;
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
            set.Add(AlpsEffectKind.Brightness).brightness.value = level;
            ((AlpsLightingClip)clip.asset).data = set;

            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.RebuildGraph();
            director.time = 1.0;
            director.Evaluate();

            var camera = Keep(new GameObject("Camera")).AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = true;
            camera.fieldOfView = 20f;
            camera.nearClipPlane = 0.05f;
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            var target = Keep(new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var image = Keep(new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true));
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;

            var face = 0f;
            foreach (var pixel in image.GetPixels())
            {
                face = Mathf.Max(face, pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f);
            }

            return face;
        }

        private static GameObject Child(GameObject gameObject, GameObject parent)
        {
            gameObject.transform.SetParent(parent.transform, false);
            return gameObject;
        }

        private T Keep<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }
    }
}
