using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// The DMX grid against where VRSL reads it, the DMX rows shows share, and tracking a
    /// user. These need a graphics device, so they are ignored under -nographics.
    /// </summary>
    public class AlpsGpuEvaluatorTests
    {
        private const float Bpm = 60f;
        private const int Fixtures = 6;

        private readonly List<Object> _created = new List<Object>();
        private readonly List<AlpsGpuShow> _shows = new List<AlpsGpuShow>();

        [SetUp]
        public void RequireGraphics()
        {
            AlpsGpuShow.RequireGraphics();
        }

        [TearDown]
        public void DestroyCreated()
        {
            // A blit leaves its target active, and releasing the active target warns.
            RenderTexture.active = null;
            foreach (var created in _created)
            {
                Object.DestroyImmediate(created);
            }

            foreach (var show in _shows)
            {
                show.Dispose();
            }

            _created.Clear();
            _shows.Clear();
        }

        [Test]
        public void Tracking_AimsAtTheUserAndFollowsAtItsSpeed()
        {
            var set = Set();
            var move = set.Add(AlpsEffectKind.Move);
            move.moveMode = AlpsMoveMode.TrackUser;
            move.trackUserName = "Someone";
            move.trackSpeed = 2f;
            var show = AlpsShowCompiler.CompileStandalone(1, Bpm, new AlpsStandaloneClip { set = set, end = 10f });
            Assert.AreEqual(new[] { "Someone" }, show.userNames);

            // The fixture's aim space is the world turned by 90 degrees around Y.
            var aim = new float[AlpsShowPlayer.GpuAimStride];
            AlpsShowPlayer.WriteAim(Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one).inverse, aim, 0);
            var gpu = Keep(new AlpsGpuShow(show, 1, aim: aim));
            var previous = Keep(AlpsPreviewDriver.CreateGpuTarget("Previous", AlpsShowPlayer.GpuFrameTexels, 1));
            gpu.FramesMaterial.SetTexture("_AlpsPrevFrames", previous);

            float[] Render(Vector3 head, float deltaTime)
            {
                gpu.FramesMaterial.SetVector("_AlpsTrackTarget0", new Vector4(head.x, head.y, head.z, 1f));
                gpu.FramesMaterial.SetFloat("_AlpsDeltaTime", deltaTime);
                gpu.Render(1f);
                var frame = AlpsGpuShow.ReadFrames(gpu.Frames, 1, AlpsShowLayout.FrameAimed + 1);
                Graphics.Blit(gpu.Frames, previous);
                return frame;
            }

            float Pan(Vector3 head)
            {
                var local = Quaternion.Inverse(Quaternion.Euler(0f, 90f, 0f)) * head;
                return AimPan(local);
            }

            var first = new Vector3(1f, -2f, 3f);
            var aimed = Render(first, 0f);
            Assert.AreEqual(1f, aimed[AlpsShowLayout.FrameAimed], "The head is aimed.");
            Assert.AreEqual(Pan(first), aimed[AlpsShowLayout.FramePan], 0.01f, "The first frame points straight at the user.");
            var local = Quaternion.Inverse(Quaternion.Euler(0f, 90f, 0f)) * first;
            Assert.AreEqual(AimTilt(local), aimed[AlpsShowLayout.FrameTilt], 0.01f);

            // Half a follow later the head has turned part of the way to where the user went.
            var second = new Vector3(-2f, -2f, 1f);
            var followed = Render(second, 0.1f);
            var share = 1f - Mathf.Exp(-2f * 0.1f);
            var expectedPan = aimed[AlpsShowLayout.FramePan] + Mathf.DeltaAngle(aimed[AlpsShowLayout.FramePan], Pan(second)) * share;
            Assert.AreEqual(expectedPan, followed[AlpsShowLayout.FramePan], 0.01f, "Pan follows the short way round.");

            // Once the user leaves, the head keeps what the layers give it and is no longer aimed.
            gpu.FramesMaterial.SetVector("_AlpsTrackTarget0", Vector4.zero);
            gpu.Render(1f);
            var gone = AlpsGpuShow.ReadFrames(gpu.Frames, 1, AlpsShowLayout.FrameAimed + 1);
            Assert.AreEqual(0f, gone[AlpsShowLayout.FrameAimed]);
            Assert.AreEqual(0f, gone[AlpsShowLayout.FramePan], 0.001f, "The neutral default pan.");
        }

        /// <summary>Pan that points the head at a point in its aim space. VRSL pans around the mesh's local Z axis.</summary>
        private static float AimPan(Vector3 local)
        {
            return Mathf.Atan2(local.x, -local.y) * Mathf.Rad2Deg;
        }

        /// <summary>Tilt that points the head at a point in its aim space. Tilt 0 aims along the mesh's local -Z axis.</summary>
        private static float AimTilt(Vector3 local)
        {
            return Mathf.Atan2(Mathf.Sqrt(local.x * local.x + local.y * local.y), -local.z) * Mathf.Rad2Deg;
        }

        [Test]
        public void Grid_PutsEachFixtureOnItsSharedRow()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Brightness).brightness.value = 50f;
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
            var show = AlpsShowCompiler.CompileStandalone(2, Bpm, new AlpsStandaloneClip { set = set, end = 10f });

            // Another show holds rows 0 to 4, so these two fixtures went on rows 5 and 9.
            var gpu = Keep(new AlpsGpuShow(show, 2, new[] { 5, 9 }));
            gpu.Render(1f);
            var grid = gpu.ReadBack(gpu.Grid);
            for (var row = 0; row < 12; row++)
            {
                var texel = VrslTexel(13 * row + 1 + 7);
                var red = grid.GetPixel(texel.x, texel.y).r;
                Assert.AreEqual(row == 5 || row == 9 ? 1f : 0f, red, 1e-4f, $"Red of row {row}.");
            }
        }

        [Test]
        public void Grid_PutsEveryChannelWhereVrslReadsIt()
        {
            var show = Standalone(MoveSet(), ColorSet(), BrightnessSet(), ConeSet(), GoboSet());
            const float time = 1.3f;
            var gpu = Keep(new AlpsGpuShow(show, Fixtures));
            var frames = gpu.FramesAt(time);
            var grid = gpu.ReadBack(gpu.Grid);
            var spin = gpu.ReadBack(gpu.Spin);
            var info = NeutralInfo();

            for (var fixture = 0; fixture < Fixtures; fixture++)
            {
                float Channel(int ch) => frames[fixture * AlpsShowLayout.FrameStride + ch];
                float Read(Texture2D texture, int offset)
                {
                    var texel = VrslTexel(13 * fixture + 1 + offset);
                    var color = texture.GetPixel(texel.x, texel.y);
                    return texture == grid ? color.r * 0.2126729f + color.g * 0.7151522f + color.b * 0.0721750f : color.r;
                }

                var level = Mathf.Max(0f, Channel(AlpsShowLayout.FrameBrightness) * Channel(AlpsShowLayout.FrameBrightnessScale) / 100f);
                var diagnostics = $"fixture {fixture}";
                Assert.AreEqual((-Channel(AlpsShowLayout.FramePan) + info[0]) / (2f * info[0]), Read(grid, 0), 1e-4f, "Pan. " + diagnostics);
                Assert.AreEqual(ConeStretch(Channel(AlpsShowLayout.FrameConeLength), Channel(AlpsShowLayout.FrameConeMeshLength)), Read(grid, 1), 1e-4f, "Cone length. " + diagnostics);
                Assert.AreEqual((Channel(AlpsShowLayout.FrameTilt) + info[1]) / (2f * info[1]), Read(grid, 2), 1e-4f, "Tilt. " + diagnostics);
                Assert.AreEqual(Mathf.Max(0f, Channel(AlpsShowLayout.FrameConeWidth)) / 90f * 6f / 5.5f, Read(grid, 4), 1e-4f, "Cone width. " + diagnostics);
                Assert.AreEqual(1f, Read(grid, 5), 1e-4f, "Dimmer. " + diagnostics);
                Assert.AreEqual(Channel(AlpsShowLayout.FrameRed) * level * 2f, Read(grid, 7), 1e-4f, "Red. " + diagnostics);
                Assert.AreEqual(Channel(AlpsShowLayout.FrameGreen) * level * 2f, Read(grid, 8), 1e-4f, "Green. " + diagnostics);
                Assert.AreEqual(Channel(AlpsShowLayout.FrameBlue) * level * 2f, Read(grid, 9), 1e-4f, "Blue. " + diagnostics);
                Assert.AreEqual(Mathf.Round(Channel(AlpsShowLayout.FrameGobo)), Mathf.Round(Read(grid, 11) * 255f / 30f), "Gobo. " + diagnostics);
                Assert.AreEqual(Channel(AlpsShowLayout.FrameGoboRotation) * Mathf.Deg2Rad / 4f, Read(spin, 10), 1e-4f, "Gobo angle. " + diagnostics);
            }
        }

        [Test]
        public void Grid_ReadsBackThroughVrslsOwnFunctions()
        {
            var show = Standalone(MoveSet(), ColorSet(), BrightnessSet(), ConeSet(), GoboSet());
            const float time = 1.3f;
            var gpu = Keep(new AlpsGpuShow(show, Fixtures));
            var frames = gpu.FramesAt(time);
            var grids = new[] { gpu.Grid, gpu.Spin };
            var info = NeutralInfo();

            var probeShader = Shader.Find("Hidden/ALPS/Tests/VRSL Probe");
            Assert.NotNull(probeShader, "The VRSL probe shader did not compile.");
            var probe = Keep(new Material(probeShader));
            var channels = Fixtures * 13;
            probe.SetFloat("_AlpsProbeChannels", channels);
            Shader.SetGlobalTexture("_Udon_DMXGridRenderTexture", grids[0]);
            Shader.SetGlobalTexture("_Udon_DMXGridSpinTimer", grids[1]);
            Shader.SetGlobalVector("_Udon_DMXGridRenderTexture_TexelSize", new Vector4(1f / grids[0].width, 1f / grids[0].height, grids[0].width, grids[0].height));
            var read = Keep(AlpsPreviewDriver.CreateGpuTarget("Probe", channels, 1));
            Graphics.Blit(null, read, probe);
            var values = Keep(AlpsGpuShow.ReadBackTexture(read));

            var failures = new List<string>();
            for (var fixture = 0; fixture < Fixtures; fixture++)
            {
                float Channel(int ch) => frames[fixture * AlpsShowLayout.FrameStride + ch];
                float Vrsl(int offset) => values.GetPixel(13 * fixture + offset, 0).r;
                var level = Mathf.Max(0f, Channel(AlpsShowLayout.FrameBrightness) * Channel(AlpsShowLayout.FrameBrightnessScale) / 100f);
                var expected = new[]
                {
                    (-Channel(AlpsShowLayout.FramePan) + info[0]) / (2f * info[0]),
                    ConeStretch(Channel(AlpsShowLayout.FrameConeLength), Channel(AlpsShowLayout.FrameConeMeshLength)),
                    (Channel(AlpsShowLayout.FrameTilt) + info[1]) / (2f * info[1]),
                    0f,
                    Mathf.Max(0f, Channel(AlpsShowLayout.FrameConeWidth)) / 90f * 6f / 5.5f,
                    1f,
                    1f,
                    Channel(AlpsShowLayout.FrameRed) * level * 2f,
                    Channel(AlpsShowLayout.FrameGreen) * level * 2f,
                    Channel(AlpsShowLayout.FrameBlue) * level * 2f,
                    0f,
                    Mathf.Round(Channel(AlpsShowLayout.FrameGobo)) * 30f / 255f,
                };

                for (var offset = 0; offset < expected.Length; offset++)
                {
                    if (Mathf.Abs(expected[offset] - Vrsl(offset)) > 1e-3f)
                    {
                        failures.Add($"fixture {fixture} offset {offset}: expected {expected[offset]:0.####}, VRSL reads {Vrsl(offset):0.####}");
                    }
                }

                var spin = values.GetPixel(13 * fixture + 10, 0).g;
                if (Mathf.Abs(Channel(AlpsShowLayout.FrameGoboRotation) * Mathf.Deg2Rad / 4f - spin) > 1e-3f)
                {
                    failures.Add($"fixture {fixture} gobo angle: VRSL reads {spin:0.####}");
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>Logs which texel VRSL's getValueAtCoords reads for each channel, from a grid that holds its own coordinates.</summary>
        [Test, Explicit]
        public void Diagnose_WhereVrslReads()
        {
            var width = AlpsShowPlayer.GpuGridWidth;
            var height = AlpsShowPlayer.GpuGridHeight;
            var coded = Keep(new Texture2D(width, height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point });
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var v = x + y * 100f;
                    coded.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            }

            coded.Apply();
            var grid = Keep(AlpsPreviewDriver.CreateGpuTarget("Coded", width, height));
            Graphics.Blit(coded, grid);

            var probe = Keep(new Material(Shader.Find("Hidden/ALPS/Tests/VRSL Probe")));
            const int channels = 13 * 12;
            probe.SetFloat("_AlpsProbeChannels", channels);
            Shader.SetGlobalTexture("_Udon_DMXGridRenderTexture", grid);
            Shader.SetGlobalTexture("_Udon_DMXGridSpinTimer", grid);
            var read = Keep(AlpsPreviewDriver.CreateGpuTarget("Probe", channels, 1));
            Graphics.Blit(null, read, probe);
            var values = Keep(AlpsGpuShow.ReadBackTexture(read));

            var text = new System.Text.StringBuilder("[ALPS Probe] channel: texel x,y\n");
            for (var c = 0; c < channels; c++)
            {
                var v = Mathf.RoundToInt(values.GetPixel(c, 0).r);
                text.Append($"{c + 1}:{v % 100},{v / 100} ");
                if ((c + 1) % 13 == 0)
                {
                    text.Append('\n');
                }
            }

            Debug.Log(text.ToString());
        }

        // ------------------------------------------------------------------ shows

        private static AlpsClipEffectSet Set(float spread = 0.5f)
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.Wave;
            set.phase.SetShares(0.4f, 0.1f, 0.3f);
            set.phase.ease = AlpsEaseType.InOutSine;
            set.phase.fallEase = AlpsEaseType.OutBounce;
            set.phase.beatsPerCycle = 2f;
            set.phase.spread = spread;
            return set;
        }

        private static AlpsClipEffectSet MoveSet()
        {
            var set = Set();
            set.order = AlpsOrderMode.Symmetric;
            var move = set.Add(AlpsEffectKind.Move);
            move.pan.isRange = true;
            move.pan.range = new Vector2(-60f, 60f);
            move.tilt.isRange = true;
            move.tilt.range = new Vector2(20f, 80f);
            move.tilt.hasSpread = true;
            move.tilt.spreadRange = new Vector2(20f, 40f);
            move.tilt.spreadRangeEnd = new Vector2(70f, 90f);
            move.panTiltPhaseOffsetDegrees = 90f;
            return set;
        }

        private static AlpsClipEffectSet ColorSet()
        {
            var set = Set();
            set.order = AlpsOrderMode.Random;
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(new AlpsColorStop(Color.green));
            color.colorStops.Add(new AlpsColorStop(new Color(0.2f, 0.4f, 1f)));
            set.Add(AlpsEffectKind.Brightness).brightness.value = 80f;
            return set;
        }

        private static AlpsClipEffectSet BrightnessSet()
        {
            var set = Set();
            set.order = AlpsOrderMode.Reverse;
            set.phase.fixtureGroupSize = 2;
            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.brightness.isRange = true;
            brightness.brightness.range = new Vector2(10f, 150f);
            brightness.blackoutOnReturn = true;
            brightness.blackoutFadeIn = 0.2f;
            brightness.blackoutFadeOut = 0.1f;
            return set;
        }

        private static AlpsClipEffectSet ConeSet()
        {
            var set = Set();
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.isRange = true;
            cone.coneWidth.timing = AlpsTimingMode.PerCycle;
            cone.coneWidth.range = new Vector2(10f, 70f);
            cone.coneLength.value = 30f;
            return set;
        }

        private static AlpsClipEffectSet GoboSet()
        {
            var set = Set();
            var gobo = set.Add(AlpsEffectKind.Gobo);
            gobo.goboStops.Add(new AlpsGoboStop(2));
            gobo.goboStops.Add(new AlpsGoboStop(5));
            gobo.goboPhasing.timing = AlpsTimingMode.PerCycle;
            gobo.goboRotationBeats = 3f;
            gobo.goboFixtureStaggerDegrees = 20f;
            return set;
        }

        /// <summary>Each set as its own clip on its own layer, over the whole test.</summary>
        private static AlpsCompiledShow Standalone(params AlpsClipEffectSet[] sets)
        {
            var clips = new AlpsStandaloneClip[sets.Length];
            for (var i = 0; i < sets.Length; i++)
            {
                clips[i] = new AlpsStandaloneClip { set = sets[i], start = 0f, end = 10f, layer = i, seed = 17 + i };
            }

            return AlpsShowCompiler.CompileStandalone(Fixtures, Bpm, clips);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// What VRSL's volumetric mesh adds to the fixture's own mesh length, 4 per unit of
        /// the fine pan channel, for a model cone length.
        /// </summary>
        private static float ConeStretch(float length, float mesh)
        {
            return mesh * (Mathf.Max(0f, length) / AlpsShowPlayer.ModelConeLengthLimit - 1f) / 4f;
        }

        private T Keep<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        private AlpsGpuShow Keep(AlpsGpuShow gpu)
        {
            _shows.Add(gpu);
            return gpu;
        }

        private static float[] NeutralInfo()
        {
            var info = new float[AlpsShowPlayer.GpuFixtureInfoStride];
            AlpsShowPlayer.WriteNeutralDmxInfo(info, 0);
            return info;
        }

        /// <summary>
        /// The texel VRSL samples for absolute channel <paramref name="dmx"/>, as its
        /// getValueAtCoords and IndustryRead work it out on the 26 x 240 horizontal grid.
        /// IndustryRead takes the row as an int, so the fraction of dmx / 13 is dropped.
        /// </summary>
        private static Vector2Int VrslTexel(int dmx)
        {
            var x = dmx % 13;
            var row = (int)(dmx / 13f + 1f);
            var resMultiplier = 26f / 13f;
            var u = x * resMultiplier * (1f / 26f) - 0.015f;
            var v = row * resMultiplier * (1f / 240f) - 0.001915f;
            return new Vector2Int(Mathf.FloorToInt(u * 26f), Mathf.FloorToInt(v * 240f));
        }
    }
}
