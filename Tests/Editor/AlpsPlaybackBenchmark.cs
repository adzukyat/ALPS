using System;
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
    /// Times the compiled Udon player in play mode on real VRSL movers. Explicit, since it
    /// enters play mode and only reports numbers. Results are logged with an [ALPS Bench] prefix.
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

        private const string GpuWasOnKey = "AdzukiSoft.ALPS.Benchmark.GpuWasOn";
        private const float CaptureTime = 13.37f;

        /// <summary>
        /// Renders the same moment of a small show on the CPU path and on the GPU path into
        /// TestResults~/capture-cpu.png and capture-gpu.png, to compare the two by eye.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_Cpu()
        {
            return Capture(false, "capture-cpu.png");
        }

        [UnityTest]
        public IEnumerator Capture_Gpu()
        {
            return Capture(true, "capture-gpu.png");
        }

        private static IEnumerator Capture(bool gpu, string file)
        {
            if (!Application.isPlaying)
            {
                SessionState.SetBool(GpuWasOnKey, AlpsGpuPlayback.Enabled);
                AlpsGpuPlayback.Enabled = gpu;
                BuildCaptureScene();
                yield return new EnterPlayMode();
            }

            LogAssert.ignoreFailingMessages = true;
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            try
            {
                RenderCapture(file);
            }
            finally
            {
                AlpsGpuPlayback.Enabled = SessionState.GetBool(GpuWasOnKey, false);
            }

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
            ((AlpsTimelineClip)clip.asset).data = set;
            director.SetGenericBinding(track, container);

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        [UnityTest]
        public IEnumerator Benchmark_GpuPlayerFrameCost()
        {
            if (!Application.isPlaying)
            {
                SessionState.SetBool(GpuWasOnKey, AlpsGpuPlayback.Enabled);
                AlpsGpuPlayback.Enabled = true;
                BuildScene();
                yield return new EnterPlayMode();
            }

            LogAssert.ignoreFailingMessages = true;
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            try
            {
                MeasureGpu();
            }
            finally
            {
                AlpsGpuPlayback.Enabled = SessionState.GetBool(GpuWasOnKey, false);
            }

            yield return new ExitPlayMode();
        }

        private static void MeasureGpu()
        {
            var player = Object.FindObjectsOfType<UdonBehaviour>().FirstOrDefault(b => b.gameObject.name == AlpsShowSetup.PlayerName);
            Assert.NotNull(player, "No player in play mode.");
            Assert.AreEqual(true, player.GetProgramVariable("gpu"), "The player plays on the GPU.");
            var director = Object.FindObjectOfType<PlayableDirector>();
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            var grid = (RenderTexture)player.GetProgramVariable("gpuGrid");
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
                $"[ALPS Bench] GPU playback, {Containers * FixturesPerContainer} fixtures\n" +
                $"[ALPS Bench] player frame on the CPU:          {cpu:0.000} ms\n" +
                $"[ALPS Bench] until the GPU has drawn the grid: {waited - waitOnly:0.000} ms (readback alone {waitOnly:0.000} ms)";
            Debug.Log(report);
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "TestResults~", "benchmark-gpu.txt"), report);
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
            var vrsl = behaviours.Where(b => b != player).ToArray();

            var start = 10f;
            double Run(Action<float> perFrame)
            {
                // Warm up, then time.
                for (var i = 0; i < 30; i++)
                {
                    perFrame(start + i * FrameSeconds);
                }

                var watch = Stopwatch.StartNew();
                for (var i = 0; i < Frames; i++)
                {
                    perFrame(start + (30 + i) * FrameSeconds);
                }

                watch.Stop();
                start += 20f;
                return watch.Elapsed.TotalMilliseconds / Frames;
            }

            void Frame(float time)
            {
                director.time = time;
                player.RunProgram("_update");
            }

            var full = Run(Frame);
            var paused = Run(_ =>
            {
                director.time = 5f;
                player.RunProgram("_update");
            });
            var vrslOnly = Run(_ =>
            {
                foreach (var fixture in vrsl)
                {
                    fixture.SendCustomEvent("_UpdateInstancedProperties");
                }
            });

            var fixtures = player.GetProgramVariable("vrslFixtures") as Array;
            player.SetProgramVariable("vrslFixtures", Array.CreateInstance(fixtures.GetType().GetElementType(), 0));
            var evaluateOnly = Run(Frame);
            player.SetProgramVariable("vrslFixtures", fixtures);

            var report =
                $"[ALPS Bench] {vrsl.Length} VRSL movers, {Containers * FixturesPerContainer} fixtures in the show\n" +
                $"[ALPS Bench] player frame, full:           {full:0.000} ms\n" +
                $"[ALPS Bench] player frame, no VRSL writes: {evaluateOnly:0.000} ms\n" +
                $"[ALPS Bench] player frame, paused:         {paused:0.000} ms\n" +
                $"[ALPS Bench] VRSL rebuild of every mover:  {vrslOnly:0.000} ms\n";

            // One clip over every fixture at a time, evaluated without VRSL writes, to see what each effect costs.
            var fixtureCount = Containers * FixturesPerContainer;
            double EvaluateShow(AlpsCompiledShow show)
            {
                player.SetProgramVariable("clips", show.clips);
                player.SetProgramVariable("effects", show.effects);
                player.SetProgramVariable("parameters", show.parameters);
                player.SetProgramVariable("colors", show.colors);
                player.SetProgramVariable("gobos", show.gobos);
                player.SetProgramVariable("userNames", show.userNames);
                player.SetProgramVariable("positions", show.positions);
                player.SetProgramVariable("bucketStart", show.bucketStart);
                player.SetProgramVariable("bucketClips", show.bucketClips);
                player.SetProgramVariable("bucketSeconds", show.bucketSeconds);
                player.SetProgramVariable("groupCount", show.groupCount);
                player.SetProgramVariable("groupIndex", show.groupIndex);
                player.SetProgramVariable("vrslFixtures", Array.CreateInstance(fixtures.GetType().GetElementType(), 0));
                player.SendCustomEvent("Initialize");
                return Run(Frame);
            }

            double Effect(Action<AlpsClipEffectSet> build)
            {
                var set = new AlpsClipEffectSet();
                set.phase.beatsPerCycle = 4f;
                set.phase.spread = 0.5f;
                build(set);
                return EvaluateShow(AlpsShowCompiler.CompileStandalone(fixtureCount, 120f, new AlpsStandaloneClip { set = set, start = 0f, end = 1000f }));
            }

            var noClips = EvaluateShow(AlpsShowCompiler.CompileStandalone(fixtureCount, 120f));
            var emptyClip = Effect(_ => { });
            var rows = new (string name, Action<AlpsClipEffectSet> build)[]
            {
                ("brightness value", s => s.Add(AlpsEffectKind.Brightness)),
                ("brightness range", s =>
                {
                    var b = s.Add(AlpsEffectKind.Brightness).brightness;
                    b.isRange = true;
                    b.range = new Vector2(0f, 100f);
                }),
                ("move angle, values", s => s.Add(AlpsEffectKind.Move)),
                ("move angle, ranges", s =>
                {
                    var m = s.Add(AlpsEffectKind.Move);
                    m.pan.isRange = true;
                    m.pan.range = new Vector2(-60f, 60f);
                    m.tilt.isRange = true;
                    m.tilt.range = new Vector2(30f, 90f);
                }),
                ("move circle", s => s.Add(AlpsEffectKind.Move).moveMode = AlpsMoveMode.Circle),
                ("color, 1 stop", s => s.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.red))),
                ("color, 3 stops", s =>
                {
                    var c = s.Add(AlpsEffectKind.Color);
                    c.colorStops.Add(new AlpsColorStop(Color.red));
                    c.colorStops.Add(new AlpsColorStop(Color.green));
                    c.colorStops.Add(new AlpsColorStop(Color.blue));
                }),
                ("cone values", s => s.Add(AlpsEffectKind.Cone)),
                ("gobo, turning", s =>
                {
                    var g = s.Add(AlpsEffectKind.Gobo);
                    g.goboStops.Add(new AlpsGoboStop(3));
                    g.goboRotationBeats = 4f;
                }),
                ("flicker", s => s.Add(AlpsEffectKind.Flicker)),
            };

            report += $"[ALPS Bench] evaluate only, no clips:     {noClips:0.000} ms\n";
            report += $"[ALPS Bench] evaluate only, empty clip:   {emptyClip:0.000} ms\n";
            foreach (var row in rows)
            {
                var cost = Effect(row.build);
                report += $"[ALPS Bench] + {row.name,-22} {cost - emptyClip:0.000} ms\n";
            }

            report += $"[ALPS Bench] moving set as one clip:      {EvaluateShow(AlpsShowCompiler.CompileStandalone(fixtureCount, 120f, new AlpsStandaloneClip { set = MovingSet(0), start = 0f, end = 1000f })):0.000} ms";
            player.SetProgramVariable("vrslFixtures", fixtures);
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
