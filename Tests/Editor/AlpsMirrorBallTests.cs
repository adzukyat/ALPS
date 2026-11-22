using System.Collections.Generic;
using System.IO;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// The light VRSL's mirror ball throws on VRChat's default floor, previewed: added the
    /// way the other projections add theirs, dimmed by the curve of every other fixture, and
    /// turned by the show's time. The renders are written to TestResults~ to look at, and the
    /// light it adds is logged, since how bright it is was set without measuring it
    /// (<c>scripts/vendor-vrsl.py</c>).
    /// </summary>
    public class AlpsMirrorBallTests
    {
        private const string DefaultWorldScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        private const int Width = 640;
        private const int Height = 480;

        // The ball holds still at 100% and then at 50%, and turns on its own after that.
        private const float Still = 10f;
        private const float Half = 30f;
        private const float Turning = 50f;

        private readonly List<Object> _created = new List<Object>();
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
        public void TheBallsLight_DimsByTheCurveOfEveryFixture()
        {
            var light = BuildScene(out var camera, out var director);

            var dark = Render(camera, director, light, Still, false, null);
            var full = Render(camera, director, light, Still, true, "mirror-ball-100.png");
            var half = Render(camera, director, light, Half, true, "mirror-ball-50.png");

            float fullAdded = 0f, halfAdded = 0f, brightest = 0f;
            var lit = 0;
            for (var i = 0; i < dark.Length; i++)
            {
                var added = Luminance(full[i]) - Luminance(dark[i]);
                fullAdded += added;
                halfAdded += Luminance(half[i]) - Luminance(dark[i]);
                brightest = Mathf.Max(brightest, added);
                lit += added > 0.01f ? 1 : 0;
            }

            var report = $"at 100% the brightest fleck adds {brightest:0.000}, {lit} of {dark.Length} pixels are lit, " +
                $"and the lit ones add {(lit > 0 ? fullAdded / lit : 0f):0.000} on average; 50% adds x{halfAdded / Mathf.Max(fullAdded, 1e-6f):0.000} of it";
            Debug.Log("[ALPS Mirror Ball] " + report);
            Assert.Greater(lit, 0, "The ball throws no light on the floor.\n" + report);
            Assert.AreEqual(Mathf.Pow(0.5f, 2.2f), halfAdded / fullAdded, 0.02f, "Half the level looks half as bright.\n" + report);
        }

        [Test]
        public void TheBall_TurnsByTheShowsTimeAlone()
        {
            var light = BuildScene(out var camera, out var director);
            var speed = light.sharedMaterial.GetFloat(AlpsShowPlayer.MirrorBallSpinProperty);

            var dark = Render(camera, director, light, Turning, false, null);
            var start = Render(camera, director, light, Turning, true, "mirror-ball-turning.png");
            var turn = Render(camera, director, light, Turning + 360f / speed, true, null);
            var part = Render(camera, director, light, Turning + 37f / speed, true, null);

            float added = 0f, afterATurn = 0f, partWay = 0f;
            for (var i = 0; i < dark.Length; i++)
            {
                added += Luminance(start[i]) - Luminance(dark[i]);
                afterATurn += Mathf.Abs(Luminance(turn[i]) - Luminance(start[i]));
                partWay += Mathf.Abs(Luminance(part[i]) - Luminance(start[i]));
            }

            var report = $"a whole turn later the light differs by x{afterATurn / added:0.000} of what it adds, 37 degrees later by x{partWay / added:0.000}";
            Debug.Log("[ALPS Mirror Ball] " + report);
            Assert.Less(afterATurn / added, 0.05f, "A whole turn of the show's time later the flecks are back where they were.\n" + report);
            Assert.Greater(partWay / added, 0.3f, "The flecks move as the show's time goes on.\n" + report);
        }

        private static float Luminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }

        private static Color[] Render(Camera camera, PlayableDirector director, Renderer light, float time, bool lit, string file)
        {
            director.time = time;
            director.Evaluate();
            light.enabled = lit;

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
                var folder = Path.Combine(Application.dataPath, "..", "TestResults~");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, file), png.EncodeToPNG());
                Object.DestroyImmediate(png);
            }

            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
            return pixels;
        }

        /// <summary>
        /// A mirror ball 3 m above VRChat's default floor, white, held still at 100% and then at
        /// 50%, and left to turn on its own after that. A camera looks down at the floor from
        /// beside it. Returns the renderer of the ball's light.
        /// </summary>
        private Renderer BuildScene(out Camera camera, out PlayableDirector director)
        {
            EditorSceneManager.OpenScene(DefaultWorldScene, OpenSceneMode.Single);
            DynamicGI.UpdateEnvironment();

            var container = AlpsCreateMenu.CreateContainer(null);
            var ball = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MirrorBall, container);
            Assert.NotNull(ball, AlpsCreateMenu.MirrorBall.Path);
            ball.transform.position = new Vector3(0f, 3f, 0f);
            Renderer light = null;
            foreach (var renderer in ball.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(AlpsShowPlayer.MirrorBallSpinProperty))
                {
                    light = renderer;
                }
            }

            Assert.NotNull(light, "The mirror ball has no light.");

            var timeline = Keep(ScriptableObject.CreateInstance<TimelineAsset>());
            var track = timeline.CreateTrack<AlpsLightingTrack>(null, "Ball");
            AddClip(track, 0f, 20f, 100f, true);
            AddClip(track, 20f, 40f, 50f, true);
            AddClip(track, 40f, 200f, 100f, false);

            director = new GameObject("Director").AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.playableAsset = timeline;
            director.SetGenericBinding(track, container.GetComponent<AlpsContainer>());
            director.RebuildGraph();

            camera = new GameObject("Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0f, 2.5f, -6f);
            camera.transform.LookAt(new Vector3(0f, 0f, 1f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            // The ball's light finds what it lands on in the depth texture.
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.allowHDR = true;
            camera.fieldOfView = 70f;
            return light;
        }

        /// <summary>A white clip at <paramref name="level"/>, whose gobo effect, if <paramref name="still"/>, holds the ball still.</summary>
        private static void AddClip(TrackAsset track, float start, float end, float level, bool still)
        {
            var clip = track.CreateClip<AlpsLightingClip>();
            clip.start = start;
            clip.duration = end - start;
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
            set.Add(AlpsEffectKind.Brightness).brightness.value = level;
            if (still)
            {
                var gobo = set.Add(AlpsEffectKind.Gobo);
                gobo.goboStops.Add(new AlpsGoboStop(1));
                gobo.goboRotationBeats = 0f;
            }

            ((AlpsLightingClip)clip.asset).data = set;
        }

        private T Keep<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }
    }
}
