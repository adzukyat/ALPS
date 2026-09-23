// The show evaluator. Reads the compiled show from one float texture laid out by
// AlpsShowPlayer.PackShowData and gives the frame of one fixture, for the preview and the
// Udon player alike.
//
// Every index and constant here mirrors AlpsShowLayout and PackShowData. Changing the
// layout on one side means changing it here too.
//
// Each value, phase and ease is worked out at a single place in the code, inside loops the
// compiler keeps as loops. Calling them from every effect instead has the compiler inline
// them over and over, which is what makes a shader like this take minutes to compile.

#ifndef ALPS_EVALUATOR_INCLUDED
#define ALPS_EVALUATOR_INCLUDED

Texture2D<float> _AlpsData;
float _AlpsTime;

// --- Data texture header, mirrored from AlpsShowPlayer.PackShowData -------------------

#define ALPS_DATA_WIDTH 1024
#define ALPS_HEADER_CLIPS 1
#define ALPS_HEADER_CLIP_COUNT 2
#define ALPS_HEADER_EFFECTS 3
#define ALPS_HEADER_PARAMETERS 4
#define ALPS_HEADER_COLORS 5
#define ALPS_HEADER_GOBOS 6
#define ALPS_HEADER_POSITIONS 7
#define ALPS_HEADER_BUCKET_START 8
#define ALPS_HEADER_BUCKET_COUNT 9
#define ALPS_HEADER_BUCKET_CLIPS 10
#define ALPS_HEADER_BUCKET_SECONDS 11
#define ALPS_HEADER_GROUP_COUNT 12
#define ALPS_HEADER_GROUPS 13
#define ALPS_HEADER_GROUP_INDEX 14
#define ALPS_HEADER_FIXTURE_COUNT 15
#define ALPS_HEADER_DEFAULTS 16
#define ALPS_HEADER_FIXTURE_INFO 17
#define ALPS_HEADER_GROUP_COLUMNS 18
#define ALPS_HEADER_ROW_FIXTURES 19
#define ALPS_HEADER_AIM 20

#define ALPS_FIXTURE_INFO_STRIDE 4
#define ALPS_AIM_STRIDE 12
#define ALPS_MAX_FIXTURES 118

// --- Layout constants, mirrored from AlpsShowLayout -----------------------------------

#define PhaseWave 0
#define PhaseRandom 1
#define TimingPerCycle 1
#define KindMove 0
#define KindCone 1
#define KindColor 2
#define KindBrightness 3
#define KindFlicker 4
#define KindGobo 5
#define ParityEven 1
#define ParityOdd 2
#define MoveCircle 1
#define MoveTrackUser 2

#define PhaseMode 0
#define PhaseEase 1
#define PhaseRise 2
#define PhaseHoldHigh 3
#define PhaseFall 4
#define PhaseDelay 6
#define PhaseBeatsPerCycle 7
#define PhaseInverse 8
#define PhaseFallEase 9
#define PhaseStride 10

#define ClipStart 0
#define ClipEnd 1
#define ClipMixInDuration 2
#define ClipMixOutDuration 3
#define ClipLayer 4
#define ClipGroup 5
#define ClipEffectStart 6
#define ClipEffectCount 7
#define ClipSeed 9
#define ClipBpm 10
#define ClipFadeIn 11
#define ClipFadeOut 12
#define ClipPositionStart 13
#define ClipPhase 14
#define CurveSamples 16
#define ClipMixInCurve (ClipPhase + PhaseStride)
#define ClipMixOutCurve (ClipMixInCurve + CurveSamples)
#define ClipStride (ClipMixOutCurve + CurveSamples)

#define EffectKind 0
#define EffectParity 1
#define EffectParamStart 2
#define EffectPaletteStart 3
#define EffectPaletteCount 4
#define EffectScalarA 5
#define EffectScalarB 6
#define EffectScalarC 7
#define EffectScalarD 8
#define EffectScalarE 9
#define EffectPhaseOffset 10
#define EffectStride 11

#define ParamValue 0
#define ParamRangeMin 1
#define ParamRangeMax 2
#define ParamIsRange 3
#define ParamSpread 4
#define ParamSpreadMin 5
#define ParamSpreadMax 6
#define ParamHasSpread 7
#define ParamTiming 8
#define ParamUseOwnPhase 9
#define ParamOwnPhase 10
#define ParamStride (ParamOwnPhase + PhaseStride)

#define ColorIsGradient 0
#define ColorSolid 1
#define ColorGradient 4
#define ColorStride (ColorGradient + CurveSamples * 3)

#define FramePan 0
#define FrameTilt 1
#define FrameBrightness 2
#define FrameRed 3
#define FrameGreen 4
#define FrameBlue 5
#define FrameConeWidth 6
#define FrameConeLength 7
#define FrameGobo 8
#define FrameGoboRotation 9
#define FrameBrightnessScale 10
#define FrameTrackEffect 11
#define FrameConeMeshLength 12
#define FrameStride 13
#define FrameAimed 13

// Most values one effect reads: a circle's turn, its center tilt and pan, and its radius.
#define ALPS_MAX_SLOTS 4

#define ALPS_PI 3.14159265358979

// --- Data access -------------------------------------------------------------------------

float AlpsRead(int index)
{
    return _AlpsData.Load(int3(index % ALPS_DATA_WIDTH, index / ALPS_DATA_WIDTH, 0));
}

int AlpsReadInt(int index)
{
    return (int)round(AlpsRead(index));
}

// Sections of the data texture, read once per evaluation.
struct AlpsShow
{
    int clips;
    int clipCount;
    int effects;
    int parameters;
    int colors;
    int gobos;
    int positions;
    int bucketStart;
    int bucketCount;
    int bucketClips;
    float bucketSeconds;
    int groupCount;
    int groups;
    int groupIndex;
    int fixtureCount;
    int defaults;
    int fixtureInfo;
    int groupColumns;
    int rowFixtures;
    int aim;
};

AlpsShow AlpsLoadShow()
{
    AlpsShow show;
    show.clips = AlpsReadInt(ALPS_HEADER_CLIPS);
    show.clipCount = AlpsReadInt(ALPS_HEADER_CLIP_COUNT);
    show.effects = AlpsReadInt(ALPS_HEADER_EFFECTS);
    show.parameters = AlpsReadInt(ALPS_HEADER_PARAMETERS);
    show.colors = AlpsReadInt(ALPS_HEADER_COLORS);
    show.gobos = AlpsReadInt(ALPS_HEADER_GOBOS);
    show.positions = AlpsReadInt(ALPS_HEADER_POSITIONS);
    show.bucketStart = AlpsReadInt(ALPS_HEADER_BUCKET_START);
    show.bucketCount = AlpsReadInt(ALPS_HEADER_BUCKET_COUNT);
    show.bucketClips = AlpsReadInt(ALPS_HEADER_BUCKET_CLIPS);
    show.bucketSeconds = AlpsRead(ALPS_HEADER_BUCKET_SECONDS);
    show.groupCount = AlpsReadInt(ALPS_HEADER_GROUP_COUNT);
    show.groups = AlpsReadInt(ALPS_HEADER_GROUPS);
    show.groupIndex = AlpsReadInt(ALPS_HEADER_GROUP_INDEX);
    show.fixtureCount = AlpsReadInt(ALPS_HEADER_FIXTURE_COUNT);
    show.defaults = AlpsReadInt(ALPS_HEADER_DEFAULTS);
    show.fixtureInfo = AlpsReadInt(ALPS_HEADER_FIXTURE_INFO);
    show.groupColumns = AlpsReadInt(ALPS_HEADER_GROUP_COLUMNS);
    show.rowFixtures = AlpsReadInt(ALPS_HEADER_ROW_FIXTURES);
    show.aim = AlpsReadInt(ALPS_HEADER_AIM);
    return show;
}

// --- Scalar helpers ----------------------------------------------------------------------

float AlpsOutBounce(float t)
{
    if (t < 1.0 / 2.75) return 7.5625 * t * t;
    if (t < 2.0 / 2.75) { t -= 1.5 / 2.75; return 7.5625 * t * t + 0.75; }
    if (t < 2.5 / 2.75) { t -= 2.25 / 2.75; return 7.5625 * t * t + 0.9375; }
    t -= 2.625 / 2.75;
    return 7.5625 * t * t + 0.984375;
}

// AlpsPhaseCurve.Ease. Powers are spelled out, since pow is undefined for a negative base
// on the GPU. The three bounce eases share one bounce, as base + scale * bounce(x).
float AlpsEase(int type, float t)
{
    t = saturate(t);
    if (type == 10 || type == 16 || type == 17)
    {
        float x = t;
        float base = 0.0;
        float scale = 1.0;
        if (type == 16)
        {
            x = 1.0 - t;
            base = 1.0;
            scale = -1.0;
        }
        else if (type == 17)
        {
            bool first = t < 0.5;
            x = first ? 1.0 - 2.0 * t : 2.0 * t - 1.0;
            base = 0.5;
            scale = first ? -0.5 : 0.5;
        }

        return base + scale * AlpsOutBounce(x);
    }

    if (type == 1) return 1.0 - cos(t * ALPS_PI * 0.5);
    if (type == 2) return sin(t * ALPS_PI * 0.5);
    if (type == 3) return -(cos(ALPS_PI * t) - 1.0) * 0.5;
    if (type == 4) return t * t;
    if (type == 5) return 1.0 - (1.0 - t) * (1.0 - t);
    if (type == 6)
    {
        float u = -2.0 * t + 2.0;
        return t < 0.5 ? 4.0 * t * t * t : 1.0 - u * u * u * 0.5;
    }
    if (type == 7) return t <= 0.0 ? 0.0 : exp2(10.0 * t - 10.0);
    if (type == 8) return t >= 1.0 ? 1.0 : 1.0 - exp2(-10.0 * t);
    if (type == 9)
    {
        float u = t - 1.0;
        return 1.0 + 2.70158 * u * u * u + 1.70158 * u * u;
    }
    if (type == 11) return t * t * t;
    if (type == 12) return 1.0 - (1.0 - t) * (1.0 - t) * (1.0 - t);
    if (type == 13)
    {
        float u = -2.0 * t + 2.0;
        return t < 0.5 ? 2.0 * t * t : 1.0 - u * u * 0.5;
    }
    if (type == 14) return 2.70158 * t * t * t - 1.70158 * t * t;
    if (type == 15)
    {
        float c2 = 1.70158 * 1.525;
        float a = 2.0 * t;
        float b = 2.0 * t - 2.0;
        return t < 0.5
            ? a * a * ((c2 + 1.0) * 2.0 * t - c2) * 0.5
            : (b * b * ((c2 + 1.0) * (t * 2.0 - 2.0) + c2) + 2.0) * 0.5;
    }
    return t;
}

// Smooth 2D value noise in 0..1, AlpsPhaseCurve.Noise.
float AlpsHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float AlpsNoise(float x, float y)
{
    float2 p = float2(x, y);
    float2 i = floor(p);
    float2 f = p - i;
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = AlpsHash(i);
    float b = AlpsHash(i + float2(1, 0));
    float c = AlpsHash(i + float2(0, 1));
    float d = AlpsHash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float AlpsBeats(float time, float bpm, float clipStart)
{
    return (time - clipStart) * max(0.0, bpm) / 60.0;
}

float AlpsFixtureCycles(float beats, float beatsPerCycle, float delay, int k, float extraCycles)
{
    float cycles = beatsPerCycle > 0.0 ? beats / beatsPerCycle : 0.0;
    return cycles - delay * k + extraCycles;
}

// AlpsPhaseCurve.Wave, with the rise and the fall sharing one ease.
float AlpsWave(int riseEase, int fallEase, float rise, float holdHigh, float fall, float u)
{
    float riseEnd = saturate(rise);
    float highEnd = riseEnd + clamp(holdHigh, 0.0, 1.0 - riseEnd);
    float fallEnd = highEnd + clamp(fall, 0.0, 1.0 - highEnd);
    if (u >= riseEnd && u < highEnd) return 1.0;
    if (u >= fallEnd) return 0.0;

    bool rising = u < riseEnd;
    float t = rising ? u / riseEnd : (u - highEnd) / (fallEnd - highEnd);
    float eased = AlpsEase(rising ? riseEase : fallEase, t);
    return rising ? eased : 1.0 - eased;
}

// Phase from the phase block starting at data index <row>.
float AlpsPhaseAt(int row, float cycles, int k, int seed)
{
    if (AlpsReadInt(row + PhaseMode) == PhaseRandom)
    {
        return saturate(AlpsNoise(cycles * 2.0, (k + 1) * 7.31 + seed * 0.137));
    }

    float u = AlpsWave(
        AlpsReadInt(row + PhaseEase),
        AlpsReadInt(row + PhaseFallEase),
        AlpsRead(row + PhaseRise),
        AlpsRead(row + PhaseHoldHigh),
        AlpsRead(row + PhaseFall),
        cycles - floor(cycles));
    return AlpsRead(row + PhaseInverse) > 0.5 ? 1.0 - u : u;
}

float AlpsOutboundLeg(float rise, float holdHigh)
{
    float riseEnd = saturate(rise);
    return riseEnd + clamp(holdHigh, 0.0, 1.0 - riseEnd);
}

// --- Clip weight -------------------------------------------------------------------------

float AlpsSampleCurve(int offset, float t)
{
    float x = saturate(t) * (CurveSamples - 1);
    int i = min(CurveSamples - 2, (int)floor(x));
    return lerp(AlpsRead(offset + i), AlpsRead(offset + i + 1), x - i);
}

// Timeline style weight from the sampled mix in and mix out curves, 0 outside the clip.
float AlpsClipWeight(int row, float time)
{
    float start = AlpsRead(row + ClipStart);
    float end = AlpsRead(row + ClipEnd);
    if (time < start || time > end) return 0.0;

    float weight = 1.0;
    float mixIn = AlpsRead(row + ClipMixInDuration);
    if (mixIn > 0.0 && time < start + mixIn)
    {
        weight *= AlpsSampleCurve(row + ClipMixInCurve, (time - start) / mixIn);
    }

    float mixOut = AlpsRead(row + ClipMixOutDuration);
    if (mixOut > 0.0 && time > end - mixOut)
    {
        weight *= AlpsSampleCurve(row + ClipMixOutCurve, (time - (end - mixOut)) / mixOut);
    }

    return saturate(weight);
}

// The clip's own fade in and fade out, counted in beats at the clip's tempo.
float AlpsClipFade(int row, float time)
{
    float bpm = AlpsRead(row + ClipBpm);
    float scale = 1.0;
    float fadeIn = AlpsRead(row + ClipFadeIn);
    if (fadeIn > 0.0) scale = min(scale, AlpsBeats(time, bpm, AlpsRead(row + ClipStart)) / fadeIn);
    float fadeOut = AlpsRead(row + ClipFadeOut);
    if (fadeOut > 0.0) scale = min(scale, AlpsBeats(AlpsRead(row + ClipEnd), bpm, time) / fadeOut);
    return saturate(scale);
}

// --- Values ------------------------------------------------------------------------------

// What one clip evaluation needs about the clip and the fixture, worked out once.
struct AlpsClipContext
{
    int row;
    int seed;
    float beats;
    int k;
    float sharedCycles;
    float sharedPhase;
};

// One value an effect reads: the parameter at data index <paramRow> run late by <extra>
// cycles, spread and range included. Also gives the cycles and the phase it followed, which
// palettes and blackout read. A <paramRow> below 0 reads only the clip's shared phase.
//
// A value follows its own phase if it has one, otherwise the clip's shared phase, which is
// already worked out unless the value runs late.
float AlpsResolve(AlpsClipContext c, int paramRow, float extra, out float cycles, out float phase)
{
    bool own = paramRow >= 0 && AlpsRead(paramRow + ParamUseOwnPhase) > 0.5;
    int phaseRow = own ? paramRow + ParamOwnPhase : c.row + ClipPhase;
    cycles = own
        ? AlpsFixtureCycles(c.beats, AlpsRead(phaseRow + PhaseBeatsPerCycle), AlpsRead(phaseRow + PhaseDelay), c.k, extra)
        : c.sharedCycles + extra;

    [branch]
    if (own || extra != 0.0)
    {
        phase = AlpsPhaseAt(phaseRow, cycles, c.k, c.seed);
    }
    else
    {
        phase = c.sharedPhase;
    }

    if (paramRow < 0) return phase;

    float value;
    float step;
    if (AlpsRead(paramRow + ParamIsRange) > 0.5)
    {
        if (AlpsReadInt(paramRow + ParamTiming) == TimingPerCycle)
        {
            bool atMin = ((int)floor(cycles) & 1) == 0;
            value = AlpsRead(paramRow + (atMin ? ParamRangeMin : ParamRangeMax));
            step = AlpsRead(paramRow + (atMin ? ParamSpreadMin : ParamSpreadMax));
        }
        else
        {
            float rangeMin = AlpsRead(paramRow + ParamRangeMin);
            float spreadMin = AlpsRead(paramRow + ParamSpreadMin);
            value = rangeMin + (AlpsRead(paramRow + ParamRangeMax) - rangeMin) * phase;
            step = spreadMin + (AlpsRead(paramRow + ParamSpreadMax) - spreadMin) * phase;
        }
    }
    else
    {
        value = AlpsRead(paramRow + ParamValue);
        step = AlpsRead(paramRow + ParamSpread);
    }

    return AlpsRead(paramRow + ParamHasSpread) > 0.5 ? value + step * c.k : value;
}

// Palette entry and the local 0..1 position inside it.
int AlpsPaletteStop(int paramRow, int count, float cycles, float phase, out float local)
{
    if (count <= 1)
    {
        local = phase;
        return 0;
    }

    if (AlpsReadInt(paramRow + ParamTiming) == TimingPerCycle)
    {
        local = phase;
        int index = (int)floor(cycles) % count;
        return index < 0 ? index + count : index;
    }

    float scaled = saturate(phase) * count;
    int stop = min(count - 1, (int)floor(scaled));
    local = saturate(scaled - stop);
    return stop;
}

// Brightness multiplier for blackout on return, 0 on the return leg of the wave the value
// follows and faded at both ends of the outbound leg.
float AlpsBlackoutScale(AlpsClipContext c, int paramRow, float cycles, float fadeIn, float fadeOut)
{
    bool own = AlpsRead(paramRow + ParamUseOwnPhase) > 0.5;
    int phaseRow = own ? paramRow + ParamOwnPhase : c.row + ClipPhase;
    float outbound = AlpsOutboundLeg(AlpsRead(phaseRow + PhaseRise), AlpsRead(phaseRow + PhaseHoldHigh));
    if (AlpsReadInt(phaseRow + PhaseMode) != PhaseWave || outbound >= 1.0) return 1.0;

    float u = cycles - floor(cycles);
    if (u >= outbound) return 0.0;

    u /= max(0.001, outbound);
    float scale = 1.0;
    if (fadeIn > 0.0) scale = min(scale, u / fadeIn);
    if (fadeOut > 0.0) scale = min(scale, (1.0 - u) / fadeOut);
    return saturate(scale);
}

// Pan and tilt of a beam on a ring around a center direction, <turn> radians round. The
// ring is worked out on the sphere, so it stays a ring wherever the center points, where
// sweeping pan and tilt with two waves would fold into a figure eight.
float2 AlpsCircle(float centerTilt, float centerPan, float radius, float aspect, float turn)
{
    float sinTilt = sin(centerTilt);
    float cosTilt = cos(centerTilt);
    float sinPan = sin(centerPan);
    float cosPan = cos(centerPan);
    float3 center = float3(-sinTilt * sinPan, -cosTilt, -sinTilt * cosPan);
    float3 acrossAxis = float3(cosPan, 0.0, -sinPan);
    float3 upAxis = float3(cosTilt * sinPan, -sinTilt, cosTilt * cosPan);

    float across = radius * aspect * cos(turn);
    float up = radius * sin(turn);
    float opening = sqrt(across * across + up * up);

    float3 dir = center;
    if (opening > 0.0001)
    {
        float3 step = (across * acrossAxis + up * upAxis) / opening;
        dir = cos(radians(opening)) * center + sin(radians(opening)) * step;
    }

    return float2(degrees(atan2(-dir.x, -dir.z)), degrees(acos(clamp(-dir.y, -1.0, 1.0))));
}

// --- One clip, one fixture ---------------------------------------------------------------

// A fixture frame and which of its channels a clip drove.
struct AlpsFrame
{
    float value[FrameStride];
    bool written[FrameStride];
};

void AlpsWrite(inout AlpsFrame frame, int channel, float value)
{
    frame.value[channel] = value;
    frame.written[channel] = true;
}

// Evaluates clip <clip> for the fixture at position <fixtureIndex> of the clip's group.
void AlpsEvaluateClip(int clip, int fixtureIndex, float time, inout AlpsFrame frame, AlpsShow show)
{
    AlpsClipContext c;
    c.row = show.clips + clip * ClipStride;
    int phaseRow = c.row + ClipPhase;
    c.seed = AlpsReadInt(c.row + ClipSeed);
    c.beats = AlpsBeats(time, AlpsRead(c.row + ClipBpm), AlpsRead(c.row + ClipStart));
    int positionRow = show.positions + AlpsReadInt(c.row + ClipPositionStart) + fixtureIndex * 2;
    c.k = AlpsReadInt(positionRow);
    bool mirrored = AlpsRead(positionRow + 1) > 0.5;
    bool isOdd = fixtureIndex % 2 == 0;
    c.sharedCycles = AlpsFixtureCycles(c.beats, AlpsRead(phaseRow + PhaseBeatsPerCycle), AlpsRead(phaseRow + PhaseDelay), c.k, 0.0);
    c.sharedPhase = AlpsPhaseAt(phaseRow, c.sharedCycles, c.k, c.seed);

    int effectStart = AlpsReadInt(c.row + ClipEffectStart);
    int effectEnd = effectStart + AlpsReadInt(c.row + ClipEffectCount);
    [loop]
    for (int e = effectStart; e < effectEnd; e++)
    {
        int effectRow = show.effects + e * EffectStride;
        int parity = AlpsReadInt(effectRow + EffectParity);
        if ((parity == ParityOdd && !isOdd) || (parity == ParityEven && isOdd)) continue;

        int kind = AlpsReadInt(effectRow + EffectKind);
        int paramRow = show.parameters + AlpsReadInt(effectRow + EffectParamStart) * ParamStride;
        int mode = AlpsReadInt(effectRow + EffectScalarA);
        int paletteCount = AlpsReadInt(effectRow + EffectPaletteCount);

        // The effect's phase offset runs every value on it late by that share of a cycle.
        float late = -AlpsRead(effectRow + EffectPhaseOffset);

        // Which values the effect reads, worked out below in one loop.
        int slots = 0;
        int slotParam[ALPS_MAX_SLOTS];
        float slotExtra[ALPS_MAX_SLOTS];
        [unroll]
        for (int s = 0; s < ALPS_MAX_SLOTS; s++)
        {
            slotParam[s] = paramRow;
            slotExtra[s] = late;
        }

        if (kind == KindMove && mode == MoveCircle)
        {
            // The turn follows the shared phase, then center tilt, center pan and radius.
            slots = 4;
            slotParam[0] = -1;
            slotParam[1] = paramRow + 2 * ParamStride;
            slotParam[2] = paramRow + 3 * ParamStride;
            slotParam[3] = paramRow + 4 * ParamStride;
        }
        else if (kind == KindMove && mode != MoveTrackUser)
        {
            // Tilt, then pan, which a phase offset in degrees may run ahead of tilt.
            int panRow = paramRow + ParamStride;
            bool bothRanged = AlpsRead(paramRow + ParamIsRange) > 0.5 && AlpsRead(panRow + ParamIsRange) > 0.5;
            slots = 2;
            slotParam[1] = panRow;
            slotExtra[1] = late + (bothRanged ? AlpsRead(effectRow + EffectScalarB) / 360.0 : 0.0);
        }
        else if (kind == KindCone)
        {
            slots = 2;
            slotParam[1] = paramRow + ParamStride;
        }
        else if (kind == KindBrightness || ((kind == KindColor || kind == KindGobo) && paletteCount > 0))
        {
            slots = 1;
        }

        float value[ALPS_MAX_SLOTS];
        float cycles[ALPS_MAX_SLOTS];
        float phase[ALPS_MAX_SLOTS];
        [unroll]
        for (int z = 0; z < ALPS_MAX_SLOTS; z++)
        {
            value[z] = 0.0;
            cycles[z] = 0.0;
            phase[z] = 0.0;
        }

        [loop]
        for (int v = 0; v < slots; v++)
        {
            float slotCycles;
            float slotPhase;
            float resolved = AlpsResolve(c, slotParam[v], slotExtra[v], slotCycles, slotPhase);
            value[v] = resolved;
            cycles[v] = slotCycles;
            phase[v] = slotPhase;
        }

        if (kind == KindMove)
        {
            if (mode == MoveTrackUser)
            {
                // The frames pass aims the head at the user, and the order does not apply.
                AlpsWrite(frame, FrameTrackEffect, e + 1);
                continue;
            }

            // An angle move above a tracking move takes the head back from the user.
            AlpsWrite(frame, FrameTrackEffect, 0.0);
            float2 panTilt = mode == MoveCircle
                ? AlpsCircle(radians(value[1]), radians(value[2]), value[3], AlpsRead(effectRow + EffectScalarE), value[0] * 2.0 * ALPS_PI)
                : float2(value[1], value[0]);
            AlpsWrite(frame, FrameTilt, panTilt.y);
            AlpsWrite(frame, FramePan, mirrored ? -panTilt.x : panTilt.x);
        }
        else if (kind == KindCone)
        {
            AlpsWrite(frame, FrameConeWidth, value[0]);
            AlpsWrite(frame, FrameConeLength, value[1]);
        }
        else if (kind == KindBrightness)
        {
            float brightness = value[0];
            if (mode > 0)
            {
                // Blackout on return: dark on the return leg, still covering lower layers.
                brightness *= AlpsBlackoutScale(c, paramRow, cycles[0], AlpsRead(effectRow + EffectScalarB), AlpsRead(effectRow + EffectScalarC));
            }

            AlpsWrite(frame, FrameBrightness, brightness);
        }
        else if (kind == KindColor)
        {
            if (paletteCount <= 0) continue;

            float t;
            int colorRow = show.colors + (AlpsReadInt(effectRow + EffectPaletteStart) + AlpsPaletteStop(paramRow, paletteCount, cycles[0], phase[0], t)) * ColorStride;
            float3 rgb;
            if (AlpsRead(colorRow + ColorIsGradient) > 0.5)
            {
                float x = saturate(t) * (CurveSamples - 1);
                int i = min(CurveSamples - 2, (int)floor(x));
                int a = colorRow + ColorGradient + i * 3;
                rgb = lerp(float3(AlpsRead(a), AlpsRead(a + 1), AlpsRead(a + 2)), float3(AlpsRead(a + 3), AlpsRead(a + 4), AlpsRead(a + 5)), x - i);
            }
            else
            {
                rgb = float3(AlpsRead(colorRow + ColorSolid), AlpsRead(colorRow + ColorSolid + 1), AlpsRead(colorRow + ColorSolid + 2));
            }

            AlpsWrite(frame, FrameRed, rgb.r);
            AlpsWrite(frame, FrameGreen, rgb.g);
            AlpsWrite(frame, FrameBlue, rgb.b);
        }
        else if (kind == KindFlicker)
        {
            float speed = AlpsRead(effectRow + EffectScalarA);
            float strength = saturate(AlpsRead(effectRow + EffectScalarB));
            float stagger = AlpsRead(effectRow + EffectScalarC);
            float noise = saturate(AlpsNoise(time * speed + fixtureIndex * stagger, 3.7 + c.seed * 0.071));
            AlpsWrite(frame, FrameBrightnessScale, 1.0 - strength * noise);
        }
        else if (kind == KindGobo)
        {
            float beatsPerTurn = AlpsRead(effectRow + EffectScalarA);
            float turn = beatsPerTurn > 0.0 ? c.beats / beatsPerTurn : 0.0;
            float angle = 360.0 * turn + fixtureIndex * AlpsRead(effectRow + EffectScalarB);
            AlpsWrite(frame, FrameGoboRotation, angle - floor(angle / 360.0) * 360.0);

            if (paletteCount <= 0) continue;

            float local;
            int stop = AlpsPaletteStop(paramRow, paletteCount, cycles[0], phase[0], local);
            AlpsWrite(frame, FrameGobo, AlpsRead(show.gobos + AlpsReadInt(effectRow + EffectPaletteStart) + stop));
        }
    }
}

// --- Composition -------------------------------------------------------------------------

// Channels that switch instead of blending.
bool AlpsIsDiscrete(int channel)
{
    return channel == FrameGobo || channel == FrameTrackEffect;
}

// Position of a fixture inside a group, or -1 when it is not a member.
int AlpsIndexInGroup(AlpsShow show, int group, int fixture)
{
    if (group < 0 || group >= show.groups || fixture < 0 || fixture >= show.groupColumns) return -1;
    return AlpsReadInt(show.groupIndex + group * show.groupColumns + fixture);
}

// The final frame of one fixture at <time>.
//
// Clips are listed by layer. Inside a layer, overlapping clips blend by their weights. A
// layer then covers the layers below it by its total weight, per channel, so a later track
// only overrides the channels its effects drive. Channels nobody drives keep the fixture
// default, except brightness, which is dark until a brightness effect lights it. A clip's
// own fade scales its weight, so fading behaves like blending with an empty clip.
void AlpsEvaluateFixture(int fixture, float time, out float result[FrameStride], AlpsShow show)
{
    int defaultRow = show.defaults + fixture * FrameStride;
    [unroll]
    for (int ch = 0; ch < FrameStride; ch++)
    {
        result[ch] = AlpsRead(defaultRow + ch);
    }

    result[FrameBrightness] = 0.0;
    if (show.bucketCount <= 0 || show.bucketSeconds <= 0.0) return;

    int bucket = clamp((int)floor(time / show.bucketSeconds), 0, show.bucketCount - 1);
    int first = AlpsReadInt(show.bucketStart + bucket);
    int end = AlpsReadInt(show.bucketStart + bucket + 1);

    float sum[FrameStride];
    float weightSum[FrameStride];
    [unroll]
    for (int w = 0; w < FrameStride; w++)
    {
        sum[w] = 0.0;
        weightSum[w] = 0.0;
    }

    float layer = -1.0;
    [loop]
    for (int i = first; i <= end; i++)
    {
        // Past the last clip, or where a new layer starts, the layer so far covers the frame.
        bool last = i == end;
        int clip = last ? 0 : AlpsReadInt(show.bucketClips + i);
        int row = show.clips + clip * ClipStride;
        float clipLayer = last ? -2.0 : AlpsRead(row + ClipLayer);
        if (clipLayer != layer)
        {
            [unroll]
            for (int ch = 0; ch < FrameStride; ch++)
            {
                float coverage = weightSum[ch];
                if (coverage > 0.0)
                {
                    result[ch] = AlpsIsDiscrete(ch)
                        ? (coverage >= 0.5 ? sum[ch] : result[ch])
                        : lerp(result[ch], sum[ch] / coverage, saturate(coverage));
                }

                sum[ch] = 0.0;
                weightSum[ch] = 0.0;
            }

            layer = clipLayer;
        }

        if (last) break;

        float weight = AlpsClipWeight(row, time);
        if (weight <= 0.0) continue;
        weight *= AlpsClipFade(row, time);
        if (weight <= 0.0) continue;

        int index = AlpsIndexInGroup(show, AlpsReadInt(row + ClipGroup), fixture);
        if (index < 0) continue;

        AlpsFrame frame;
        [unroll]
        for (int f = 0; f < FrameStride; f++)
        {
            frame.value[f] = 0.0;
            frame.written[f] = false;
        }

        AlpsEvaluateClip(clip, index, time, frame, show);

        [unroll]
        for (int ch = 0; ch < FrameStride; ch++)
        {
            if (!frame.written[ch]) continue;
            if (AlpsIsDiscrete(ch))
            {
                if (weight > weightSum[ch])
                {
                    sum[ch] = frame.value[ch];
                    weightSum[ch] = weight;
                }
            }
            else
            {
                sum[ch] += frame.value[ch] * weight;
                weightSum[ch] += weight;
            }
        }
    }
}

#endif
