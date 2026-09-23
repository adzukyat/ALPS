using UnityEngine;

#if UDONSHARP
using UdonSharp;
#endif

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// The layout of a compiled show: the strides and columns of the flat arrays the
    /// compiler writes and the GPU evaluator (<c>Runtime/Gpu/AlpsEvaluator.hlsl</c>) reads,
    /// and the order maths the compiler runs once so playback never has to.
    ///
    /// The shader mirrors every constant here, so a layout change touches both. Ints travel
    /// inside float arrays and are read back with <see cref="ToInt"/>.
    /// </summary>
#if UDONSHARP
    public class AlpsShowLayout : UdonSharpBehaviour
#else
    public class AlpsShowLayout : MonoBehaviour
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
        public const int ClipPhase = 14;
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
        /// The fixture's own cone mesh length. No effect writes it, so it keeps the captured
        /// default, and the cone length scales the mesh relative to it.
        /// </summary>
        public const int FrameConeMeshLength = 12;
        public const int FrameStride = 13;

        /// <summary>
        /// 1 while a tracking head is aimed at its user. Only the GPU frames hold it, right
        /// after the channels, so the next frame knows to follow instead of jumping.
        /// </summary>
        public const int FrameAimed = 13;

        // ==================================================================================
        // Helpers the compiler runs once
        // ==================================================================================

        public static int ToInt(float value)
        {
            return Mathf.RoundToInt(value);
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

        /// <summary>
        /// The time bucket <paramref name="time"/> falls in, the way the GPU evaluator finds
        /// it. Times outside the show fall in the first or last bucket, whose clips then all
        /// weigh 0.
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
    }
}
