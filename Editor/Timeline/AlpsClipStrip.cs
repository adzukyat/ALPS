using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// What the Timeline window draws on an ALPS clip: the color the clip plays over time,
    /// dimmed by its brightness and fade, where the clip's shared phase starts a new cycle,
    /// and the slopes of its own fade in and fade out.
    ///
    /// The clip is compiled on its own and evaluated by the same shader code as the show
    /// (<c>Hidden/ALPS/Clip Strip</c>), so the strip shows the same maths as the preview.
    /// Fixtures differ by order position, so each lane follows one representative fixture,
    /// the one at order position 0.
    /// When the clip's color or brightness is split by parity, odd and even fixtures get a
    /// lane each, odd on top.
    /// </summary>
    public sealed class AlpsClipStrip
    {
        public const int SamplesPerBeat = 16;
        public const int MinSamples = 64;
        public const int MaxSamples = 2048;
        private const int MaxCycleLines = 4096;

        /// <summary>Samples per lane across the whole clip.</summary>
        public int width;

        /// <summary>1, or 2 when odd and even fixtures play differently.</summary>
        public int lanes;

        /// <summary>Lane by lane from the top, <see cref="width"/> each. Null when the clip plays no color.</summary>
        public Color[] pixels;

        /// <summary>Cycle starts in seconds from the clip start, inside the clip only.</summary>
        public float[] cycleTimes = new float[0];

        /// <summary>Length of one cycle in seconds, 0 when the phase does not cycle.</summary>
        public float secondsPerCycle;

        /// <summary>Seconds the clip's own fade in and fade out take, 0 when it has none.</summary>
        public float fadeInSeconds;
        public float fadeOutSeconds;

        /// <summary>Length of the clip in seconds.</summary>
        public float duration;

        public const string ShaderName = "Hidden/ALPS/Clip Strip";

        public bool HasColor => pixels != null;

        public bool HasFade => fadeInSeconds > 0f || fadeOutSeconds > 0f;

        /// <summary>
        /// The clip's own fade at <paramref name="local"/> seconds from its start, 1 when it
        /// has none. Fades longer than the clip meet in a triangle.
        /// </summary>
        public float FadeAt(float local)
        {
            var scale = 1f;
            if (fadeInSeconds > 0f)
            {
                scale = Mathf.Min(scale, local / fadeInSeconds);
            }

            if (fadeOutSeconds > 0f)
            {
                scale = Mathf.Min(scale, (duration - local) / fadeOutSeconds);
            }

            return Mathf.Clamp01(scale);
        }

        public static AlpsClipStrip Build(
            AlpsClipEffectSet set,
            int fixtureCount,
            float bpm,
            float start,
            float end,
            int seed)
        {
            var strip = new AlpsClipStrip { lanes = 1, duration = Mathf.Max(0f, end - start) };
            if (set == null || end <= start)
            {
                return strip;
            }

            fixtureCount = Mathf.Max(1, fixtureCount);
            var show = AlpsShowCompiler.CompileStandalone(
                fixtureCount,
                bpm,
                new AlpsStandaloneClip { set = set, start = start, end = end, seed = seed });
            if (show.ClipCount == 0)
            {
                return strip;
            }

            // The compiled row carries the tempo that plays, a clip BPM override included.
            var rowBpm = show.clips[AlpsShowLayout.ClipBpm];
            FindCycles(strip, show, rowBpm, start, end);

            if (rowBpm > 0f)
            {
                strip.fadeInSeconds = show.clips[AlpsShowLayout.ClipFadeIn] * 60f / rowBpm;
                strip.fadeOutSeconds = show.clips[AlpsShowLayout.ClipFadeOut] * 60f / rowBpm;
            }

            var representatives = Representatives(set, show, fixtureCount);
            strip.lanes = representatives.Count;

            var beats = (end - start) * Mathf.Max(0f, rowBpm) / 60f;
            strip.width = Mathf.Clamp(Mathf.CeilToInt(beats * SamplesPerBeat), MinSamples, MaxSamples);

            var values = Render(show, fixtureCount, representatives, start, end, strip.width);
            if (values == null)
            {
                return strip;
            }

            var pixels = new Color[strip.width * strip.lanes];
            var anyColor = false;
            for (var lane = 0; lane < strip.lanes; lane++)
            {
                for (var i = 0; i < strip.width; i++)
                {
                    var value = values[lane * strip.width + i];
                    if (value.a < -0.5f)
                    {
                        pixels[lane * strip.width + i] = Color.clear;
                        continue;
                    }

                    // Brightness is dark unless an effect lights it, as in the show.
                    anyColor = true;
                    var local = (end - start) * (i + 0.5f) / strip.width;
                    pixels[lane * strip.width + i] = new Color(
                        Mathf.Clamp01(value.r),
                        Mathf.Clamp01(value.g),
                        Mathf.Clamp01(value.b),
                        Mathf.Clamp01(value.a) * strip.FadeAt(local));
                }
            }

            strip.pixels = anyColor ? pixels : null;
            return strip;
        }

        /// <summary>
        /// Evaluates clip 0 of <paramref name="show"/> on the GPU, one row per lane and one
        /// texel per sample, and reads it back. Null without a graphics device.
        /// </summary>
        private static Color[] Render(AlpsCompiledShow show, int fixtureCount, List<int> representatives, float start, float end, int width)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return null;
            }

            var lanes = representatives.Count;
            var info = new float[fixtureCount * AlpsShowPlayer.GpuFixtureInfoStride];
            var defaults = new float[fixtureCount * AlpsShowLayout.FrameStride];
            var rows = new int[fixtureCount];
            for (var i = 0; i < fixtureCount; i++)
            {
                AlpsShowPlayer.WriteNeutralFrame(defaults, i * AlpsShowLayout.FrameStride);
                AlpsShowPlayer.WriteNeutralDmxInfo(info, i * AlpsShowPlayer.GpuFixtureInfoStride);
                rows[i] = i;
            }

            var data = AlpsShowPlayer.CreateShowTexture(AlpsShowPlayer.PackShowData(
                show.clips, show.effects, show.parameters, show.colors, show.gobos, show.positions,
                show.bucketStart, show.bucketClips, show.bucketSeconds, show.groupCount, show.groupIndex,
                fixtureCount, defaults, info, rows, new float[fixtureCount * AlpsShowPlayer.GpuAimStride]));
            var material = new Material(shader);
            var target = AlpsPreviewDriver.CreateGpuTarget("ALPS Clip Strip", width, lanes);
            var readback = new Texture2D(width, lanes, TextureFormat.RGBAFloat, false, true);
            var active = RenderTexture.active;
            try
            {
                material.SetTexture("_AlpsData", data);
                material.SetFloat("_AlpsStripStart", start);
                material.SetFloat("_AlpsStripEnd", end);
                material.SetFloat("_AlpsStripLanes", lanes);
                material.SetFloat("_AlpsStripFixture0", representatives[0]);
                material.SetFloat("_AlpsStripFixture1", representatives[lanes > 1 ? 1 : 0]);
                Graphics.Blit(data, target, material, 0);

                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, width, lanes), 0, 0);
                readback.Apply(false);
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = active;
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(data);
            }
        }

        /// <summary>
        /// Fixtures to sample, one per lane. The evaluator counts odd fixtures from 1, so
        /// list index 0 is odd. Each lane takes its fixture with the lowest order position.
        /// </summary>
        private static List<int> Representatives(AlpsClipEffectSet set, AlpsCompiledShow show, int fixtureCount)
        {
            var order = AlpsShowLayout.ToInt(show.clips[AlpsShowLayout.ClipOrder]);
            var seed = AlpsShowLayout.ToInt(show.clips[AlpsShowLayout.ClipSeed]);
            var groupSize = AlpsShowLayout.ToInt(show.clips[AlpsShowLayout.ClipPhase + AlpsShowLayout.PhaseGroupSize]);

            int Lowest(int parity)
            {
                var best = -1;
                var bestK = int.MaxValue;
                for (var i = 0; i < fixtureCount; i++)
                {
                    if (parity >= 0 && i % 2 != parity)
                    {
                        continue;
                    }

                    var k = AlpsShowLayout.OrderPosition(order, seed, i, fixtureCount, groupSize);
                    if (k < bestK)
                    {
                        best = i;
                        bestK = k;
                    }
                }

                return best;
            }

            var result = new List<int>();
            if (fixtureCount > 1 && SplitsByParity(set))
            {
                result.Add(Lowest(0));
                result.Add(Lowest(1));
            }
            else
            {
                result.Add(Lowest(-1));
            }

            return result;
        }

        private static bool SplitsByParity(AlpsClipEffectSet set)
        {
            foreach (var effect in set.effects)
            {
                if (effect != null
                    && (effect.kind == AlpsEffectKind.Color || effect.kind == AlpsEffectKind.Brightness)
                    && effect.parity != AlpsParity.All)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Cycle starts of the clip's shared phase, for order position 0. Beats count from
        /// the clip's start, so the first cycle begins with the clip and gets no line.
        /// </summary>
        private static void FindCycles(AlpsClipStrip strip, AlpsCompiledShow show, float bpm, float start, float end)
        {
            var beatsPerCycle = show.clips[AlpsShowLayout.ClipPhase + AlpsShowLayout.PhaseBeatsPerCycle];
            if (bpm <= 0f || beatsPerCycle <= 0f)
            {
                return;
            }

            var secondsPerCycle = beatsPerCycle * 60f / bpm;
            strip.secondsPerCycle = secondsPerCycle;

            var times = new List<float>();
            for (var cycle = 1; cycle <= MaxCycleLines; cycle++)
            {
                var offset = cycle * secondsPerCycle;
                if (start + offset >= end)
                {
                    break;
                }

                times.Add(offset);
            }

            strip.cycleTimes = times.ToArray();
        }
    }
}
