using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// The experimental GPU evaluator against the C# one it was ported from, and the DMX grid
    /// against where VRSL reads it. These need a graphics device, so they are ignored under
    /// -nographics. Random phases and flicker use a noise of the GPU's own and are left out.
    /// </summary>
    public class AlpsGpuEvaluatorTests
    {
        private const float Bpm = 60f;
        private const int Fixtures = 6;

        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void RequireGraphics()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Needs a graphics device.");
            }
        }

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var created in _created)
            {
                Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        [Test]
        public void Frames_MatchTheCpuEvaluator()
        {
            var cases = new (string name, AlpsCompiledShow show)[]
            {
                ("moves", Standalone(MoveSet(), CircleSet())),
                ("colors", Standalone(ColorSet(), GradientSet())),
                ("brightness", Standalone(BrightnessSet(), ConeSet())),
                ("gobos", Standalone(GoboSet())),
                ("layers", LayeredShow()),
            };

            foreach (var (name, show) in cases)
            {
                for (var time = 0.05f; time < 6f; time += 0.37f)
                {
                    var gpu = RenderFrames(show, time, out _);
                    for (var fixture = 0; fixture < Fixtures; fixture++)
                    {
                        var cpu = EvaluateCpu(show, fixture, time);
                        for (var ch = 0; ch < AlpsShowEvaluator.FrameStride; ch++)
                        {
                            var expected = cpu[ch];
                            var actual = gpu[fixture * AlpsShowEvaluator.FrameStride + ch];
                            var tolerance = 2e-3f * Mathf.Max(1f, Mathf.Abs(expected));
                            Assert.AreEqual(expected, actual, tolerance, $"{name}, fixture {fixture}, channel {ch} at {time}s.");
                        }
                    }
                }
            }
        }

        [Test]
        public void Grid_PutsEveryChannelWhereVrslReadsIt()
        {
            var show = Standalone(MoveSet(), ColorSet(), BrightnessSet(), ConeSet(), GoboSet());
            const float time = 1.3f;
            var frames = RenderFrames(show, time, out var grids);
            var grid = ReadBack(grids[0]);
            var spin = ReadBack(grids[1]);
            var info = NeutralInfo();

            for (var fixture = 0; fixture < Fixtures; fixture++)
            {
                float Channel(int ch) => frames[fixture * AlpsShowEvaluator.FrameStride + ch];
                float Read(Texture2D texture, int offset)
                {
                    var texel = VrslTexel(13 * fixture + 1 + offset);
                    var color = texture.GetPixel(texel.x, texel.y);
                    return texture == grid ? color.r * 0.2126729f + color.g * 0.7151522f + color.b * 0.0721750f : color.r;
                }

                var level = Mathf.Max(0f, Channel(AlpsShowEvaluator.FrameBrightness) * Channel(AlpsShowEvaluator.FrameBrightnessScale) / 100f);
                var diagnostics = $"fixture {fixture}";
                Assert.AreEqual((-Channel(AlpsShowEvaluator.FramePan) + info[0]) / (2f * info[0]), Read(grid, 0), 1e-4f, "Pan. " + diagnostics);
                Assert.AreEqual((Channel(AlpsShowEvaluator.FrameTilt) + info[1]) / (2f * info[1]), Read(grid, 2), 1e-4f, "Tilt. " + diagnostics);
                Assert.AreEqual(Mathf.Max(0f, Channel(AlpsShowEvaluator.FrameConeWidth)) / 90f * 6f / 5.5f, Read(grid, 4), 1e-4f, "Cone width. " + diagnostics);
                Assert.AreEqual(1f, Read(grid, 5), 1e-4f, "Dimmer. " + diagnostics);
                Assert.AreEqual(Channel(AlpsShowEvaluator.FrameRed) * level * 2f, Read(grid, 7), 1e-4f, "Red. " + diagnostics);
                Assert.AreEqual(Channel(AlpsShowEvaluator.FrameGreen) * level * 2f, Read(grid, 8), 1e-4f, "Green. " + diagnostics);
                Assert.AreEqual(Channel(AlpsShowEvaluator.FrameBlue) * level * 2f, Read(grid, 9), 1e-4f, "Blue. " + diagnostics);
                Assert.AreEqual(Mathf.Round(Channel(AlpsShowEvaluator.FrameGobo)), Mathf.Round(Read(grid, 11) * 255f / 30f), "Gobo. " + diagnostics);
                Assert.AreEqual(Channel(AlpsShowEvaluator.FrameGoboRotation) * Mathf.Deg2Rad / 4f, Read(spin, 10), 1e-4f, "Gobo angle. " + diagnostics);
            }
        }

        [Test]
        public void Grid_ReadsBackThroughVrslsOwnFunctions()
        {
            var show = Standalone(MoveSet(), ColorSet(), BrightnessSet(), ConeSet(), GoboSet());
            const float time = 1.3f;
            var frames = RenderFrames(show, time, out var grids);
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
            var values = ReadBack(read);

            var failures = new List<string>();
            for (var fixture = 0; fixture < Fixtures; fixture++)
            {
                float Channel(int ch) => frames[fixture * AlpsShowEvaluator.FrameStride + ch];
                float Vrsl(int offset) => values.GetPixel(13 * fixture + offset, 0).r;
                var level = Mathf.Max(0f, Channel(AlpsShowEvaluator.FrameBrightness) * Channel(AlpsShowEvaluator.FrameBrightnessScale) / 100f);
                var expected = new[]
                {
                    (-Channel(AlpsShowEvaluator.FramePan) + info[0]) / (2f * info[0]),
                    0f,
                    (Channel(AlpsShowEvaluator.FrameTilt) + info[1]) / (2f * info[1]),
                    0f,
                    Mathf.Max(0f, Channel(AlpsShowEvaluator.FrameConeWidth)) / 90f * 6f / 5.5f,
                    1f,
                    1f,
                    Channel(AlpsShowEvaluator.FrameRed) * level * 2f,
                    Channel(AlpsShowEvaluator.FrameGreen) * level * 2f,
                    Channel(AlpsShowEvaluator.FrameBlue) * level * 2f,
                    0f,
                    Mathf.Round(Channel(AlpsShowEvaluator.FrameGobo)) * 30f / 255f,
                };

                for (var offset = 0; offset < expected.Length; offset++)
                {
                    if (Mathf.Abs(expected[offset] - Vrsl(offset)) > 1e-3f)
                    {
                        failures.Add($"fixture {fixture} offset {offset}: expected {expected[offset]:0.####}, VRSL reads {Vrsl(offset):0.####}");
                    }
                }

                var spin = values.GetPixel(13 * fixture + 10, 0).g;
                if (Mathf.Abs(Channel(AlpsShowEvaluator.FrameGoboRotation) * Mathf.Deg2Rad / 4f - spin) > 1e-3f)
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
            var values = ReadBack(read);

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

        private static AlpsClipEffectSet CircleSet()
        {
            var set = Set();
            var move = set.Add(AlpsEffectKind.Move);
            move.moveMode = AlpsMoveMode.Circle;
            move.circleCenterTilt.value = 40f;
            move.circleRadius.value = 15f;
            move.phaseOffset = 0.25f;
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

        private static AlpsClipEffectSet GradientSet()
        {
            var set = Set();
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.white) { isGradient = true });
            color.colorPhasing.useOwnPhase = true;
            color.colorPhasing.ownPhase = new AlpsPhaseSettings { beatsPerCycle = 3f, spread = 0.3f };
            color.parity = AlpsParity.Even;
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

        /// <summary>Crossfades on one layer, a fading clip above it, and gaps with nothing playing.</summary>
        private static AlpsCompiledShow LayeredShow()
        {
            var dim = Set();
            dim.Add(AlpsEffectKind.Brightness).brightness.value = 20f;
            dim.Add(AlpsEffectKind.Gobo).goboStops.Add(new AlpsGoboStop(3));
            var bright = BrightnessSet();
            var over = ColorSet();
            over.fadeInBeats = 1f;
            over.fadeOutBeats = 0.5f;

            return AlpsShowCompiler.CompileStandalone(
                Fixtures,
                Bpm,
                new AlpsStandaloneClip { set = dim, start = 0f, end = 3f, mixOut = 1f },
                new AlpsStandaloneClip { set = bright, start = 2f, end = 5f, mixIn = 1f },
                new AlpsStandaloneClip { set = over, start = 1f, end = 4f, layer = 1 });
        }

        // ------------------------------------------------------------------ evaluation

        private static float[] EvaluateCpu(AlpsCompiledShow show, int fixture, float time)
        {
            var stride = AlpsShowEvaluator.FrameStride;
            var defaults = NeutralDefaults();
            var active = new int[show.ClipCount];
            var weights = new float[show.ClipCount];
            var count = AlpsShowEvaluator.ActiveClips(show.clips, show.bucketStart, show.bucketClips, show.bucketSeconds, time, active, weights);
            var frame = new float[stride];
            AlpsShowEvaluator.EvaluateFixture(
                show.clips, show.effects, show.parameters, show.colors, show.gobos, show.positions,
                active, weights, count, show.groupCount, show.groupIndex,
                fixture, time, defaults, frame,
                new float[stride], new float[stride], new float[stride], new float[stride], new float[1]);
            return frame;
        }

        /// <summary>Runs the GPU passes and returns every fixture's frame, channel after channel.</summary>
        private float[] RenderFrames(AlpsCompiledShow show, float time, out RenderTexture[] grids)
        {
            var info = new float[Fixtures * AlpsShowPlayer.GpuFixtureInfoStride];
            for (var i = 0; i < Fixtures; i++)
            {
                AlpsShowPlayer.WriteNeutralDmxInfo(info, i * AlpsShowPlayer.GpuFixtureInfoStride);
            }

            var data = Keep(AlpsShowPlayer.CreateShowTexture(AlpsShowPlayer.PackShowData(
                show.clips, show.effects, show.parameters, show.colors, show.gobos, show.positions,
                show.bucketStart, show.bucketClips, show.bucketSeconds, show.groupCount, show.groupIndex,
                Fixtures, NeutralDefaults(), info)));
            var framesMaterial = Keep(new Material(Shader.Find(AlpsPreviewDriver.GpuFramesShader)));
            var gridMaterial = Keep(new Material(Shader.Find(AlpsPreviewDriver.GpuGridShader)));
            var frames = Keep(AlpsPreviewDriver.CreateGpuTarget("Frames", AlpsShowPlayer.GpuFrameTexels, Fixtures));
            var grid = Keep(AlpsPreviewDriver.CreateGpuTarget("Grid", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight));
            var spin = Keep(AlpsPreviewDriver.CreateGpuTarget("Spin", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight));
            AlpsShowPlayer.BindGpu(data, framesMaterial, gridMaterial, frames, grid, spin);
            framesMaterial.SetFloat("_AlpsTime", time);
            AlpsShowPlayer.RenderGpuPasses(data, framesMaterial, gridMaterial, frames, grid, spin);
            grids = new[] { grid, spin };

            var pixels = ReadBack(frames);
            var result = new float[Fixtures * AlpsShowEvaluator.FrameStride];
            for (var fixture = 0; fixture < Fixtures; fixture++)
            {
                for (var ch = 0; ch < AlpsShowEvaluator.FrameStride; ch++)
                {
                    result[fixture * AlpsShowEvaluator.FrameStride + ch] = pixels.GetPixel(ch / 4, fixture)[ch % 4];
                }
            }

            return result;
        }

        private Texture2D ReadBack(RenderTexture target)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = Keep(new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true));
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            return texture;
        }

        private T Keep<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        private static float[] NeutralDefaults()
        {
            var defaults = new float[Fixtures * AlpsShowEvaluator.FrameStride];
            for (var i = 0; i < Fixtures; i++)
            {
                AlpsShowPlayer.WriteNeutralFrame(defaults, i * AlpsShowEvaluator.FrameStride);
            }

            return defaults;
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
