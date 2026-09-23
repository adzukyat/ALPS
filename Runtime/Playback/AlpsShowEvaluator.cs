using UnityEngine;

#if UDONSHARP
using UdonSharp;
#endif

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// The one evaluator for a compiled show. Editor preview and the Udon player both call
    /// these static methods, so what the timeline shows is what plays in VRChat.
    ///
    /// Everything here stays inside what UdonSharp can compile: static methods over flat
    /// arrays, constants and Mathf. No lists, structs, generics, ref or out parameters.
    /// Ints travel inside float arrays and are read back with <see cref="ToInt"/>.
    /// </summary>
#if UDONSHARP
    public class AlpsShowEvaluator : UdonSharpBehaviour
#else
    public class AlpsShowEvaluator : MonoBehaviour
#endif
    {
        // --- Enum values, mirrored from the serialized model ------------------------------

        public const int PhaseWave = 0;
        public const int PhaseRandom = 1;

        public const int OrderNormal = 0;
        public const int OrderReverse = 1;
        public const int OrderSymmetric = 2;
        public const int OrderRandom = 3;

        public const int TimingWithinCycle = 0;
        public const int TimingPerCycle = 1;

        public const int KindMove = 0;
        public const int KindCone = 1;
        public const int KindColor = 2;
        public const int KindBrightness = 3;
        public const int KindFlicker = 4;
        public const int KindGobo = 5;

        public const int ParityAll = 0;
        public const int ParityEven = 1;
        public const int ParityOdd = 2;

        public const int MoveAngle = 0;
        public const int MoveCircle = 1;
        public const int MoveTrackUser = 2;

        public const int GoboOff = 1;

        // --- Phase block, shared by clips and own phase parameters ------------------------

        public const int PhaseMode = 0;
        public const int PhaseEase = 1;
        /// <summary>Share of a wave cycle spent rising from 0 to 1.</summary>
        public const int PhaseRise = 2;
        /// <summary>Share of a wave cycle held at 1 after the rise.</summary>
        public const int PhaseHoldHigh = 3;
        /// <summary>Share of a wave cycle spent falling back to 0. The rest is held at 0.</summary>
        public const int PhaseFall = 4;
        public const int PhaseGroupSize = 5;
        /// <summary>
        /// Delay per order position, in cycles. The model stores the spread over the whole
        /// group and the compiler divides it once, so the evaluator only sees the step.
        /// </summary>
        public const int PhaseDelay = 6;
        public const int PhaseBeatsPerCycle = 7;
        public const int PhaseInverse = 8;
        /// <summary>Ease of the fall. <see cref="PhaseEase"/> shapes the rise.</summary>
        public const int PhaseFallEase = 9;
        public const int PhaseStride = 10;

        // --- Clip rows ---------------------------------------------------------------------

        public const int ClipStart = 0;
        public const int ClipEnd = 1;
        public const int ClipMixInDuration = 2;
        public const int ClipMixOutDuration = 3;
        public const int ClipLayer = 4;
        public const int ClipGroup = 5;
        public const int ClipEffectStart = 6;
        public const int ClipEffectCount = 7;
        public const int ClipOrder = 8;
        public const int ClipSeed = 9;
        public const int ClipBpm = 10;
        /// <summary>Beats the clip's own fade takes to bring its weight in after its start.</summary>
        public const int ClipFadeIn = 11;
        /// <summary>Beats the clip's own fade takes to take its weight out before its end.</summary>
        public const int ClipFadeOut = 12;
        /// <summary>
        /// Row in the positions table where this clip's group members start. Each member takes
        /// two entries, its order position k and 1 when its pan is mirrored.
        /// </summary>
        public const int ClipPositionStart = 13;
        /// <summary>1 when some value on the clip follows the clip's shared phase, so it is worth working out.</summary>
        public const int ClipUsesPhase = 14;
        public const int ClipPhase = 15;
        public const int CurveSamples = 16;
        public const int ClipMixInCurve = ClipPhase + PhaseStride;
        public const int ClipMixOutCurve = ClipMixInCurve + CurveSamples;
        public const int ClipStride = ClipMixOutCurve + CurveSamples;

        // --- Effect rows -------------------------------------------------------------------

        public const int EffectKind = 0;
        public const int EffectParity = 1;
        public const int EffectParamStart = 2;
        public const int EffectPaletteStart = 3;
        public const int EffectPaletteCount = 4;
        /// <summary>Move mode, brightness blackout on return, flicker speed, gobo beats per turn.</summary>
        public const int EffectScalarA = 5;
        /// <summary>Pan/tilt phase offset, brightness blackout fade in, flicker strength, gobo stagger degrees.</summary>
        public const int EffectScalarB = 6;
        /// <summary>Track speed, brightness blackout fade out, flicker stagger.</summary>
        public const int EffectScalarC = 7;
        /// <summary>Tracked user name index.</summary>
        public const int EffectScalarD = 8;
        /// <summary>Circle width over height.</summary>
        public const int EffectScalarE = 9;
        /// <summary>
        /// Share of a cycle every value on the effect runs late by. One side of an even / odd
        /// pair at 0.5 takes turns with the other.
        /// </summary>
        public const int EffectPhaseOffset = 10;
        public const int EffectStride = 11;

        // --- Parameter rows ----------------------------------------------------------------

        /// <summary>The value, or the first order position's value while spread is on.</summary>
        public const int ParamValue = 0;
        /// <summary>The range's ends, or the first order position's ends while spread is on.</summary>
        public const int ParamRangeMin = 1;
        public const int ParamRangeMax = 2;
        public const int ParamIsRange = 3;
        /// <summary>
        /// Step per order position. The model stores the values at the first and last position
        /// and the compiler divides them once, so the evaluator only sees the step.
        /// </summary>
        public const int ParamSpread = 4;
        /// <summary>The step at each end of the range.</summary>
        public const int ParamSpreadMin = 5;
        public const int ParamSpreadMax = 6;
        public const int ParamHasSpread = 7;
        public const int ParamTiming = 8;
        public const int ParamUseOwnPhase = 9;
        public const int ParamOwnPhase = 10;
        public const int ParamStride = ParamOwnPhase + PhaseStride;

        // --- Color palette rows ------------------------------------------------------------

        public const int ColorIsGradient = 0;
        public const int ColorSolid = 1;
        public const int ColorGradient = 4;
        public const int ColorStride = ColorGradient + CurveSamples * 3;

        // --- Frame channels ----------------------------------------------------------------

        public const int FramePan = 0;
        public const int FrameTilt = 1;
        /// <summary>Brightness on the 0..100 scale.</summary>
        public const int FrameBrightness = 2;
        public const int FrameRed = 3;
        public const int FrameGreen = 4;
        public const int FrameBlue = 5;
        public const int FrameConeWidth = 6;
        public const int FrameConeLength = 7;
        public const int FrameGobo = 8;
        public const int FrameGoboRotation = 9;
        /// <summary>Flicker multiplier on brightness, 1 when nothing flickers.</summary>
        public const int FrameBrightnessScale = 10;
        /// <summary>Row of the tracking move effect plus one, 0 when not tracking.</summary>
        public const int FrameTrackEffect = 11;
        /// <summary>
        /// The fixture's own cone mesh length. No effect writes it, so it keeps the captured default
        /// and lets the adapter stretch the mesh relative to it when the cone runs past its full length.
        /// </summary>
        public const int FrameConeMeshLength = 12;
        public const int FrameStride = 13;

        // ==================================================================================
        // Scalar helpers
        // ==================================================================================

        public static int ToInt(float value)
        {
            return Mathf.RoundToInt(value);
        }

        /// <summary>Normalized easing, 0..1 in and out (overshooting curves may leave the range).</summary>
        public static float Ease(int type, float t)
        {
            t = Mathf.Clamp01(t);
            if (type == 1) return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
            if (type == 2) return Mathf.Sin(t * Mathf.PI * 0.5f);
            if (type == 3) return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
            if (type == 4) return t * t;
            if (type == 5) return 1f - (1f - t) * (1f - t);
            if (type == 6) return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
            if (type == 7) return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
            if (type == 8) return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
            if (type == 9) return 1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f);
            if (type == 10) return OutBounce(t);
            if (type == 11) return t * t * t;
            if (type == 12) return 1f - Mathf.Pow(1f - t, 3f);
            if (type == 13) return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
            if (type == 14) return 2.70158f * t * t * t - 1.70158f * t * t;
            if (type == 15)
            {
                var c2 = 1.70158f * 1.525f;
                return t < 0.5f
                    ? Mathf.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2) * 0.5f
                    : (Mathf.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) * 0.5f;
            }

            if (type == 16) return 1f - OutBounce(1f - t);
            if (type == 17)
            {
                return t < 0.5f ? (1f - OutBounce(1f - 2f * t)) * 0.5f : (1f + OutBounce(2f * t - 1f)) * 0.5f;
            }

            return t;
        }

        private static float OutBounce(float t)
        {
            if (t < 1f / 2.75f) return 7.5625f * t * t;
            if (t < 2f / 2.75f) { t -= 1.5f / 2.75f; return 7.5625f * t * t + 0.75f; }
            if (t < 2.5f / 2.75f) { t -= 2.25f / 2.75f; return 7.5625f * t * t + 0.9375f; }
            t -= 2.625f / 2.75f;
            return 7.5625f * t * t + 0.984375f;
        }

        /// <summary>
        /// Deterministic 0..1 hash of an integer pair. Float based on purpose, since integer
        /// overflow is not something to rely on inside Udon.
        /// </summary>
        public static float Hash01(int a, int b)
        {
            var x = Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }

        /// <summary>Beats elapsed since the clip started. Every clip counts its own beats.</summary>
        public static float Beats(float time, float bpm, float clipStart)
        {
            return (time - clipStart) * Mathf.Max(0f, bpm) / 60f;
        }

        /// <summary>How many fixture groups a group of <paramref name="fixtureCount"/> fixtures forms.</summary>
        public static int GroupCount(int fixtureCount, int groupSize)
        {
            var size = Mathf.Max(1, groupSize);
            return Mathf.Max(1, (Mathf.Max(1, fixtureCount) + size - 1) / size);
        }

        /// <summary>
        /// Order position k of the fixture at list index <paramref name="fixtureIndex"/>.
        /// Grouping is applied before the symmetric fold. Symmetric counts outward from the
        /// center, so a positive spread starts in the middle and a value spread puts its first
        /// value there and its last at the edges.
        /// The compiler writes every clip's positions into the positions table once, so
        /// playback never runs this, and a random order costs no more than any other.
        /// </summary>
        public static int OrderPosition(int order, int seed, int fixtureIndex, int fixtureCount, int groupSize)
        {
            var groups = GroupCount(fixtureCount, groupSize);
            var group = Mathf.Clamp(Mathf.Max(0, fixtureIndex) / Mathf.Max(1, groupSize), 0, groups - 1);

            if (order == OrderReverse)
            {
                return groups - 1 - group;
            }

            if (order == OrderSymmetric)
            {
                return Mathf.Max(group, groups - 1 - group) - groups / 2;
            }

            if (order == OrderRandom)
            {
                // Rank of this group's hash among all groups: a seeded permutation without arrays.
                var own = Hash01(group, seed);
                var rank = 0;
                for (var other = 0; other < groups; other++)
                {
                    var value = Hash01(other, seed);
                    if (value < own || (value == own && other < group))
                    {
                        rank++;
                    }
                }

                return rank;
            }

            return group;
        }

        /// <summary>
        /// True for fixtures on the first half of a symmetric order. Their pan is mirrored,
        /// so both halves turn away from the center together. A middle fixture is not mirrored.
        /// </summary>
        public static bool IsMirrored(int order, int fixtureIndex, int fixtureCount, int groupSize)
        {
            if (order != OrderSymmetric)
            {
                return false;
            }

            var groups = GroupCount(fixtureCount, groupSize);
            var group = Mathf.Clamp(Mathf.Max(0, fixtureIndex) / Mathf.Max(1, groupSize), 0, groups - 1);
            return 2 * group < groups - 1;
        }

        /// <summary>
        /// How many order positions a spread is divided across. Normal, reverse and random
        /// run over every fixture group, so a spread of 1 travels the whole group in one
        /// cycle. Symmetric counts outward from the middle and only reaches the edge, so it
        /// is divided by that half instead.
        /// </summary>
        public static int SpreadPositions(int order, int fixtureCount, int groupSize)
        {
            var groups = GroupCount(fixtureCount, groupSize);
            if (order == OrderSymmetric)
            {
                return Mathf.Max(1, groups - groups / 2);
            }

            return groups;
        }

        /// <summary>
        /// The per order position delay a spread comes to for one group. The compiler
        /// applies this while it encodes the phase row, so the arrays already hold the step.
        /// </summary>
        public static float DelayFromSpread(float spread, int order, int fixtureCount, int groupSize)
        {
            return spread / Mathf.Max(1, SpreadPositions(order, fixtureCount, groupSize));
        }

        /// <summary>
        /// The per order position step of a value spread that runs from <paramref name="first"/>
        /// at the first position to <paramref name="last"/> at the last. A lone position has no
        /// step and keeps the first value. The compiler applies this like
        /// <see cref="DelayFromSpread"/>.
        /// </summary>
        public static float StepFromSpread(float first, float last, int order, int fixtureCount, int groupSize)
        {
            var positions = SpreadPositions(order, fixtureCount, groupSize);
            return positions > 1 ? (last - first) / (positions - 1) : 0f;
        }

        /// <summary>Cycles elapsed for a fixture at order position k, before wrapping.</summary>
        public static float FixtureCycles(float beats, float beatsPerCycle, float delay, int k, float extraCycles)
        {
            var cycles = beatsPerCycle > 0f ? beats / beatsPerCycle : 0f;
            return cycles - delay * k + extraCycles;
        }

        /// <summary>
        /// Phase φ in 0..1 from unwrapped cycles. A wave walks one cycle of
        /// <see cref="Wave"/>, random is a smooth seeded wander. Invert and the eases apply to
        /// the wave only. Invert flips the eased wave upside down.
        /// </summary>
        public static float Phase(int mode, int riseEase, int fallEase, float rise, float holdHigh, float fall, bool inverse, float cycles, int k, int seed)
        {
            if (mode == PhaseRandom)
            {
                return Mathf.Clamp01(Mathf.PerlinNoise(cycles * 2f, (k + 1) * 7.31f + seed * 0.137f));
            }

            var u = Wave(riseEase, fallEase, rise, holdHigh, fall, cycles - Mathf.Floor(cycles));
            return inverse ? 1f - u : u;
        }

        /// <summary><see cref="Phase"/> for the phase block at <paramref name="row"/> of <paramref name="data"/>.</summary>
        public static float PhaseAt(float[] data, int row, float cycles, int k, int seed)
        {
            return Phase(
                ToInt(data[row + PhaseMode]),
                ToInt(data[row + PhaseEase]),
                ToInt(data[row + PhaseFallEase]),
                data[row + PhaseRise],
                data[row + PhaseHoldHigh],
                data[row + PhaseFall],
                data[row + PhaseInverse] > 0.5f,
                cycles,
                k,
                seed);
        }

        /// <summary>
        /// One wave cycle at <paramref name="u"/> in 0..1: a rise from 0 to 1, a hold at 1, a
        /// fall back to 0, and a hold at 0 for whatever the three leave over. A share of zero
        /// skips its part, so a rise of 0 jumps straight up and a rise of 1 is a sawtooth.
        /// The rise follows its ease forwards. The fall follows its own ease forwards in time
        /// on the way down, so an OutBounce fall bounces at the bottom.
        /// </summary>
        public static float Wave(int riseEase, int fallEase, float rise, float holdHigh, float fall, float u)
        {
            var riseEnd = Mathf.Clamp01(rise);
            var highEnd = riseEnd + Mathf.Clamp(holdHigh, 0f, 1f - riseEnd);
            var fallEnd = highEnd + Mathf.Clamp(fall, 0f, 1f - highEnd);
            if (u < riseEnd)
            {
                return Ease(riseEase, u / riseEnd);
            }

            if (u < highEnd)
            {
                return 1f;
            }

            if (u < fallEnd)
            {
                return 1f - Ease(fallEase, (u - highEnd) / (fallEnd - highEnd));
            }

            return 0f;
        }

        /// <summary>
        /// True while a wave is on its return leg: the fall and the low hold after it. A wave
        /// whose rise and high hold fill the whole cycle never returns.
        /// </summary>
        public static bool IsReturnLeg(int mode, float rise, float holdHigh, float cycles)
        {
            if (mode != PhaseWave)
            {
                return false;
            }

            var outbound = OutboundLeg(rise, holdHigh);
            var u = cycles - Mathf.Floor(cycles);
            return outbound < 1f && u >= outbound;
        }

        /// <summary>The share of a wave cycle before the return leg: the rise and the high hold.</summary>
        public static float OutboundLeg(float rise, float holdHigh)
        {
            var riseEnd = Mathf.Clamp01(rise);
            return riseEnd + Mathf.Clamp(holdHigh, 0f, 1f - riseEnd);
        }

        public static int CycleIndex(float cycles)
        {
            return Mathf.FloorToInt(cycles);
        }

        // ==================================================================================
        // Clip weight
        // ==================================================================================

        /// <summary>
        /// Timeline style weight of a clip at <paramref name="time"/>, from its sampled mix
        /// in and mix out curves. Zero outside the clip.
        /// </summary>
        public static float ClipWeight(float[] clips, int clip, float time)
        {
            var row = clip * ClipStride;
            var start = clips[row + ClipStart];
            var end = clips[row + ClipEnd];
            if (time < start || time > end)
            {
                return 0f;
            }

            var weight = 1f;
            var mixIn = clips[row + ClipMixInDuration];
            if (mixIn > 0f && time < start + mixIn)
            {
                weight *= SampleCurve(clips, row + ClipMixInCurve, (time - start) / mixIn);
            }

            var mixOut = clips[row + ClipMixOutDuration];
            if (mixOut > 0f && time > end - mixOut)
            {
                weight *= SampleCurve(clips, row + ClipMixOutCurve, (time - (end - mixOut)) / mixOut);
            }

            return Mathf.Clamp01(weight);
        }

        /// <summary>
        /// The clip's own fade envelope at <paramref name="time"/>: rises linearly from 0 over
        /// the fade in beats after the start and falls to 0 over the fade out beats before the
        /// end. Fades longer than the clip meet in a triangle. It scales the clip's weight in
        /// counted beats, where Timeline's own mix in and out is set in seconds.
        /// </summary>
        public static float ClipFade(float[] clips, int clip, float time)
        {
            var row = clip * ClipStride;
            var bpm = clips[row + ClipBpm];
            var scale = 1f;

            var fadeIn = clips[row + ClipFadeIn];
            if (fadeIn > 0f)
            {
                scale = Mathf.Min(scale, Beats(time, bpm, clips[row + ClipStart]) / fadeIn);
            }

            var fadeOut = clips[row + ClipFadeOut];
            if (fadeOut > 0f)
            {
                scale = Mathf.Min(scale, Beats(clips[row + ClipEnd], bpm, time) / fadeOut);
            }

            return Mathf.Clamp01(scale);
        }

        private static float SampleCurve(float[] data, int offset, float t)
        {
            var x = Mathf.Clamp01(t) * (CurveSamples - 1);
            var i = Mathf.Min(CurveSamples - 2, Mathf.FloorToInt(x));
            return Mathf.Lerp(data[offset + i], data[offset + i + 1], x - i);
        }

        /// <summary>
        /// The time bucket <paramref name="time"/> falls in. Times outside the show fall in
        /// the first or last bucket, whose clips then all weigh 0.
        /// </summary>
        public static int TimeBucket(int[] bucketStart, float bucketSeconds, float time)
        {
            var buckets = bucketStart.Length - 1;
            if (buckets <= 0 || bucketSeconds <= 0f)
            {
                return -1;
            }

            return Mathf.Clamp(Mathf.FloorToInt(time / bucketSeconds), 0, buckets - 1);
        }

        /// <summary>
        /// Collects the clips with a weight at <paramref name="time"/> into
        /// <paramref name="active"/> and their weights, fade included, into
        /// <paramref name="activeWeight"/>, and returns how many there are. Only the clips of
        /// the time bucket are looked at. Each bucket lists its clips in clip order, so the
        /// result stays sorted by layer. Weights do not depend on the fixture, so this runs
        /// once per frame for all of them.
        /// </summary>
        public static int ActiveClips(
            float[] clips,
            int[] bucketStart,
            int[] bucketClips,
            float bucketSeconds,
            float time,
            int[] active,
            float[] activeWeight)
        {
            var bucket = TimeBucket(bucketStart, bucketSeconds, time);
            if (bucket < 0)
            {
                return 0;
            }

            var count = 0;
            var end = bucketStart[bucket + 1];
            for (var i = bucketStart[bucket]; i < end; i++)
            {
                var clip = bucketClips[i];
                var weight = ClipWeight(clips, clip, time);
                if (weight <= 0f)
                {
                    continue;
                }

                weight *= ClipFade(clips, clip, time);
                if (weight <= 0f)
                {
                    continue;
                }

                active[count] = clip;
                activeWeight[count] = weight;
                count++;
            }

            return count;
        }

        // ==================================================================================
        // Clip evaluation
        // ==================================================================================

        /// <summary>
        /// Evaluates one clip for one fixture. Writes channel values into
        /// <paramref name="frame"/> and 1 into <paramref name="written"/> for every channel
        /// the clip drives. Channels the clip leaves alone are not touched.
        /// <paramref name="fixtureIndex"/> is the fixture's position inside the clip's group,
        /// and its order position comes from <paramref name="positions"/>.
        /// </summary>
        public static void EvaluateClip(
            float[] clips,
            float[] effects,
            float[] parameters,
            float[] colors,
            float[] gobos,
            int[] positions,
            int clip,
            int fixtureIndex,
            float time,
            float[] frame,
            float[] written,
            float[] scratch)
        {
            var row = clip * ClipStride;
            var phaseRow = row + ClipPhase;
            var seed = ToInt(clips[row + ClipSeed]);
            var beats = Beats(time, clips[row + ClipBpm], clips[row + ClipStart]);
            var positionRow = ToInt(clips[row + ClipPositionStart]) + fixtureIndex * 2;
            var k = positions[positionRow];
            var mirrored = positions[positionRow + 1] != 0;
            var isOdd = fixtureIndex % 2 == 0;

            // The clip's shared phase without a phase offset, worked out once for every value
            // that follows it. The compiler marks the clips where nothing does.
            var shared = FixtureCycles(beats, clips[phaseRow + PhaseBeatsPerCycle], clips[phaseRow + PhaseDelay], k, 0f);
            var sharedPhase = clips[row + ClipUsesPhase] > 0.5f ? PhaseAt(clips, phaseRow, shared, k, seed) : 0f;

            var effectStart = ToInt(clips[row + ClipEffectStart]);
            var effectEnd = effectStart + ToInt(clips[row + ClipEffectCount]);
            for (var e = effectStart; e < effectEnd; e++)
            {
                var effectRow = e * EffectStride;
                var parity = ToInt(effects[effectRow + EffectParity]);
                if ((parity == ParityOdd && !isOdd) || (parity == ParityEven && isOdd))
                {
                    continue;
                }

                var kind = ToInt(effects[effectRow + EffectKind]);
                var paramStart = ToInt(effects[effectRow + EffectParamStart]);

                // The card's phase offset runs every value on it late by that share of a cycle.
                var late = -effects[effectRow + EffectPhaseOffset];

                if (kind == KindMove)
                {
                    EvaluateMove(clips, effects, parameters, row, e, paramStart, k, mirrored, beats, seed, late, shared, sharedPhase, frame, written);
                }
                else if (kind == KindCone)
                {
                    frame[FrameConeWidth] = ResolveValue(clips, parameters, row, paramStart, k, beats, seed, late, shared, sharedPhase);
                    written[FrameConeWidth] = 1f;
                    frame[FrameConeLength] = ResolveValue(clips, parameters, row, paramStart + 1, k, beats, seed, late, shared, sharedPhase);
                    written[FrameConeLength] = 1f;
                }
                else if (kind == KindBrightness)
                {
                    var brightness = ResolveValue(clips, parameters, row, paramStart, k, beats, seed, late, shared, sharedPhase);
                    if (effects[effectRow + EffectScalarA] > 0.5f)
                    {
                        // Blackout on return: dark on the return leg, still covering lower layers.
                        brightness *= BlackoutScale(
                            clips,
                            parameters,
                            row,
                            paramStart,
                            beats,
                            k,
                            late,
                            shared,
                            effects[effectRow + EffectScalarB],
                            effects[effectRow + EffectScalarC]);
                    }

                    frame[FrameBrightness] = brightness;
                    written[FrameBrightness] = 1f;
                }
                else if (kind == KindColor)
                {
                    EvaluateColor(clips, effects, parameters, colors, row, e, paramStart, k, beats, seed, late, shared, sharedPhase, frame, written, scratch);
                }
                else if (kind == KindFlicker)
                {
                    var speed = effects[effectRow + EffectScalarA];
                    var strength = Mathf.Clamp01(effects[effectRow + EffectScalarB]);
                    var stagger = effects[effectRow + EffectScalarC];
                    var noise = Mathf.Clamp01(Mathf.PerlinNoise(time * speed + fixtureIndex * stagger, 3.7f + seed * 0.071f));
                    frame[FrameBrightnessScale] = 1f - strength * noise;
                    written[FrameBrightnessScale] = 1f;
                }
                else if (kind == KindGobo)
                {
                    EvaluateGobo(clips, effects, parameters, gobos, row, e, paramStart, k, fixtureIndex, beats, seed, late, shared, sharedPhase, frame, written, scratch);
                }
            }
        }

        private static void EvaluateMove(
            float[] clips,
            float[] effects,
            float[] parameters,
            int clipRow,
            int effect,
            int paramStart,
            int k,
            bool mirrored,
            float beats,
            int seed,
            float extraCycles,
            float shared,
            float sharedPhase,
            float[] frame,
            float[] written)
        {
            var effectRow = effect * EffectStride;
            var mode = ToInt(effects[effectRow + EffectScalarA]);
            if (mode == MoveTrackUser)
            {
                // Tracking is resolved by the player, and the order does not apply.
                frame[FrameTrackEffect] = effect + 1;
                written[FrameTrackEffect] = 1f;
                return;
            }

            // An angle move above a tracking move takes the head back from the player.
            frame[FrameTrackEffect] = 0f;
            written[FrameTrackEffect] = 1f;

            if (mode == MoveCircle)
            {
                EvaluateCircle(clips, effects, parameters, clipRow, effectRow, paramStart, k, beats, seed, extraCycles, shared, sharedPhase, frame);
            }
            else
            {
                var tiltRow = paramStart * ParamStride;
                var panRow = (paramStart + 1) * ParamStride;
                var bothRanged = parameters[tiltRow + ParamIsRange] > 0.5f && parameters[panRow + ParamIsRange] > 0.5f;
                var panOffsetCycles = bothRanged ? effects[effectRow + EffectScalarB] / 360f : 0f;

                frame[FrameTilt] = ResolveValue(clips, parameters, clipRow, paramStart, k, beats, seed, extraCycles, shared, sharedPhase);
                frame[FramePan] = ResolveValue(clips, parameters, clipRow, paramStart + 1, k, beats, seed, extraCycles + panOffsetCycles, shared, sharedPhase);
            }

            written[FrameTilt] = 1f;
            written[FramePan] = 1f;
            if (mirrored)
            {
                frame[FramePan] = -frame[FramePan];
            }
        }

        /// <summary>
        /// Turns the beam around a center direction at a fixed opening angle, so the ring
        /// stays a ring wherever the center points.
        ///
        /// Sweeping pan and tilt with two offset waves instead would fold into a figure eight
        /// as soon as the tilt passes the fixture's own axis, where pan no longer moves the beam.
        /// The clip's shared phase drives the turn, so the spread walks the fixtures around the ring.
        /// Center tilt, center pan and radius are parameters, so they range, spread and take
        /// their own phase like any other value.
        /// </summary>
        private static void EvaluateCircle(
            float[] clips,
            float[] effects,
            float[] parameters,
            int clipRow,
            int effectRow,
            int paramStart,
            int k,
            float beats,
            int seed,
            float extraCycles,
            float shared,
            float sharedPhase,
            float[] frame)
        {
            var tiltParam = paramStart + 2;
            var panParam = paramStart + 3;
            var radiusParam = paramStart + 4;

            var phase = extraCycles == 0f ? sharedPhase : PhaseAt(clips, clipRow + ClipPhase, shared + extraCycles, k, seed);

            var centerTilt = ResolveValue(clips, parameters, clipRow, tiltParam, k, beats, seed, extraCycles, shared, sharedPhase) * Mathf.Deg2Rad;
            var centerPan = ResolveValue(clips, parameters, clipRow, panParam, k, beats, seed, extraCycles, shared, sharedPhase) * Mathf.Deg2Rad;
            var radius = ResolveValue(clips, parameters, clipRow, radiusParam, k, beats, seed, extraCycles, shared, sharedPhase);
            var aspect = effects[effectRow + EffectScalarE];

            // The center direction and the two axes across it, all turned by the center angles.
            var sinTilt = Mathf.Sin(centerTilt);
            var cosTilt = Mathf.Cos(centerTilt);
            var sinPan = Mathf.Sin(centerPan);
            var cosPan = Mathf.Cos(centerPan);
            var centerX = -sinTilt * sinPan;
            var centerY = -cosTilt;
            var centerZ = -sinTilt * cosPan;
            var acrossX = cosPan;
            var acrossZ = -sinPan;
            var upX = cosTilt * sinPan;
            var upY = -sinTilt;
            var upZ = cosTilt * cosPan;

            var turn = phase * 2f * Mathf.PI;
            var across = radius * aspect * Mathf.Cos(turn);
            var up = radius * Mathf.Sin(turn);
            var opening = Mathf.Sqrt(across * across + up * up);

            var dirX = centerX;
            var dirY = centerY;
            var dirZ = centerZ;
            if (opening > 0.0001f)
            {
                // One unit step across the center direction, then lean that far away from it.
                var stepX = (across * acrossX + up * upX) / opening;
                var stepY = up * upY / opening;
                var stepZ = (across * acrossZ + up * upZ) / opening;
                var lean = Mathf.Sin(opening * Mathf.Deg2Rad);
                var keep = Mathf.Cos(opening * Mathf.Deg2Rad);
                dirX = keep * centerX + lean * stepX;
                dirY = keep * centerY + lean * stepY;
                dirZ = keep * centerZ + lean * stepZ;
            }

            frame[FrameTilt] = Mathf.Acos(Mathf.Clamp(-dirY, -1f, 1f)) * Mathf.Rad2Deg;
            frame[FramePan] = Mathf.Atan2(-dirX, -dirZ) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Brightness multiplier for blackout on return, following the phase that governs
        /// <paramref name="param"/>. 0 on the return leg of a wave, the fall and the low hold
        /// after it. On the outbound leg it ramps up over <paramref name="fadeIn"/> and down
        /// over <paramref name="fadeOut"/>, both fractions of that leg. 1 when the phase has
        /// no return leg. <paramref name="sharedCycles"/> are the clip's shared cycles without
        /// a phase offset.
        /// </summary>
        public static float BlackoutScale(float[] clips, float[] parameters, int clipRow, int param, float beats, int k, float extraCycles, float sharedCycles, float fadeIn, float fadeOut)
        {
            var paramRow = param * ParamStride;
            var own = parameters[paramRow + ParamUseOwnPhase] > 0.5f;
            var phaseData = own ? parameters : clips;
            var phaseRow = own ? paramRow + ParamOwnPhase : clipRow + ClipPhase;
            var mode = ToInt(phaseData[phaseRow + PhaseMode]);
            var rise = phaseData[phaseRow + PhaseRise];
            var holdHigh = phaseData[phaseRow + PhaseHoldHigh];
            var outbound = OutboundLeg(rise, holdHigh);
            if (mode != PhaseWave || outbound >= 1f)
            {
                return 1f;
            }

            var cycles = CyclesOf(parameters, paramRow, beats, k, extraCycles, sharedCycles);
            if (IsReturnLeg(mode, rise, holdHigh, cycles))
            {
                return 0f;
            }

            var u = (cycles - Mathf.Floor(cycles)) / Mathf.Max(0.001f, outbound);
            var scale = 1f;
            if (fadeIn > 0f)
            {
                scale = Mathf.Min(scale, u / fadeIn);
            }

            if (fadeOut > 0f)
            {
                scale = Mathf.Min(scale, (1f - u) / fadeOut);
            }

            return Mathf.Clamp01(scale);
        }

        /// <summary>
        /// Unwrapped cycles for the parameter at <paramref name="paramRow"/>: its own phase if
        /// it has one, otherwise the clip's <paramref name="sharedCycles"/> run late by
        /// <paramref name="extraCycles"/>.
        /// </summary>
        private static float CyclesOf(float[] parameters, int paramRow, float beats, int k, float extraCycles, float sharedCycles)
        {
            if (parameters[paramRow + ParamUseOwnPhase] > 0.5f)
            {
                var own = paramRow + ParamOwnPhase;
                return FixtureCycles(beats, parameters[own + PhaseBeatsPerCycle], parameters[own + PhaseDelay], k, extraCycles);
            }

            return sharedCycles + extraCycles;
        }

        /// <summary>
        /// Phase φ for the parameter at <paramref name="paramRow"/> at <paramref name="cycles"/>
        /// from <see cref="CyclesOf"/>. Without an own phase or a phase offset it is the clip's
        /// <paramref name="sharedPhase"/>, already worked out.
        /// </summary>
        private static float PhaseOf(float[] clips, float[] parameters, int clipRow, int paramRow, float cycles, int k, int seed, float extraCycles, float sharedPhase)
        {
            if (parameters[paramRow + ParamUseOwnPhase] > 0.5f)
            {
                return PhaseAt(parameters, paramRow + ParamOwnPhase, cycles, k, seed);
            }

            return extraCycles == 0f ? sharedPhase : PhaseAt(clips, clipRow + ClipPhase, cycles, k, seed);
        }

        /// <summary>
        /// The value of a numeric parameter for one fixture, spread included. A spread arrives
        /// as the first order position's value plus a step per position, and a range moves the
        /// two together, so it runs from one spread to the other.
        /// </summary>
        public static float ResolveScalar(float[] clips, float[] parameters, int clipRow, int param, int k, float beats, int seed, float extraCycles)
        {
            var phaseRow = clipRow + ClipPhase;
            var shared = FixtureCycles(beats, clips[phaseRow + PhaseBeatsPerCycle], clips[phaseRow + PhaseDelay], k, 0f);
            var sharedPhase = PhaseAt(clips, phaseRow, shared, k, seed);
            return ResolveValue(clips, parameters, clipRow, param, k, beats, seed, extraCycles, shared, sharedPhase);
        }

        /// <summary>
        /// <see cref="ResolveScalar"/> with the clip's shared cycles and phase, without a phase
        /// offset, already worked out by <see cref="EvaluateClip"/>.
        /// </summary>
        private static float ResolveValue(
            float[] clips,
            float[] parameters,
            int clipRow,
            int param,
            int k,
            float beats,
            int seed,
            float extraCycles,
            float shared,
            float sharedPhase)
        {
            var paramRow = param * ParamStride;

            float value;
            float step;
            if (parameters[paramRow + ParamIsRange] > 0.5f)
            {
                var cycles = CyclesOf(parameters, paramRow, beats, k, extraCycles, shared);
                if (ToInt(parameters[paramRow + ParamTiming]) == TimingPerCycle)
                {
                    var atMin = (CycleIndex(cycles) & 1) == 0;
                    value = atMin ? parameters[paramRow + ParamRangeMin] : parameters[paramRow + ParamRangeMax];
                    step = atMin ? parameters[paramRow + ParamSpreadMin] : parameters[paramRow + ParamSpreadMax];
                }
                else
                {
                    var phase = PhaseOf(clips, parameters, clipRow, paramRow, cycles, k, seed, extraCycles, sharedPhase);
                    value = Mathf.LerpUnclamped(parameters[paramRow + ParamRangeMin], parameters[paramRow + ParamRangeMax], phase);
                    step = Mathf.LerpUnclamped(parameters[paramRow + ParamSpreadMin], parameters[paramRow + ParamSpreadMax], phase);
                }
            }
            else
            {
                value = parameters[paramRow + ParamValue];
                step = parameters[paramRow + ParamSpread];
            }

            return parameters[paramRow + ParamHasSpread] > 0.5f ? value + step * k : value;
        }

        /// <summary>
        /// Which palette entry is active and where inside it, for the phasing parameter
        /// <paramref name="param"/> at <paramref name="cycles"/> and <paramref name="phase"/>:
        /// returns the stop index and writes the local 0..1 position into
        /// <paramref name="local"/>[0].
        /// </summary>
        public static int PaletteStop(float[] parameters, int param, int count, float cycles, float phase, float[] local)
        {
            if (count <= 1)
            {
                local[0] = phase;
                return 0;
            }

            if (ToInt(parameters[param * ParamStride + ParamTiming]) == TimingPerCycle)
            {
                local[0] = phase;
                var index = CycleIndex(cycles) % count;
                return index < 0 ? index + count : index;
            }

            var scaled = Mathf.Clamp01(phase) * count;
            var stop = Mathf.Min(count - 1, Mathf.FloorToInt(scaled));
            local[0] = Mathf.Clamp01(scaled - stop);
            return stop;
        }

        private static void EvaluateColor(
            float[] clips,
            float[] effects,
            float[] parameters,
            float[] colors,
            int clipRow,
            int effect,
            int param,
            int k,
            float beats,
            int seed,
            float extraCycles,
            float shared,
            float sharedPhase,
            float[] frame,
            float[] written,
            float[] scratch)
        {
            var effectRow = effect * EffectStride;
            var count = ToInt(effects[effectRow + EffectPaletteCount]);
            if (count <= 0)
            {
                return;
            }

            written[FrameRed] = 1f;
            written[FrameGreen] = 1f;
            written[FrameBlue] = 1f;

            var paramRow = param * ParamStride;
            var colorRow = ToInt(effects[effectRow + EffectPaletteStart]) * ColorStride;
            var t = 0f;
            if (count > 1)
            {
                var cycles = CyclesOf(parameters, paramRow, beats, k, extraCycles, shared);
                var phase = PhaseOf(clips, parameters, clipRow, paramRow, cycles, k, seed, extraCycles, sharedPhase);
                colorRow += PaletteStop(parameters, param, count, cycles, phase, scratch) * ColorStride;
                t = scratch[0];
            }

            if (colors[colorRow + ColorIsGradient] > 0.5f)
            {
                if (count == 1)
                {
                    t = PhaseOf(clips, parameters, clipRow, paramRow, CyclesOf(parameters, paramRow, beats, k, extraCycles, shared), k, seed, extraCycles, sharedPhase);
                }

                var x = Mathf.Clamp01(t) * (CurveSamples - 1);
                var i = Mathf.Min(CurveSamples - 2, Mathf.FloorToInt(x));
                var f = x - i;
                var a = colorRow + ColorGradient + i * 3;
                var b = a + 3;
                frame[FrameRed] = Mathf.Lerp(colors[a], colors[b], f);
                frame[FrameGreen] = Mathf.Lerp(colors[a + 1], colors[b + 1], f);
                frame[FrameBlue] = Mathf.Lerp(colors[a + 2], colors[b + 2], f);
            }
            else
            {
                frame[FrameRed] = colors[colorRow + ColorSolid];
                frame[FrameGreen] = colors[colorRow + ColorSolid + 1];
                frame[FrameBlue] = colors[colorRow + ColorSolid + 2];
            }
        }

        private static void EvaluateGobo(
            float[] clips,
            float[] effects,
            float[] parameters,
            float[] gobos,
            int clipRow,
            int effect,
            int param,
            int k,
            int fixtureIndex,
            float beats,
            int seed,
            float extraCycles,
            float shared,
            float sharedPhase,
            float[] frame,
            float[] written,
            float[] scratch)
        {
            var effectRow = effect * EffectStride;
            var beatsPerTurn = effects[effectRow + EffectScalarA];
            var turn = beatsPerTurn > 0f ? beats / beatsPerTurn : 0f;
            frame[FrameGoboRotation] = Mathf.Repeat(360f * turn + fixtureIndex * effects[effectRow + EffectScalarB], 360f);
            written[FrameGoboRotation] = 1f;

            var count = ToInt(effects[effectRow + EffectPaletteCount]);
            if (count <= 0)
            {
                return;
            }

            written[FrameGobo] = 1f;

            var stop = 0;
            if (count > 1)
            {
                var paramRow = param * ParamStride;
                var cycles = CyclesOf(parameters, paramRow, beats, k, extraCycles, shared);
                var phase = PhaseOf(clips, parameters, clipRow, paramRow, cycles, k, seed, extraCycles, sharedPhase);
                stop = PaletteStop(parameters, param, count, cycles, phase, scratch);
            }

            frame[FrameGobo] = gobos[ToInt(effects[effectRow + EffectPaletteStart]) + stop];
        }

        // ==================================================================================
        // Composition
        // ==================================================================================

        /// <summary>Channels that switch instead of blending.</summary>
        public static bool IsDiscreteChannel(int channel)
        {
            return channel == FrameGobo || channel == FrameTrackEffect;
        }

        /// <summary>
        /// Position of a fixture inside a fixture group, or -1 when it is not a member.
        /// <paramref name="groupIndex"/> holds one row of fixture positions per group.
        /// </summary>
        public static int IndexInGroup(int[] groupCount, int[] groupIndex, int group, int fixture)
        {
            var groups = groupCount.Length;
            if (group < 0 || group >= groups)
            {
                return -1;
            }

            var fixtures = groupIndex.Length / groups;
            if (fixture < 0 || fixture >= fixtures)
            {
                return -1;
            }

            return groupIndex[group * fixtures + fixture];
        }

        /// <summary>
        /// The final frame of one fixture at <paramref name="time"/>, from the clips
        /// <see cref="ActiveClips"/> collected for that time.
        ///
        /// Clips must be sorted by layer. Inside a layer, overlapping clips blend by their
        /// weights. A layer then covers the layers below it by its total weight, per channel,
        /// so a later track only overrides the channels its effects actually drive.
        /// Channels nobody drives keep the fixture default, except brightness, which is dark
        /// until a brightness effect lights it. A clip's own fade scales its weight, so fading
        /// behaves like blending with an empty clip on every channel.
        /// </summary>
        public static void EvaluateFixture(
            float[] clips,
            float[] effects,
            float[] parameters,
            float[] colors,
            float[] gobos,
            int[] positions,
            int[] active,
            float[] activeWeight,
            int activeCount,
            int[] groupCount,
            int[] groupIndex,
            int fixture,
            float time,
            float[] defaults,
            float[] frame,
            float[] clipFrame,
            float[] clipWritten,
            float[] sum,
            float[] weightSum,
            float[] scratch)
        {
            var defaultRow = fixture * FrameStride;
            for (var ch = 0; ch < FrameStride; ch++)
            {
                frame[ch] = defaults[defaultRow + ch];
            }

            frame[FrameBrightness] = 0f;

            var a = 0;
            while (a < activeCount)
            {
                // Layers are whole numbers, so the float columns compare exactly.
                var layer = clips[active[a] * ClipStride + ClipLayer];

                // A clip alone on its layer at full weight covers exactly the channels it
                // drives, so it writes straight into the frame and skips the blending. Nothing
                // in a clip reads the written marks back, so they need no clearing here.
                var next = a + 1;
                if (activeWeight[a] >= 1f && (next >= activeCount || clips[active[next] * ClipStride + ClipLayer] != layer))
                {
                    var lone = active[a];
                    a = next;
                    var loneIndex = IndexInGroup(groupCount, groupIndex, ToInt(clips[lone * ClipStride + ClipGroup]), fixture);
                    if (loneIndex >= 0)
                    {
                        EvaluateClip(clips, effects, parameters, colors, gobos, positions, lone, loneIndex, time, frame, clipWritten, scratch);
                    }

                    continue;
                }

                for (var ch = 0; ch < FrameStride; ch++)
                {
                    sum[ch] = 0f;
                    weightSum[ch] = 0f;
                }

                while (a < activeCount && clips[active[a] * ClipStride + ClipLayer] == layer)
                {
                    var clip = active[a];
                    var weight = activeWeight[a];
                    a++;

                    var group = ToInt(clips[clip * ClipStride + ClipGroup]);
                    var index = IndexInGroup(groupCount, groupIndex, group, fixture);
                    if (index < 0)
                    {
                        continue;
                    }

                    for (var ch = 0; ch < FrameStride; ch++)
                    {
                        clipWritten[ch] = 0f;
                    }

                    EvaluateClip(clips, effects, parameters, colors, gobos, positions, clip, index, time, clipFrame, clipWritten, scratch);

                    for (var ch = 0; ch < FrameStride; ch++)
                    {
                        if (clipWritten[ch] < 0.5f)
                        {
                            continue;
                        }

                        // The discrete channels of IsDiscreteChannel, spelled out to spare a call per channel.
                        if (ch == FrameGobo || ch == FrameTrackEffect)
                        {
                            if (weight > weightSum[ch])
                            {
                                sum[ch] = clipFrame[ch];
                                weightSum[ch] = weight;
                            }
                        }
                        else
                        {
                            sum[ch] += clipFrame[ch] * weight;
                            weightSum[ch] += weight;
                        }
                    }
                }

                for (var ch = 0; ch < FrameStride; ch++)
                {
                    var coverage = weightSum[ch];
                    if (coverage <= 0f)
                    {
                        continue;
                    }

                    if (ch == FrameGobo || ch == FrameTrackEffect)
                    {
                        if (coverage >= 0.5f)
                        {
                            frame[ch] = sum[ch];
                        }
                    }
                    else
                    {
                        frame[ch] = Mathf.Lerp(frame[ch], sum[ch] / coverage, Mathf.Clamp01(coverage));
                    }
                }
            }
        }
    }
}
