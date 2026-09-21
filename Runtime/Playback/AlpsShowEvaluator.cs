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

        public const int PhaseForward = 0;
        public const int PhasePingPong = 1;
        public const int PhaseRandom = 2;

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
        public const int PhaseRatio = 2;
        public const int PhaseGroupSize = 3;
        /// <summary>
        /// Delay per order position, in cycles. The model stores the spread over the whole
        /// group and the compiler divides it once, so the evaluator only sees the step.
        /// </summary>
        public const int PhaseDelay = 4;
        public const int PhaseBeatsPerCycle = 5;
        public const int PhaseInverse = 6;
        public const int PhaseStride = 7;

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
        public const int ClipBeatOrigin = 11;
        public const int ClipPhase = 12;
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
        public const int EffectStride = 10;

        // --- Parameter rows ----------------------------------------------------------------

        public const int ParamValue = 0;
        public const int ParamRangeMin = 1;
        public const int ParamRangeMax = 2;
        public const int ParamIsRange = 3;
        public const int ParamSpread = 4;
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
        public const int FrameStride = 12;

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
            if (type == 11) return t < 0.5f ? 0f : 1f;
            if (type == 12) return t * t * t;
            if (type == 13) return 1f - Mathf.Pow(1f - t, 3f);
            if (type == 14) return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
            if (type == 15) return 2.70158f * t * t * t - 1.70158f * t * t;
            if (type == 16)
            {
                var c2 = 1.70158f * 1.525f;
                return t < 0.5f
                    ? Mathf.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2) * 0.5f
                    : (Mathf.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) * 0.5f;
            }

            if (type == 17) return 1f - OutBounce(1f - t);
            if (type == 18)
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

        /// <summary>Beats elapsed since the track's beat origin.</summary>
        public static float Beats(float time, float bpm, float beatOrigin)
        {
            return (time - beatOrigin) * Mathf.Max(0f, bpm) / 60f;
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
        /// center, so a positive spread starts in the middle and a positive value spread opens out.
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

        /// <summary>Cycles elapsed for a fixture at order position k, before wrapping.</summary>
        public static float FixtureCycles(float beats, float beatsPerCycle, float delay, int k, float extraCycles)
        {
            var cycles = beatsPerCycle > 0f ? beats / beatsPerCycle : 0f;
            return cycles - delay * k + extraCycles;
        }

        /// <summary>
        /// Phase φ in 0..1 from unwrapped cycles. Forward is a sawtooth, ping-pong a
        /// triangle peaking at <paramref name="ratio"/>, random a smooth seeded wander.
        /// Invert and ease apply to forward and ping-pong only.
        /// </summary>
        public static float Phase(int mode, int ease, float ratio, bool inverse, float cycles, int k, int seed)
        {
            if (mode == PhaseRandom)
            {
                return Mathf.Clamp01(Mathf.PerlinNoise(cycles * 2f, (k + 1) * 7.31f + seed * 0.137f));
            }

            var u = cycles - Mathf.Floor(cycles);
            if (mode == PhasePingPong)
            {
                var peak = Mathf.Clamp(ratio, 0.001f, 0.999f);
                u = u < peak ? u / peak : 1f - (u - peak) / (1f - peak);
            }

            if (inverse)
            {
                u = 1f - u;
            }

            return Ease(ease, u);
        }

        /// <summary>True while a ping-pong phase is on its return leg.</summary>
        public static bool IsReturnLeg(int mode, float ratio, float cycles)
        {
            if (mode != PhasePingPong)
            {
                return false;
            }

            var u = cycles - Mathf.Floor(cycles);
            return u >= Mathf.Clamp(ratio, 0.001f, 0.999f);
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

        private static float SampleCurve(float[] data, int offset, float t)
        {
            var x = Mathf.Clamp01(t) * (CurveSamples - 1);
            var i = Mathf.Min(CurveSamples - 2, Mathf.FloorToInt(x));
            return Mathf.Lerp(data[offset + i], data[offset + i + 1], x - i);
        }

        // ==================================================================================
        // Clip evaluation
        // ==================================================================================

        /// <summary>
        /// Evaluates one clip for one fixture. Writes channel values into
        /// <paramref name="frame"/> and 1 into <paramref name="written"/> for every channel
        /// the clip drives. Channels the clip leaves alone are not touched.
        /// </summary>
        public static void EvaluateClip(
            float[] clips,
            float[] effects,
            float[] parameters,
            float[] colors,
            float[] gobos,
            int clip,
            int fixtureIndex,
            int fixtureCount,
            float time,
            float[] frame,
            float[] written,
            float[] scratch)
        {
            var row = clip * ClipStride;
            var phaseRow = row + ClipPhase;
            var order = ToInt(clips[row + ClipOrder]);
            var seed = ToInt(clips[row + ClipSeed]);
            var groupSize = ToInt(clips[phaseRow + PhaseGroupSize]);
            var beats = Beats(time, clips[row + ClipBpm], clips[row + ClipBeatOrigin]);
            var k = OrderPosition(order, seed, fixtureIndex, fixtureCount, groupSize);
            var isOdd = fixtureIndex % 2 == 0;

            var effectStart = ToInt(clips[row + ClipEffectStart]);
            var effectCount = ToInt(clips[row + ClipEffectCount]);
            for (var e = effectStart; e < effectStart + effectCount; e++)
            {
                var effectRow = e * EffectStride;
                var parity = ToInt(effects[effectRow + EffectParity]);
                if ((parity == ParityOdd && !isOdd) || (parity == ParityEven && isOdd))
                {
                    continue;
                }

                var kind = ToInt(effects[effectRow + EffectKind]);
                var paramStart = ToInt(effects[effectRow + EffectParamStart]);

                if (kind == KindMove)
                {
                    EvaluateMove(clips, effects, parameters, row, e, paramStart, fixtureIndex, fixtureCount, beats, seed, frame, written);
                }
                else if (kind == KindCone)
                {
                    WriteScalar(clips, parameters, row, paramStart, k, beats, seed, 0f, FrameConeWidth, frame, written);
                    WriteScalar(clips, parameters, row, paramStart + 1, k, beats, seed, 0f, FrameConeLength, frame, written);
                }
                else if (kind == KindBrightness)
                {
                    WriteScalar(clips, parameters, row, paramStart, k, beats, seed, 0f, FrameBrightness, frame, written);
                    if (effects[effectRow + EffectScalarA] > 0.5f)
                    {
                        // Blackout on return: dark on the return leg, still covering lower layers.
                        frame[FrameBrightness] *= BlackoutScale(
                            clips,
                            parameters,
                            row,
                            paramStart,
                            beats,
                            k,
                            effects[effectRow + EffectScalarB],
                            effects[effectRow + EffectScalarC]);
                    }
                }
                else if (kind == KindColor)
                {
                    EvaluateColor(clips, effects, parameters, colors, row, e, paramStart, k, beats, seed, frame, written, scratch);
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
                    EvaluateGobo(clips, effects, parameters, gobos, row, e, paramStart, k, fixtureIndex, beats, seed, frame, written, scratch);
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
            int fixtureIndex,
            int fixtureCount,
            float beats,
            int seed,
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

            var order = ToInt(clips[clipRow + ClipOrder]);
            var groupSize = ToInt(clips[clipRow + ClipPhase + PhaseGroupSize]);
            var k = OrderPosition(order, seed, fixtureIndex, fixtureCount, groupSize);

            // Only one move reaches a fixture per clip, so this marks whether it wrote pan.
            written[FramePan] = 0f;

            if (mode == MoveCircle)
            {
                EvaluateCircle(clips, effects, parameters, clipRow, effectRow, paramStart, k, beats, seed, frame, written);
            }
            else
            {
                var tiltRow = paramStart * ParamStride;
                var panRow = (paramStart + 1) * ParamStride;
                var bothRanged = parameters[tiltRow + ParamIsRange] > 0.5f && parameters[panRow + ParamIsRange] > 0.5f;
                var panOffsetCycles = bothRanged ? effects[effectRow + EffectScalarB] / 360f : 0f;

                WriteScalar(clips, parameters, clipRow, paramStart, k, beats, seed, 0f, FrameTilt, frame, written);
                WriteScalar(clips, parameters, clipRow, paramStart + 1, k, beats, seed, panOffsetCycles, FramePan, frame, written);
            }

            if (written[FramePan] > 0.5f && IsMirrored(order, fixtureIndex, fixtureCount, groupSize))
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
            float[] frame,
            float[] written)
        {
            var tiltParam = paramStart + 2;
            var panParam = paramStart + 3;
            var radiusParam = paramStart + 4;

            var phaseRow = clipRow + ClipPhase;
            var cycles = FixtureCycles(beats, clips[phaseRow + PhaseBeatsPerCycle], clips[phaseRow + PhaseDelay], k, 0f);
            var phase = Phase(
                ToInt(clips[phaseRow + PhaseMode]),
                ToInt(clips[phaseRow + PhaseEase]),
                clips[phaseRow + PhaseRatio],
                clips[phaseRow + PhaseInverse] > 0.5f,
                cycles,
                k,
                seed);

            var centerTilt = ResolveScalar(clips, parameters, clipRow, tiltParam, k, beats, seed, 0f) * Mathf.Deg2Rad;
            var centerPan = ResolveScalar(clips, parameters, clipRow, panParam, k, beats, seed, 0f) * Mathf.Deg2Rad;
            var radius = ResolveScalar(clips, parameters, clipRow, radiusParam, k, beats, seed, 0f);
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
            written[FrameTilt] = 1f;
            written[FramePan] = 1f;
        }

        /// <summary>Resolves one animatable parameter and writes it to <paramref name="channel"/>.</summary>
        private static void WriteScalar(
            float[] clips,
            float[] parameters,
            int clipRow,
            int param,
            int k,
            float beats,
            int seed,
            float extraCycles,
            int channel,
            float[] frame,
            float[] written)
        {
            frame[channel] = ResolveScalar(clips, parameters, clipRow, param, k, beats, seed, extraCycles);
            written[channel] = 1f;
        }

        /// <summary>
        /// Brightness multiplier for blackout on return, following the phase that governs
        /// <paramref name="param"/>. 0 on a ping-pong return leg. On the outbound leg it ramps
        /// up over <paramref name="fadeIn"/> and down over <paramref name="fadeOut"/>, both
        /// fractions of that leg. 1 when the phase has no return leg.
        /// </summary>
        public static float BlackoutScale(float[] clips, float[] parameters, int clipRow, int param, float beats, int k, float fadeIn, float fadeOut)
        {
            var paramRow = param * ParamStride;
            var own = parameters[paramRow + ParamUseOwnPhase] > 0.5f;
            var mode = own ? ToInt(parameters[paramRow + ParamOwnPhase + PhaseMode]) : ToInt(clips[clipRow + ClipPhase + PhaseMode]);
            if (mode != PhasePingPong)
            {
                return 1f;
            }

            var ratio = own ? parameters[paramRow + ParamOwnPhase + PhaseRatio] : clips[clipRow + ClipPhase + PhaseRatio];
            var cycles = ParamCycles(clips, parameters, clipRow, param, beats, k, 0f);
            if (IsReturnLeg(mode, ratio, cycles))
            {
                return 0f;
            }

            var u = (cycles - Mathf.Floor(cycles)) / Mathf.Clamp(ratio, 0.001f, 0.999f);
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

        /// <summary>Unwrapped cycles for a parameter, honoring its own phase.</summary>
        public static float ParamCycles(float[] clips, float[] parameters, int clipRow, int param, float beats, int k, float extraCycles)
        {
            var paramRow = param * ParamStride;
            if (parameters[paramRow + ParamUseOwnPhase] > 0.5f)
            {
                var own = paramRow + ParamOwnPhase;
                return FixtureCycles(beats, parameters[own + PhaseBeatsPerCycle], parameters[own + PhaseDelay], k, extraCycles);
            }

            var shared = clipRow + ClipPhase;
            return FixtureCycles(beats, clips[shared + PhaseBeatsPerCycle], clips[shared + PhaseDelay], k, extraCycles);
        }

        /// <summary>Phase φ for a parameter, honoring its own phase.</summary>
        public static float ParamPhase(float[] clips, float[] parameters, int clipRow, int param, float cycles, int k, int seed)
        {
            var paramRow = param * ParamStride;
            if (parameters[paramRow + ParamUseOwnPhase] > 0.5f)
            {
                var own = paramRow + ParamOwnPhase;
                return Phase(
                    ToInt(parameters[own + PhaseMode]),
                    ToInt(parameters[own + PhaseEase]),
                    parameters[own + PhaseRatio],
                    parameters[own + PhaseInverse] > 0.5f,
                    cycles,
                    k,
                    seed);
            }

            var shared = clipRow + ClipPhase;
            return Phase(
                ToInt(clips[shared + PhaseMode]),
                ToInt(clips[shared + PhaseEase]),
                clips[shared + PhaseRatio],
                clips[shared + PhaseInverse] > 0.5f,
                cycles,
                k,
                seed);
        }

        /// <summary>
        /// The value of a numeric parameter for one fixture, spread included. Only one range
        /// moves at a time: the value's, or the spread's while spread is on, where the value
        /// becomes a fixed offset.
        /// </summary>
        public static float ResolveScalar(float[] clips, float[] parameters, int clipRow, int param, int k, float beats, int seed, float extraCycles)
        {
            var paramRow = param * ParamStride;
            var hasSpread = parameters[paramRow + ParamHasSpread] > 0.5f;

            float moving;
            if (parameters[paramRow + ParamIsRange] > 0.5f)
            {
                var min = hasSpread ? parameters[paramRow + ParamSpreadMin] : parameters[paramRow + ParamRangeMin];
                var max = hasSpread ? parameters[paramRow + ParamSpreadMax] : parameters[paramRow + ParamRangeMax];
                var cycles = ParamCycles(clips, parameters, clipRow, param, beats, k, extraCycles);
                moving = ToInt(parameters[paramRow + ParamTiming]) == TimingPerCycle
                    ? ((CycleIndex(cycles) & 1) == 0 ? min : max)
                    : Mathf.LerpUnclamped(min, max, ParamPhase(clips, parameters, clipRow, param, cycles, k, seed));
            }
            else
            {
                moving = hasSpread ? parameters[paramRow + ParamSpread] : parameters[paramRow + ParamValue];
            }

            return hasSpread ? parameters[paramRow + ParamValue] + moving * k : moving;
        }

        /// <summary>
        /// Which palette entry is active and where inside it: returns the stop index and
        /// writes the local 0..1 position into <paramref name="local"/>[0].
        /// </summary>
        public static int PaletteStop(float[] clips, float[] parameters, int clipRow, int param, int count, float beats, int k, int seed, float[] local)
        {
            var cycles = ParamCycles(clips, parameters, clipRow, param, beats, k, 0f);
            var phase = ParamPhase(clips, parameters, clipRow, param, cycles, k, seed);
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

            var cycles = ParamCycles(clips, parameters, clipRow, param, beats, k, 0f);
            written[FrameRed] = 1f;
            written[FrameGreen] = 1f;
            written[FrameBlue] = 1f;

            var stop = count == 1 ? 0 : PaletteStop(clips, parameters, clipRow, param, count, beats, k, seed, scratch);
            var colorRow = (ToInt(effects[effectRow + EffectPaletteStart]) + stop) * ColorStride;
            if (colors[colorRow + ColorIsGradient] > 0.5f)
            {
                var t = count == 1 ? ParamPhase(clips, parameters, clipRow, param, cycles, k, seed) : scratch[0];
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

            var stop = count == 1 ? 0 : PaletteStop(clips, parameters, clipRow, param, count, beats, k, seed, scratch);
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
        /// The final frame of one fixture at <paramref name="time"/>.
        ///
        /// Clips must be sorted by layer. Inside a layer, overlapping clips blend by their
        /// weights. A layer then covers the layers below it by its total weight, per channel,
        /// so a later track only overrides the channels its effects actually drive.
        /// Channels nobody drives keep the fixture default.
        /// </summary>
        public static void EvaluateFixture(
            float[] clips,
            float[] effects,
            float[] parameters,
            float[] colors,
            float[] gobos,
            int clipCount,
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

            var c = 0;
            while (c < clipCount)
            {
                var layer = ToInt(clips[c * ClipStride + ClipLayer]);
                for (var ch = 0; ch < FrameStride; ch++)
                {
                    sum[ch] = 0f;
                    weightSum[ch] = 0f;
                }

                while (c < clipCount && ToInt(clips[c * ClipStride + ClipLayer]) == layer)
                {
                    var clip = c;
                    c++;

                    var weight = ClipWeight(clips, clip, time);
                    if (weight <= 0f)
                    {
                        continue;
                    }

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

                    EvaluateClip(clips, effects, parameters, colors, gobos, clip, index, groupCount[group], time, clipFrame, clipWritten, scratch);

                    for (var ch = 0; ch < FrameStride; ch++)
                    {
                        if (clipWritten[ch] < 0.5f)
                        {
                            continue;
                        }

                        if (IsDiscreteChannel(ch))
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

                    if (IsDiscreteChannel(ch))
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
