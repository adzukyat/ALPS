using System.Collections.Generic;
using System.IO;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// A laser's beams as VRSL's shader draws them from what the laser effect puts on the
    /// grid, previewed and rendered from in front of the laser, looking back along its cone,
    /// where every beam is a spoke around the middle of the picture.
    ///
    /// VRSL's own laser moves its beams round by a speed times the shader's time, so where
    /// they stand differs from one visitor to the next. <c>scripts/vendor-vrsl.py</c> has the
    /// copy take how far they have gone round from the spin grid, which this holds: a grid
    /// read back says the same whether the laser follows it or not. The renders go to
    /// TestResults~ to look at.
    /// </summary>
    public class AlpsLaserBeamTests
    {
        private const int Size = 512;
        private const int Bins = 360;
        private const float Bpm = 60f;

        /// <summary>Beats a turn takes, at which eight beams move on by half the gap between two every beat.</summary>
        private const float BeatsPerTurn = 16f;

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
        public void Beams_StandWhereTheShowsTimePutsThem()
        {
            // A beat apart the beams stand in the gaps they left, and two apart where they were.
            var start = Around(8f, 1f, "laser-beams.png");
            var half = Around(8f, 2f, "laser-beams-half-a-gap-on.png");
            var whole = Around(8f, 3f, "laser-beams-a-gap-on.png");
            var report = $"half a gap on {Alignment(start, half, 8):0.00}, a gap on {Alignment(start, whole, 8):0.00}";
            Debug.Log("[ALPS Laser Beams] " + report);

            Assert.Greater(Contrast(start), 0.5f, "The beams are spokes around the middle of the picture.");
            Assert.Less(Alignment(start, half, 8), -0.95f, "Half a gap on, the beams stand where the gaps were. " + report);
            Assert.Greater(Alignment(start, whole, 8), 0.95f, "A gap on, they stand where they were. " + report);
        }

        [Test]
        public void ABeam_IsAsBrightHoweverManyThereAre()
        {
            // The laser draws a beam by how far a pixel is from it in shares of the gap between
            // two, times the count, so the count drops out: twice the beams give nearly twice
            // the light, less the far glow of each, which ends where the next beam's begins.
            var few = Sum(Around(8f, 1f, null));
            var many = Sum(Around(16f, 1f, null));
            Debug.Log($"[ALPS Laser Beams] the light of 16 beams over that of 8: {many / few:0.00}");
            Assert.That(many / few, Is.InRange(1.5f, 2f));
        }

        private static float Sum(float[] profile)
        {
            var sum = 0f;
            foreach (var value in profile)
            {
                sum += value;
            }

            return sum;
        }

        /// <summary>How far the brightest direction stands above the mean, as a share of the brightest.</summary>
        private static float Contrast(float[] profile)
        {
            var peak = 0f;
            foreach (var value in profile)
            {
                peak = Mathf.Max(peak, value);
            }

            return peak > 0f ? 1f - Sum(profile) / profile.Length / peak : 0f;
        }

        /// <summary>
        /// 1 where the <paramref name="beams"/> beams of two profiles stand in the same
        /// directions, -1 where those of one stand in the middle of the gaps of the other: the
        /// cosine of how far apart the two are in the wave that repeats once per beam.
        /// </summary>
        private static float Alignment(float[] a, float[] b, int beams)
        {
            var waveA = Wave(a, beams);
            var waveB = Wave(b, beams);
            var length = waveA.magnitude * waveB.magnitude;
            return length > 0f ? Vector2.Dot(waveA, waveB) / length : 0f;
        }

        private static Vector2 Wave(float[] profile, int beams)
        {
            var wave = Vector2.zero;
            for (var i = 0; i < profile.Length; i++)
            {
                var angle = 2f * Mathf.PI * beams * (i + 0.5f) / profile.Length;
                wave += profile[i] * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            return wave;
        }

        /// <summary>
        /// The light of a round cone of <paramref name="beams"/> beams by the direction around
        /// its axis, a bin per degree, <paramref name="time"/> seconds into a clip that sends
        /// the beams round.
        /// </summary>
        private float[] Around(float beams, float time, string file)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;

            // A laser at its prefab's rotation fires along +Z.
            var container = Keep(AlpsCreateMenu.CreateContainer(null));
            var laser = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.Laser, container);
            Assert.NotNull(laser, AlpsCreateMenu.Laser.Path);
            laser.transform.position = Vector3.zero;

            var timeline = Keep(ScriptableObject.CreateInstance<TimelineAsset>());
            var director = Keep(new GameObject("Show")).AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var track = timeline.CreateTrack<AlpsLightingTrack>(null, "Lasers");
            track.bpm = Bpm;
            director.SetGenericBinding(track, container.GetComponent<AlpsContainer>());
            var clip = track.CreateClip<AlpsLightingClip>();
            clip.start = 0.0;
            clip.duration = 10.0;
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Move);
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
            set.Add(AlpsEffectKind.Brightness).brightness.value = 100f;
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.value = 25f;
            cone.coneLength.value = AlpsShowPlayer.ModelConeLengthLimit;
            var shape = set.Add(AlpsEffectKind.Laser);
            shape.laserFlatness.value = 0f;
            shape.laserBeams.value = beams;
            shape.laserThickness.value = 10f;
            shape.laserSpinBeats = BeatsPerTurn;
            ((AlpsLightingClip)clip.asset).data = set;

            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.RebuildGraph();
            director.time = time;
            director.Evaluate();

            var camera = Keep(new GameObject("Camera")).AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = true;
            camera.fieldOfView = 100f;
            camera.nearClipPlane = 0.01f;
            camera.transform.position = new Vector3(0f, 0f, 2f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            var target = Keep(new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var image = Keep(new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true));
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;

            var pixels = image.GetPixels();
            if (file != null)
            {
                var png = Keep(new Texture2D(Size, Size, TextureFormat.RGB24, false));
                var shown = new Color[pixels.Length];
                for (var i = 0; i < pixels.Length; i++)
                {
                    shown[i] = pixels[i].gamma;
                }

                png.SetPixels(shown);
                File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "TestResults~", file), png.EncodeToPNG());
            }

            // A ring around the middle, clear of the glow where the beams start.
            var profile = new float[Bins];
            var counts = new int[Bins];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var offset = new Vector2(x + 0.5f - Size * 0.5f, y + 0.5f - Size * 0.5f);
                    var radius = offset.magnitude / Size;
                    if (radius < 0.15f || radius > 0.48f)
                    {
                        continue;
                    }

                    var bin = Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) / (2f * Mathf.PI), 1f) * Bins) % Bins;
                    var pixel = pixels[y * Size + x];
                    profile[bin] += pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f;
                    counts[bin]++;
                }
            }

            for (var i = 0; i < Bins; i++)
            {
                profile[i] = counts[i] > 0 ? profile[i] / counts[i] : 0f;
            }

            return profile;
        }

        private T Keep<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }
    }
}
