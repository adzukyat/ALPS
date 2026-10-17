using System.Collections.Generic;
using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Whether a fixture's gobo projection shows, worked out on the CPU from a compiled show.
    ///
    /// VRSL reads nothing from DMX that hides the projection alone, so the GPU never sees this
    /// switch. The preview sets the projection renderers from <see cref="IsLit"/>, and a build
    /// keys the same switches into Animation Tracks (<see cref="Switches"/>). Composition
    /// mirrors the gobo channel of the evaluator: a gobo effect writes the switch, the
    /// heaviest clip of a layer wins by its Timeline weight, a layer covers the ones below it from a weight of 0.5, and nothing covering it leaves
    /// the projection showing.
    /// </summary>
    public static class AlpsProjection
    {
        /// <summary>Step the switch is sampled at while clips mix, where the heaviest clip can change.</summary>
        public const float MixSampleSeconds = 1f / 60f;

        /// <summary>True when some gobo effect of the show hides the projection.</summary>
        public static bool AnyOff(AlpsCompiledShow show)
        {
            var stride = AlpsShowLayout.EffectStride;
            for (var row = 0; row + stride <= show.effects.Length; row += stride)
            {
                if ((int)show.effects[row + AlpsShowLayout.EffectKind] == (int)AlpsEffectKind.Gobo
                    && show.effects[row + AlpsShowLayout.EffectScalarC] > 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether fixture <paramref name="fixture"/> shows its projection at <paramref name="time"/>.</summary>
        public static bool IsLit(AlpsCompiledShow show, int fixture, float time)
        {
            var lit = true;
            var layer = float.NaN;
            var best = 0f;
            var bestLit = true;
            var stride = AlpsShowLayout.ClipStride;
            for (var clip = 0; clip < show.ClipCount; clip++)
            {
                var row = clip * stride;
                var clipLayer = show.clips[row + AlpsShowLayout.ClipLayer];
                if (clipLayer != layer)
                {
                    if (best >= 0.5f)
                    {
                        lit = bestLit;
                    }

                    best = 0f;
                    layer = clipLayer;
                }

                var weight = ClipWeight(show.clips, row, time);
                if (weight <= best)
                {
                    continue;
                }

                var index = IndexInGroup(show, (int)show.clips[row + AlpsShowLayout.ClipGroup], fixture);
                if (index >= 0 && ClipWrites(show, row, index, out var clipLit))
                {
                    best = weight;
                    bestLit = clipLit;
                }
            }

            return best >= 0.5f ? bestLit : lit;
        }

        /// <summary>
        /// The times fixture <paramref name="fixture"/> switches its projection, each one
        /// flipping it, starting from <paramref name="litAtStart"/> at time 0. After the last
        /// clip nothing covers it, so it ends lit.
        /// </summary>
        public static List<float> Switches(AlpsCompiledShow show, int fixture, out bool litAtStart)
        {
            var stride = AlpsShowLayout.ClipStride;
            var bounds = new List<float> { 0f };
            var mixes = new List<Vector2>();
            var end = 0f;
            for (var clip = 0; clip < show.ClipCount; clip++)
            {
                var row = clip * stride;
                if (IndexInGroup(show, (int)show.clips[row + AlpsShowLayout.ClipGroup], fixture) < 0)
                {
                    continue;
                }

                var start = show.clips[row + AlpsShowLayout.ClipStart];
                var clipEnd = show.clips[row + AlpsShowLayout.ClipEnd];
                var mixIn = Mathf.Max(0f, show.clips[row + AlpsShowLayout.ClipMixInDuration]);
                var mixOut = Mathf.Max(0f, show.clips[row + AlpsShowLayout.ClipMixOutDuration]);
                bounds.Add(start);
                bounds.Add(clipEnd);
                if (mixIn > 0f)
                {
                    bounds.Add(start + mixIn);
                    mixes.Add(new Vector2(start, start + mixIn));
                }

                if (mixOut > 0f)
                {
                    bounds.Add(clipEnd - mixOut);
                    mixes.Add(new Vector2(clipEnd - mixOut, clipEnd));
                }

                end = Mathf.Max(end, clipEnd);
            }

            // Past the last clip the default holds, so one more stretch brings it back.
            bounds.Add(end + 1f);
            bounds.Sort();

            var switches = new List<float>();
            litAtStart = IsLit(show, fixture, 0f);
            var lit = litAtStart;
            for (var i = 0; i + 1 < bounds.Count; i++)
            {
                var from = bounds[i];
                var to = bounds[i + 1];
                if (to <= from)
                {
                    continue;
                }

                // Outside every mix no weight changes between two bounds, so one sample in the
                // middle tells the whole stretch. Inside one the heaviest clip can change, so
                // it is sampled in steps.
                var steps = Mixing(mixes, from, to) ? Mathf.Max(1, Mathf.CeilToInt((to - from) / MixSampleSeconds)) : 1;
                var step = (to - from) / steps;
                for (var s = 0; s < steps; s++)
                {
                    var sampled = IsLit(show, fixture, from + (s + 0.5f) * step);
                    if (sampled != lit)
                    {
                        switches.Add(from + s * step);
                        lit = sampled;
                    }
                }
            }

            return switches;
        }

        private static bool Mixing(List<Vector2> mixes, float from, float to)
        {
            foreach (var mix in mixes)
            {
                if (mix.x < to && mix.y > from)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a clip writes the switch for the member at <paramref name="index"/>, the last gobo effect winning.</summary>
        private static bool ClipWrites(AlpsCompiledShow show, int row, int index, out bool lit)
        {
            lit = true;
            var written = false;
            var positionRow = (int)show.clips[row + AlpsShowLayout.ClipPositionStart] + index * 2;
            var k = positionRow < show.positions.Length ? show.positions[positionRow] : index;
            // Odd and even count order positions from 1, as the evaluator does.
            var isOdd = k % 2 == 0;

            var effectStart = (int)show.clips[row + AlpsShowLayout.ClipEffectStart];
            var effectEnd = effectStart + (int)show.clips[row + AlpsShowLayout.ClipEffectCount];
            for (var e = effectStart; e < effectEnd; e++)
            {
                var effectRow = e * AlpsShowLayout.EffectStride;
                if ((int)show.effects[effectRow + AlpsShowLayout.EffectKind] != (int)AlpsEffectKind.Gobo)
                {
                    continue;
                }

                var parity = (int)show.effects[effectRow + AlpsShowLayout.EffectParity];
                if ((parity == (int)AlpsParity.Odd && !isOdd) || (parity == (int)AlpsParity.Even && isOdd))
                {
                    continue;
                }

                written = true;
                lit = show.effects[effectRow + AlpsShowLayout.EffectScalarC] < 0.5f;
            }

            return written;
        }

        private static int IndexInGroup(AlpsCompiledShow show, int group, int fixture)
        {
            if (group < 0 || fixture < 0 || fixture >= show.fixtureCount || group * show.fixtureCount >= show.groupIndex.Length)
            {
                return -1;
            }

            return show.groupIndex[group * show.fixtureCount + fixture];
        }

        /// <summary>The evaluator's Timeline style weight from the sampled mix curves, 0 outside the clip.</summary>
        private static float ClipWeight(float[] clips, int row, float time)
        {
            var start = clips[row + AlpsShowLayout.ClipStart];
            var end = clips[row + AlpsShowLayout.ClipEnd];
            if (time < start || time > end)
            {
                return 0f;
            }

            var weight = 1f;
            var mixIn = clips[row + AlpsShowLayout.ClipMixInDuration];
            if (mixIn > 0f && time < start + mixIn)
            {
                weight *= SampleCurve(clips, row + AlpsShowLayout.ClipMixInCurve, (time - start) / mixIn);
            }

            var mixOut = clips[row + AlpsShowLayout.ClipMixOutDuration];
            if (mixOut > 0f && time > end - mixOut)
            {
                weight *= SampleCurve(clips, row + AlpsShowLayout.ClipMixOutCurve, (time - (end - mixOut)) / mixOut);
            }

            return Mathf.Clamp01(weight);
        }

        private static float SampleCurve(float[] clips, int offset, float t)
        {
            var x = Mathf.Clamp01(t) * (AlpsShowLayout.CurveSamples - 1);
            var i = Mathf.Min(AlpsShowLayout.CurveSamples - 2, Mathf.FloorToInt(x));
            return Mathf.Lerp(clips[offset + i], clips[offset + i + 1], x - i);
        }
    }
}
