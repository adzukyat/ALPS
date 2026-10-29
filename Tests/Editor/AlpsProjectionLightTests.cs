using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using VRSL;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// VRSL's projections drawn as added light (<see cref="AlpsProjectionLight"/>) on the
    /// materials ALPS gives the fixtures it adds (<see cref="AlpsFixtureMaterials"/>): the
    /// shader copies made from VRSL's source, the materials, and two movers whose spots
    /// overlap on VRChat's default floor, previewed before and after. The renders are written
    /// to TestResults~ to look at.
    /// </summary>
    public class AlpsProjectionLightTests
    {
        private const string Folder = "Assets/AlpsTestFixtures";
        private const string TimelinePath = Folder + "/ProjectionLight.playable";
        private const string DefaultWorldScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        private const string MoverPrefab = "Packages/com.acchosen.vr-stage-lighting/Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Mover-Spotlight-H-13CH.prefab";
        private const int Width = 640;
        private const int Height = 480;

        private static readonly string[] ProjectionShaders = { "VRSL/Standard Mover/Projection", "VRSL/Wash Mover/Projection", "VRSL/Standard Static/Projection" };

        private bool _asyncCompilation;

        [SetUp]
        public void RequireGraphics()
        {
            AlpsGpuShow.RequireGraphics();

            // An edit mode render skips what is still compiling.
            _asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
        }

        [TearDown]
        public void CloseScene()
        {
            ShaderUtil.allowAsyncCompilation = _asyncCompilation;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(TimelinePath);
            if (AssetDatabase.IsValidFolder(AlpsShowSetup.GeneratedFolder))
            {
                AssetDatabase.DeleteAsset(AlpsShowSetup.GeneratedFolder);
            }
        }

        [Test]
        public void Variant_BlendsVrslsProjectionsAsLight()
        {
            foreach (var name in ProjectionShaders)
            {
                var source = Shader.Find(name);
                Assert.NotNull(source, name);
                var variant = AlpsProjectionLight.Variant(source, true);
                Assert.NotNull(variant, name);
                Assert.AreEqual("Hidden/ALPS/" + name, variant.name);
                Assert.IsFalse(ShaderUtil.ShaderHasError(variant), name);
                Assert.IsTrue(AlpsProjectionLight.IsProjection(variant), "The copy still counts as a projection.");
                Assert.IsFalse(AlpsProjectionLight.IsVrslProjection(variant));

                var text = File.ReadAllText(AssetDatabase.GetAssetPath(variant));
                StringAssert.Contains("Blend One One", text);
                Assert.IsFalse(Regex.IsMatch(text, @"Blend\s+DstColor"), name);
                Assert.AreSame(variant, AlpsProjectionLight.Variant(source, false), "The copy written is found again.");
            }
        }

        [Test]
        public void Refresh_WritesTheCopyAgainWhenVrslsShaderIsImported()
        {
            var source = Shader.Find("VRSL/Standard Mover/Projection");
            var variant = AlpsProjectionLight.Variant(source, true);
            var file = AssetDatabase.GetAssetPath(variant);
            var written = File.ReadAllText(file);
            File.WriteAllText(file, written + "\n// An older VRSL.");
            AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);

            AlpsProjectionLight.Refresh(new[] { "Packages/com.acchosen.vr-stage-lighting/Runtime/Shaders/Other.shader" });
            Assert.AreNotEqual(written, File.ReadAllText(file), "Another shader's import leaves the copy alone.");

            AlpsProjectionLight.Refresh(new[] { AssetDatabase.GetAssetPath(source) });
            Assert.AreEqual(written, File.ReadAllText(file), "The copy follows its VRSL source.");
        }

        [Test]
        public void AlpsMaterials_AddTheProjectionsAsLightInThePreview()
        {
            BuildScene(out var cameras, out var director, out var projections);
            director.time = 1.0;
            director.Evaluate();

            var dark = Render(cameras[1], projections, false, false, null);
            var stockA = Render(cameras[1], projections, true, false, null);
            var stockB = Render(cameras[1], projections, false, true, null);
            var stockBoth = Render(cameras[1], projections, true, true, "projection-light-stock.png");
            Render(cameras[0], projections, true, true, "projection-light-stock-side.png");

            foreach (var projection in projections)
            {
                AlpsFixtureMaterials.Apply(projection.GetComponentInParent<AlpsVRSLFixture>().gameObject, undo: false);
            }

            director.Evaluate();

            var lightA = Render(cameras[1], projections, true, false, null);
            var lightB = Render(cameras[1], projections, false, true, null);
            var lightBoth = Render(cameras[1], projections, true, true, "projection-light-added.png");
            Render(cameras[0], projections, true, true, "projection-light-added-side.png");

            float stockAdded = 0f, lightAdded = 0f;
            for (var i = 0; i < dark.Length; i++)
            {
                stockAdded += Luminance(stockA[i]) - Luminance(dark[i]);
                lightAdded += Luminance(lightA[i]) - Luminance(dark[i]);
            }

            var stockOverlap = Overlap(dark, stockA, stockB, stockBoth);
            var lightOverlap = Overlap(dark, lightA, lightB, lightBoth);
            var report = $"one projection adds x{lightAdded / stockAdded:0.000} the light it did; " +
                $"where two overlap, x{stockOverlap:0.000} the brighter one before, x{lightOverlap:0.000} after";
            Debug.Log("[ALPS Projection Light] " + report);
            Assert.AreEqual(1f, lightAdded / stockAdded, 0.1f, "One projection on VRChat's default floor looks the way it did.\n" + report);
            Assert.Greater(stockOverlap, 4f, "VRSL's projections multiply where they overlap.\n" + report);
            Assert.LessOrEqual(lightOverlap, 2f, "Overlapping projections add up.\n" + report);
        }

        [Test]
        public void For_MakesOneMaterialPerVrslMaterialAndFindsItAgain()
        {
            BuildScene(out _, out _, out var projections);
            var original = projections[0].sharedMaterial;
            var source = original.shader;
            var intensity = original.GetFloat("_ProjectionIntensity");
            var queue = original.renderQueue;

            var light = AlpsFixtureMaterials.For(original);

            Assert.AreNotSame(original, light);
            Assert.IsTrue(EditorUtility.IsPersistent(light), "The material is an asset, so the preview and the build draw the same.");
            Assert.AreSame(AlpsProjectionLight.Variant(source, false), light.shader);
            Assert.AreEqual(intensity * AlpsProjectionLight.IntensityScale, light.GetFloat("_ProjectionIntensity"), 1e-5f);
            Assert.AreEqual(queue, light.renderQueue, "The copy keeps VRSL's queue.");
            Assert.AreSame(source, original.shader, "VRSL's own material is left alone.");
            Assert.AreEqual(intensity, original.GetFloat("_ProjectionIntensity"));
            Assert.AreSame(light, AlpsFixtureMaterials.For(original), "The next fixture finds the same material.");
            Assert.AreSame(light, AlpsFixtureMaterials.For(light), "ALPS's material stays as it is.");

            light.SetFloat("_ProjectionIntensity", 0.42f);
            Assert.AreEqual(0.42f, AlpsFixtureMaterials.For(original).GetFloat("_ProjectionIntensity"), "A value tuned on ALPS's material stays.");

            var inScene = new Material(original);
            Assert.AreSame(inScene, AlpsFixtureMaterials.For(inScene), "A material that is no asset stays as it is.");
            var dithered = new Material(original);
            dithered.EnableKeyword("_ALPHATEST_ON");
            AlpsShowSetup.EnsureFolder(Folder);
            AssetDatabase.CreateAsset(dithered, Folder + "/Dithered.mat");
            try
            {
                Assert.AreSame(dithered, AlpsFixtureMaterials.For(dithered), "A dithered projection replaces the colour drawn, so it stays VRSL's.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Folder + "/Dithered.mat");
            }
        }

        [Test]
        public void Build_LeavesVrslsProjectionsAsTheyAre()
        {
            var scene = BuildScene(out _, out _, out var projections);
            var stock = projections[0].sharedMaterial;
            var errors = new List<string>();

            try
            {
                Assert.IsTrue(AlpsShowApplier.Apply(scene, generateTimelines: true, errors), string.Join("\n", errors));
                Assert.AreSame(stock.shader, projections[0].sharedMaterial.shader, "A fixture ALPS did not add keeps how VRSL blends it.");
                Assert.AreEqual(stock.GetFloat("_ProjectionIntensity"), projections[0].sharedMaterial.GetFloat("_ProjectionIntensity"));
            }
            finally
            {
                AlpsBuildTimeline.DeleteAll();
            }
        }

        /// <summary>The most two overlapping projections give over the brighter one alone.</summary>
        private static float Overlap(Color[] dark, Color[] a, Color[] b, Color[] both)
        {
            var most = 0f;
            for (var i = 0; i < dark.Length; i++)
            {
                if (Luminance(a[i]) - Luminance(dark[i]) > 0.01f && Luminance(b[i]) - Luminance(dark[i]) > 0.01f)
                {
                    most = Mathf.Max(most, Luminance(both[i]) / Mathf.Max(Luminance(a[i]), Luminance(b[i])));
                }
            }

            return most;
        }

        private static float Luminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }

        private static Color[] Render(Camera camera, Renderer[] projections, bool a, bool b, string file)
        {
            projections[0].enabled = a;
            projections[1].enabled = b;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            camera.targetTexture = target;
            camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            var pixels = image.GetPixels();
            if (file != null)
            {
                var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                var shown = new Color[pixels.Length];
                for (var i = 0; i < pixels.Length; i++)
                {
                    shown[i] = pixels[i].gamma;
                }

                png.SetPixels(shown);
                File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "TestResults~", file), png.EncodeToPNG());
                Object.DestroyImmediate(png);
            }

            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
            return pixels;
        }

        /// <summary>
        /// Two movers 3 m above VRChat's default floor, 0.6 m apart and pointing down at 15%
        /// brightness, so their spots overlap without either being blown out. Cameras from the
        /// side and from above.
        /// </summary>
        private static Scene BuildScene(out Camera[] cameras, out PlayableDirector director, out Renderer[] projections)
        {
            var scene = EditorSceneManager.OpenScene(DefaultWorldScene, OpenSceneMode.Single);
            DynamicGI.UpdateEnvironment();
            cameras = new[] { MakeCamera(new Vector3(0f, 3f, -9f), new Vector3(0f, 1.5f, 0f)), MakeCamera(new Vector3(0f, 9f, -2f), Vector3.zero) };

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoverPrefab);
            Assert.NotNull(prefab, MoverPrefab);
            var container = new GameObject("Container").AddComponent<AlpsContainer>();
            projections = new Renderer[2];
            for (var m = 0; m < 2; m++)
            {
                var mover = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                mover.transform.SetParent(container.transform, false);
                mover.transform.localPosition = new Vector3(m == 0 ? -0.3f : 0.3f, 3f, 0f);
                mover.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                var vrsl = mover.GetComponentInChildren<VRStageLighting_DMX_Static>();
                mover.AddComponent<AlpsVRSLFixture>().target = vrsl;

                foreach (var renderer in vrsl.objRenderers)
                {
                    var shader = renderer.sharedMaterial.shader.name;
                    if (shader.EndsWith("/Projection"))
                    {
                        projections[m] = renderer;
                    }
                    else if (shader.EndsWith("/Volumetric"))
                    {
                        renderer.enabled = false;
                    }
                }

                Assert.NotNull(projections[m], "The mover has no projection renderer.");
            }

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", Folder.Substring("Assets/".Length));
            }

            AssetDatabase.DeleteAsset(TimelinePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            var track = timeline.CreateTrack<AlpsLightingTrack>(null, "Track");
            var clip = track.CreateClip<AlpsLightingClip>();
            clip.start = 0.0;
            clip.duration = 10.0;
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Move).tilt.value = 90f;
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
            set.Add(AlpsEffectKind.Brightness).brightness.value = 15f;
            set.Add(AlpsEffectKind.Cone).coneWidth.value = 60f;
            ((AlpsLightingClip)clip.asset).data = set;
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            director = new GameObject("Director").AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.playableAsset = timeline;
            director.SetGenericBinding(track, container);
            director.RebuildGraph();
            return scene;
        }

        private static Camera MakeCamera(Vector3 position, Vector3 target)
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.LookAt(target);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.allowHDR = true;
            return camera;
        }
    }
}
