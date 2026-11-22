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
using AdzukiSoft.ALPS.Vrsl;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// VRSL's projections drawn as added light on the fixtures ALPS comes with
    /// (<c>scripts/vendor-vrsl.py</c>), against the material of a VRSL package: the
    /// shader copies made from VRSL's source, the materials, and two movers whose spots
    /// overlap on VRChat's default floor, previewed before and after. The renders are written
    /// to TestResults~ to look at.
    /// </summary>
    public class AlpsProjectionLightTests
    {
        private const string Folder = "Assets/AlpsTestFixtures";
        private const string TimelinePath = Folder + "/ProjectionLight.playable";
        private const string DefaultWorldScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        private const string StockMaterial = "Packages/com.acchosen.vr-stage-lighting/Runtime/Materials/Lights/DMX/MoverLight - Spotlight/VRSL-StandardMover-ProjectionMat-HQ-Horizontal.mat";
        private const int Width = 640;
        private const int Height = 480;

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
        public void AlpsMaterials_AddTheProjectionsAsLightInThePreview()
        {
            var stock = AssetDatabase.LoadAssetAtPath<Material>(StockMaterial);
            if (stock == null)
            {
                Assert.Ignore("The VR Stage Lighting package is not in the project.");
            }

            BuildScene(out var cameras, out var director, out var projections);
            var light = projections[0].sharedMaterial;
            foreach (var projection in projections)
            {
                projection.sharedMaterial = stock;
            }

            director.time = 1.0;
            director.Evaluate();

            var dark = Render(cameras[1], projections, false, false, null);
            var stockA = Render(cameras[1], projections, true, false, null);
            var stockB = Render(cameras[1], projections, false, true, null);
            var stockBoth = Render(cameras[1], projections, true, true, "projection-light-stock.png");
            Render(cameras[0], projections, true, true, "projection-light-stock-side.png");

            foreach (var projection in projections)
            {
                projection.sharedMaterial = light;
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
        /// Two movers 3 m above VRChat's default floor, 0.6 m apart and pointing down at 42%
        /// brightness, 15% of their light, so their spots overlap without either being blown out. Cameras from the
        /// side and from above.
        /// </summary>
        private static Scene BuildScene(out Camera[] cameras, out PlayableDirector director, out Renderer[] projections)
        {
            var scene = EditorSceneManager.OpenScene(DefaultWorldScene, OpenSceneMode.Single);
            DynamicGI.UpdateEnvironment();
            cameras = new[] { MakeCamera(new Vector3(0f, 3f, -9f), new Vector3(0f, 1.5f, 0f)), MakeCamera(new Vector3(0f, 9f, -2f), Vector3.zero) };

            var prefab = AlpsCreateMenu.MoverSpotlight.Load();
            Assert.NotNull(prefab, AlpsCreateMenu.MoverSpotlight.Path);
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
            set.Add(AlpsEffectKind.Brightness).brightness.value = 42f;
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
