using System.Collections.Generic;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// What the Timeline window draws on an ALPS clip: the color the clip plays over time,
    /// dimmed by its brightness, and where the clip's shared phase starts a new cycle.
    ///
    /// The clip is compiled on its own and run through <see cref="AlpsShowEvaluator.EvaluateClip"/>,
    /// so the strip shows the same math as the preview. Fixtures differ by order position,
    /// so each lane follows one representative fixture, the one at order position 0.
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

        public bool HasColor => pixels != null;

        public static AlpsClipStrip Build(
            AlpsClipEffectSet set,
            int fixtureCount,
            float bpm,
            float start,
            float end,
            int seed)
        {
            var strip = new AlpsClipStrip { lanes = 1 };
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
            var rowBpm = show.clips[AlpsShowEvaluator.ClipBpm];
            FindCycles(strip, show, rowBpm, start, end);

            var representatives = Representatives(set, show, fixtureCount);
            strip.lanes = representatives.Count;

            var beats = (end - start) * Mathf.Max(0f, rowBpm) / 60f;
            strip.width = Mathf.Clamp(Mathf.CeilToInt(beats * SamplesPerBeat), MinSamples, MaxSamples);

            var pixels = new Color[strip.width * strip.lanes];
            var frame = new float[AlpsShowEvaluator.FrameStride];
            var written = new float[AlpsShowEvaluator.FrameStride];
            var scratch = new float[1];
            var anyColor = false;
            for (var lane = 0; lane < strip.lanes; lane++)
            {
                for (var i = 0; i < strip.width; i++)
                {
                    var time = start + (end - start) * (i + 0.5f) / strip.width;
                    for (var ch = 0; ch < AlpsShowEvaluator.FrameStride; ch++)
                    {
                        frame[ch] = 0f;
                        written[ch] = 0f;
                    }

                    AlpsShowEvaluator.EvaluateClip(
                        show.clips,
                        show.effects,
                        show.parameters,
                        show.colors,
                        show.gobos,
                        0,
                        representatives[lane],
                        fixtureCount,
                        time,
                        frame,
                        written,
                        scratch);

                    if (written[AlpsShowEvaluator.FrameRed] < 0.5f)
                    {
                        pixels[lane * strip.width + i] = Color.clear;
                        continue;
                    }

                    anyColor = true;
                    var alpha = written[AlpsShowEvaluator.FrameBrightness] > 0.5f
                        ? Mathf.Clamp01(frame[AlpsShowEvaluator.FrameBrightness] / 100f)
                        : 1f;
                    pixels[lane * strip.width + i] = new Color(
                        Mathf.Clamp01(frame[AlpsShowEvaluator.FrameRed]),
                        Mathf.Clamp01(frame[AlpsShowEvaluator.FrameGreen]),
                        Mathf.Clamp01(frame[AlpsShowEvaluator.FrameBlue]),
                        alpha);
                }
            }

            strip.pixels = anyColor ? pixels : null;
            return strip;
        }

        /// <summary>
        /// Fixtures to sample, one per lane. The evaluator counts odd fixtures from 1, so
        /// list index 0 is odd. Each lane takes its fixture with the lowest order position.
        /// </summary>
        private static List<int> Representatives(AlpsClipEffectSet set, AlpsCompiledShow show, int fixtureCount)
        {
            var order = AlpsShowEvaluator.ToInt(show.clips[AlpsShowEvaluator.ClipOrder]);
            var seed = AlpsShowEvaluator.ToInt(show.clips[AlpsShowEvaluator.ClipSeed]);
            var groupSize = AlpsShowEvaluator.ToInt(show.clips[AlpsShowEvaluator.ClipPhase + AlpsShowEvaluator.PhaseGroupSize]);

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

                    var k = AlpsShowEvaluator.OrderPosition(order, seed, i, fixtureCount, groupSize);
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
            var beatsPerCycle = show.clips[AlpsShowEvaluator.ClipPhase + AlpsShowEvaluator.PhaseBeatsPerCycle];
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
