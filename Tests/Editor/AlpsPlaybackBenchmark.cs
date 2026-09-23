using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using VRC.Udon;
using VRSL;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Times the compiled Udon player in play mode on real VRSL movers, and renders a moment
    /// of a show to look at. Explicit, since these enter play mode and only report. Results
    /// are logged with an [ALPS Bench] prefix and written to TestResults~.
    /// </summary>
    [Explicit]
    public class AlpsPlaybackBenchmark
    {
        private const string Folder = "Assets/AlpsTestFixtures/Baked";
        private const string ScenePath = Folder + "/Benchmark.unity";
        private const string TimelinePath = Folder + "/Benchmark.playable";
        private const string MoverPrefab = "Packages/com.acchosen.vr-stage-lighting/Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Mover-Spotlight-H-13CH.prefab";
        private const int Containers = 3;
        private const int FixturesPerContainer = 16;
        private const int Frames = 300;
        private const float FrameSeconds = 1f / 90f;

        [UnityTest]
        public IEnumerator Benchmark_PlayerFrameCost()
        {
            if (!Application.isPlaying)
            {
                BuildScene();
                yield return new EnterPlayMode();
            }

            LogAssert.ignoreFailingMessages = true;
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            Measure();
            yield return new ExitPlayMode();
        }

        private const float CaptureTime = 13.37f;

        /// <summary>Renders a moment of a small show into TestResults~/capture.png, to look at by eye.</summary>
        [UnityTest]
        public IEnumerator Capture()
        {
            if (!Application.isPlaying)
            {
                BuildCaptureScene();
                yield return new EnterPlayMode();
            }

            LogAssert.ignoreFailingMessages = true;
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            RenderCapture("capture.png");
            yield return new ExitPlayMode();
        }

        private static void RenderCapture(string file)
        {
            var player = Object.FindObjectsOfType<UdonBehaviour>().FirstOrDefault(b => b.gameObject.name == AlpsShowSetup.PlayerName);
            Assert.NotNull(player, "No player in play mode.");
            var director = Object.FindObjectOfType<PlayableDirector>();
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.time = CaptureTime;
            player.RunProgram("_update");

            var camera = Object.FindObjectOfType<Camera>();
            // VRSL's volumetric cones fade out where they meet the scene, read from the depth texture.
            camera.depthTextureMode = DepthTextureMode.Depth;

            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "TestResults~", file), image.EncodeToPNG());

            Object.Destroy(image);
            Object.Destroy(target);
        }

        private static void BuildCaptureScene()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/AlpsTestFixtures", "Baked");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(TimelinePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);

            var director = new GameObject("Director").AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 4f, -12f);
            camera.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(4f, 1f, 4f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoverPrefab);
            var container = new GameObject("Container").AddComponent<AlpsContainer>();
            for (var i = 0; i < 8; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(container.transform, false);
                instance.transform.localPosition = new Vector3(-7f + i * 2f, 6f, 0f);
                instance.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                instance.AddComponent<AlpsVRSLFixture>().target = instance.GetComponentInChildren<VRStageLighting_DMX_Static>();
            }

            var track = timeline.CreateTrack<AlpsTimelineTrack>(null, "Track");
            var clip = track.CreateClip<AlpsTimelineClip>();
            clip.start = 0.0;
            clip.duration = 60.0;
            var set = MovingSet(0);
            set.effects.First(e => e.kind == AlpsEffectKind.Gobo).goboStops[0] = new AlpsGoboStop(4);

            // Each fixture a longer cone, from a fifth of its mesh to twice it.
            var cone = set.effects.First(e => e.kind == AlpsEffectKind.Cone).coneLength;
            cone.hasSpread = true;
            cone.spreadRange = new Vector2(10f, 100f);
            cone.spreadRangeEnd = cone.spreadRange;
            ((AlpsTimelineClip)clip.asset).data = set;
            director.SetGenericBinding(track, container);

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>
        /// Kept out of the test's iterator: entering play mode reloads the domain, and the
        /// iterator comes back without the closure its lambdas would capture into.
        /// </summary>
        private static void Measure()
        {
            var behaviours = Object.FindObjectsOfType<UdonBehaviour>();
            var player = behaviours.FirstOrDefault(b => b.gameObject.name == AlpsShowSetup.PlayerName);
            Assert.NotNull(player, "No player in play mode. Udon behaviours: " + string.Join(", ", behaviours.Select(b => b.gameObject.name)));
            var director = Object.FindObjectOfType<PlayableDirector>();
            Assert.NotNull(director, "No director in play mode.");
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            var grid = (RenderTexture)player.GetProgramVariable("grid");
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);

            var start = 10f;
            double Run(bool render, bool wait)
            {
                var watch = new Stopwatch();
                for (var i = 0; i < 30 + Frames; i++)
                {
                    if (i == 30)
                    {
                        watch.Start();
                    }

                    if (render)
                    {
                        director.time = start + i * FrameSeconds;
                        player.RunProgram("_update");
                    }

                    if (wait)
                    {
                        // Reading a texel back waits until the GPU has drawn the grid.
                        var previous = RenderTexture.active;
                        RenderTexture.active = grid;
                        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        RenderTexture.active = previous;
                    }
                }

                watch.Stop();
                start += 20f;
                return watch.Elapsed.TotalMilliseconds / Frames;
            }

            var cpu = Run(true, false);
            var waited = Run(true, true);
            var waitOnly = Run(false, true);
            Object.Destroy(readback);

            var report =
                $"[ALPS Bench] {Containers * FixturesPerContainer} VRSL movers\n" +
                $"[ALPS Bench] player frame on the CPU:          {cpu:0.000} ms\n" +
                $"[ALPS Bench] until the GPU has drawn the grid: {waited - waitOnly:0.000} ms (readback alone {waitOnly:0.000} ms)";
            Debug.Log(report);
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "TestResults~", "benchmark.txt"), report);
        }

        private static void BuildScene()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/AlpsTestFixtures", "Baked");
            }

            // A new scene unloads unreferenced assets, so the timeline comes after it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(TimelinePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            var director = new GameObject("Director").AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoverPrefab);
            Assert.NotNull(prefab, MoverPrefab);

            var all = new GameObject("All").AddComponent<AlpsContainer>();
            for (var c = 0; c < Containers; c++)
            {
                var container = new GameObject("Container " + c).AddComponent<AlpsContainer>();
                container.transform.SetParent(all.transform, false);
                for (var i = 0; i < FixturesPerContainer; i++)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.transform.SetParent(container.transform, false);
                    instance.transform.localPosition = new Vector3(i, c * 2f, 0f);
                    var vrslFixture = instance.GetComponentInChildren<VRStageLighting_DMX_Static>();
                    var alps = instance.AddComponent<AlpsVRSLFixture>();
                    alps.target = vrslFixture;
                }

                var track = timeline.CreateTrack<AlpsTimelineTrack>(null, "Track " + c);
                var clip = track.CreateClip<AlpsTimelineClip>();
                clip.start = 0.0;
                clip.duration = 120.0;
                ((AlpsTimelineClip)clip.asset).data = MovingSet(c);
                director.SetGenericBinding(track, container);
            }

            var accentTrack = timeline.CreateTrack<AlpsTimelineTrack>(null, "Accent");
            for (var i = 0; i < 12; i++)
            {
                var accent = accentTrack.CreateClip<AlpsTimelineClip>();
                accent.start = i * 10.0;
                accent.duration = 8.0;
                var set = new AlpsClipEffectSet();
                set.order = AlpsOrderMode.Symmetric;
                set.phase.spread = 1f;
                var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
                brightness.isRange = true;
                brightness.range = new Vector2(0f, 100f);
                set.Add(AlpsEffectKind.Flicker);
                ((AlpsTimelineClip)accent.asset).data = set;
            }

            director.SetGenericBinding(accentTrack, all);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static AlpsClipEffectSet MovingSet(int index)
        {
            var set = new AlpsClipEffectSet();
            set.phase.beatsPerCycle = 4f;
            set.phase.spread = 0.5f;
            set.order = (AlpsOrderMode)(index % 4);

            var move = set.Add(AlpsEffectKind.Move);
            move.pan.isRange = true;
            move.pan.range = new Vector2(-60f, 60f);
            move.tilt.isRange = true;
            move.tilt.range = new Vector2(30f, 90f);

            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(new AlpsColorStop(Color.green));
            color.colorStops.Add(new AlpsColorStop(Color.blue));

            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(30f, 100f);

            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.value = 30f;

            var gobo = set.Add(AlpsEffectKind.Gobo);
            gobo.goboStops.Add(new AlpsGoboStop(3));
            gobo.goboRotationBeats = 4f;
            return set;
        }
    }
}
