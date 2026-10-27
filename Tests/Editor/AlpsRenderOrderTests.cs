using System.Collections.Generic;
using System.IO;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using VRSL;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// A VRSL mover projecting onto a floor and an avatar stand-in on the Player layer,
    /// rendered before and after <see cref="AlpsRenderOrder"/>. The stand-in and the floor
    /// have colours of their own, so the pixels of each are told apart in a render without
    /// the projection, and the projection's gain is read on each. The renders are written to
    /// TestResults~ to look at.
    /// </summary>
    public class AlpsRenderOrderTests
    {
        private const string Folder = "Assets/AlpsTestFixtures";
        private const string TimelinePath = Folder + "/RenderOrder.playable";
        private const string FloorMaterialPath = Folder + "/RenderOrderFloor.mat";
        private const string MoverPrefab = "Packages/com.acchosen.vr-stage-lighting/Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Mover-Spotlight-H-13CH.prefab";
        private const int Width = 640;
        private const int Height = 480;

        private static readonly Color FloorColour = new Color(0.1f, 0.1f, 0.4f);
        private static readonly Color PlayerColour = new Color(0.4f, 0.1f, 0.1f);

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
            AssetDatabase.DeleteAsset(FloorMaterialPath);
        }

        [Test]
        public void Projection_LightsTheFloorButNotThePlayer()
        {
            var scene = BuildScene(out var camera, out var director, out var projection, out var volumetric);
            director.time = 1.0;
            director.Evaluate();

            var dark = Render(camera, projection, false, "render-order-dark.png");
            var stock = Gains(dark, Render(camera, projection, true, "render-order-stock.png"));

            AlpsRenderOrder.Apply(scene);
            director.Evaluate();
            var reordered = Gains(dark, Render(camera, projection, true, "render-order-reordered.png"));

            foreach (var renderer in volumetric)
            {
                renderer.enabled = true;
            }

            Render(camera, projection, true, "render-order-reordered-cones.png");

            var report = $"stock: floor x{stock.floor:0.000}, player x{stock.player:0.000} ({stock.playerPixels} px)\n" +
                $"reordered: floor x{reordered.floor:0.000}, player x{reordered.player:0.000} ({reordered.playerPixels} px)";
            Debug.Log("[ALPS Render Order] " + report);
            Assert.Greater(stock.playerPixels, 500, "The stand-in should cover the spot.\n" + report);
            Assert.Greater(stock.player, 1.5f, "The stock order lights the player.\n" + report);
            Assert.Greater(stock.floor, 1.5f, "The stock order lights the floor.\n" + report);
            Assert.AreEqual(1f, reordered.player, 0.01f, "The player is drawn over the projection.\n" + report);
            Assert.AreEqual(stock.floor, reordered.floor, stock.floor * 0.01f, "The floor stays lit the same.\n" + report);
        }

        [Test]
        public void Apply_SavesNothingInTheProject()
        {
            var scene = BuildScene(out _, out _, out var projection, out _);
            var floor = GameObject.Find("Floor").GetComponent<Renderer>();
            AssetDatabase.CreateAsset(floor.sharedMaterial, FloorMaterialPath);
            var floorMaterial = floor.sharedMaterial;
            var projectionMaterial = projection.sharedMaterial;
            var projectionQueue = projectionMaterial.renderQueue;
            var playerMaterial = GameObject.Find("Player Stand-in").GetComponent<Renderer>().sharedMaterial;
            var materials = AssetDatabase.FindAssets("t:Material").Length;

            AlpsRenderOrder.Apply(scene);

            Assert.AreEqual(materials, AssetDatabase.FindAssets("t:Material").Length, "No material is saved.");
            Assert.AreEqual((int)RenderQueue.Geometry, floorMaterial.renderQueue, "The authoring material is left alone.");
            Assert.AreEqual(projectionQueue, projectionMaterial.renderQueue);

            Assert.IsFalse(EditorUtility.IsPersistent(floor.sharedMaterial), "The floor draws with a copy in memory.");
            Assert.Less(floor.sharedMaterial.renderQueue, AlpsRenderOrder.ProjectionQueue);
            Assert.IsFalse(EditorUtility.IsPersistent(projection.sharedMaterial));
            Assert.AreEqual(AlpsRenderOrder.ProjectionQueue, projection.sharedMaterial.renderQueue);
            Assert.AreSame(playerMaterial, GameObject.Find("Player Stand-in").GetComponent<Renderer>().sharedMaterial, "Players keep their materials.");
        }

        [Test]
        public void Remap_KeepsTheOrderBelowTheProjections()
        {
            var queues = new[] { 2000, 2450, 1000, 2001, 2000, 1949, 2500, 0, 1 };
            var remap = AlpsRenderOrder.Remap(queues);

            var sorted = new List<int>(remap.Keys);
            sorted.Sort();
            for (var i = 0; i < sorted.Count; i++)
            {
                Assert.Less(remap[sorted[i]], AlpsRenderOrder.ProjectionQueue, $"{sorted[i]} draws before the projections.");
                if (i > 0)
                {
                    Assert.Less(remap[sorted[i - 1]], remap[sorted[i]], $"{sorted[i - 1]} still draws before {sorted[i]}.");
                }
            }

            Assert.AreEqual(1000, remap[1000], "A queue low enough already stays.");
            Assert.AreEqual(0, remap[0]);
            Assert.AreEqual(1, remap[1]);
            Assert.AreEqual(AlpsRenderOrder.ProjectionQueue - 1, remap[2500], "The highest closes in right below the projections.");
        }

        [Test]
        public void Remap_LeavesAWorldBelowTheProjectionsAlone()
        {
            var queues = new[] { 1000, 1500, 1900 };
            var remap = AlpsRenderOrder.Remap(queues);

            foreach (var queue in queues)
            {
                Assert.AreEqual(queue, remap[queue]);
            }
        }

        private struct Gain
        {
            public float floor;
            public float player;
            public int playerPixels;
        }

        /// <summary>The highest gain of the projection on the floor's pixels and on the player's.</summary>
        private static Gain Gains(Color[] dark, Color[] lit)
        {
            var gain = new Gain { floor = 0f, player = 0f };
            for (var i = 0; i < dark.Length; i++)
            {
                var floor = Leans(dark[i].b, dark[i].r, dark[i].g);
                var player = Leans(dark[i].r, dark[i].g, dark[i].b);
                if (!floor && !player)
                {
                    continue;
                }

                var ratio = Luminance(lit[i]) / Luminance(dark[i]);
                if (floor)
                {
                    gain.floor = Mathf.Max(gain.floor, ratio);
                }
                else
                {
                    gain.player = Mathf.Max(gain.player, ratio);
                    gain.playerPixels++;
                }
            }

            return gain;
        }

        /// <summary>Whether a pixel glows mostly in one channel, the way only the floor or the player does.</summary>
        private static bool Leans(float main, float other, float third)
        {
            return main > 0.001f && main > other * 2f && main > third * 2f;
        }

        private static float Luminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }

        private static Color[] Render(Camera camera, Renderer projection, bool lit, string file)
        {
            projection.enabled = lit;
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
            var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var shown = new Color[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                shown[i] = pixels[i].gamma;
            }

            png.SetPixels(shown);
            File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "TestResults~", file), png.EncodeToPNG());

            Object.DestroyImmediate(png);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
            return pixels;
        }

        /// <summary>A mover 3 m above the floor pointing down onto a capsule on the Player layer.</summary>
        private static Scene BuildScene(out Camera camera, out PlayableDirector director, out Renderer projection, out Renderer[] volumetric)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;

            camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 3f, -9f);
            camera.transform.LookAt(new Vector3(0f, 2f, 0f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.allowHDR = true;

            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(50f, 20f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.GetComponent<Renderer>().sharedMaterial = Coloured(FloorColour);

            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player Stand-in";
            player.layer = 9;
            player.transform.position = new Vector3(0.3f, 0.6f, 0f);
            player.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
            player.GetComponent<Renderer>().sharedMaterial = Coloured(PlayerColour);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoverPrefab);
            Assert.NotNull(prefab, MoverPrefab);
            var container = new GameObject("Container").AddComponent<AlpsContainer>();
            var mover = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            mover.transform.SetParent(container.transform, false);
            mover.transform.localPosition = new Vector3(0f, 3f, 0f);
            mover.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            var vrsl = mover.GetComponentInChildren<VRStageLighting_DMX_Static>();
            var fixture = mover.AddComponent<AlpsVRSLFixture>();
            fixture.target = vrsl;

            projection = null;
            var cones = new List<Renderer>();
            foreach (var renderer in vrsl.objRenderers)
            {
                var shader = renderer.sharedMaterial.shader.name;
                if (shader.EndsWith("/Projection"))
                {
                    projection = renderer;
                }
                else if (shader.EndsWith("/Volumetric"))
                {
                    renderer.enabled = false;
                    cones.Add(renderer);
                }
            }

            Assert.NotNull(projection, "The mover has no projection renderer.");
            volumetric = cones.ToArray();

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
            set.Add(AlpsEffectKind.Brightness);
            set.Add(AlpsEffectKind.Cone).coneWidth.value = 20f;
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

        /// <summary>A matte material of one colour.</summary>
        private static Material Coloured(Color colour)
        {
            var material = new Material(Shader.Find("Standard"));
            material.SetColor("_Color", colour);
            material.SetFloat("_Glossiness", 0f);
            material.SetFloat("_GlossyReflections", 0f);
            material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            return material;
        }
    }
}
