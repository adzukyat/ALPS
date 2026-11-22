using AdzukiSoft.ALPS;
using UnityEngine;

/// <summary>
/// A value as a clip's inspector row sets it: one number, a range the phase moves through
/// (R), a spread from the first fixture to the last (S), or a range between two spreads.
/// </summary>
public struct V
{
    private float _a;
    private float _b;
    private float _c;
    private float _d;
    private int _kind;
    private bool _perCycle;
    private AlpsPhaseSettings _own;

    public static implicit operator V(float value)
    {
        return new V { _a = value };
    }

    /// <summary>From <paramref name="from"/> at the bottom of the phase to <paramref name="to"/> at its top.</summary>
    public static V R(float from, float to)
    {
        return new V { _kind = 1, _a = from, _b = to };
    }

    /// <summary><paramref name="first"/> at the first order position, <paramref name="last"/> at the last.</summary>
    public static V S(float first, float last)
    {
        return new V { _kind = 2, _a = first, _b = last };
    }

    /// <summary>A spread at the bottom of the phase that opens into another at its top.</summary>
    public static V SR(float first0, float last0, float first1, float last1)
    {
        return new V { _kind = 3, _a = first0, _b = last0, _c = first1, _d = last1 };
    }

    /// <summary>Steps once per cycle instead of sweeping inside it.</summary>
    public V PerCycle()
    {
        var copy = this;
        copy._perCycle = true;
        return copy;
    }

    /// <summary>Follows a phase of its own instead of the clip's.</summary>
    public V Own(AlpsPhaseSettings phase)
    {
        var copy = this;
        copy._own = phase;
        return copy;
    }

    public void Apply(AlpsAnimatableValue target)
    {
        target.isRange = _kind == 1 || _kind == 3;
        target.hasSpread = _kind == 2 || _kind == 3;
        target.value = _a;
        if (_kind == 1)
        {
            target.range = new Vector2(_a, _b);
        }
        else if (_kind == 2)
        {
            target.spreadRange = new Vector2(_a, _b);
            target.spreadRangeEnd = target.spreadRange;
        }
        else if (_kind == 3)
        {
            target.spreadRange = new Vector2(_a, _b);
            target.spreadRangeEnd = new Vector2(_c, _d);
        }

        target.timing = _perCycle ? AlpsTimingMode.PerCycle : AlpsTimingMode.WithinCycle;
        target.useOwnPhase = _own != null;
        if (_own != null)
        {
            target.ownPhase = new AlpsPhaseSettings(_own);
        }
    }
}

/// <summary>The phases the demo's cues run on.</summary>
public static class P
{
    /// <summary>Up over <paramref name="rise"/> of the cycle, held, down over <paramref name="fall"/>, then low.</summary>
    public static AlpsPhaseSettings Wave(float beats, float rise = 0.5f, float hold = 0f, float fall = 0.5f,
        AlpsEaseType ease = AlpsEaseType.InOutSine, AlpsEaseType fallEase = AlpsEaseType.InOutSine)
    {
        var phase = new AlpsPhaseSettings { mode = AlpsPhaseMode.Wave, beatsPerCycle = beats, ease = ease, fallEase = fallEase };
        phase.SetShares(rise, hold, fall);
        return phase;
    }

    /// <summary>A steady climb over the whole cycle that drops back as the next one starts.</summary>
    public static AlpsPhaseSettings Saw(float beats, AlpsEaseType ease = AlpsEaseType.Linear)
    {
        return Wave(beats, 1f, 0f, 0f, ease, ease);
    }

    /// <summary>Jumps to the top as the cycle starts, holds, and decays.</summary>
    public static AlpsPhaseSettings Hit(float beats, float hold = 0.05f, float fall = 0.45f, AlpsEaseType fallEase = AlpsEaseType.OutQuad)
    {
        return Wave(beats, 0f, hold, fall, AlpsEaseType.Linear, fallEase);
    }

    /// <summary>A smooth seeded wander.</summary>
    public static AlpsPhaseSettings Wander(float beats)
    {
        return new AlpsPhaseSettings { mode = AlpsPhaseMode.Random, beatsPerCycle = beats };
    }

    /// <summary>Once up after the clip starts and once down before it ends.</summary>
    public static AlpsPhaseSettings InOut(float inBeats, float outBeats, AlpsInOutMode mode = AlpsInOutMode.Both,
        AlpsEaseType ease = AlpsEaseType.InOutSine, AlpsEaseType fallEase = AlpsEaseType.InOutSine)
    {
        return new AlpsPhaseSettings
        {
            mode = AlpsPhaseMode.InOut,
            inBeats = inBeats,
            outBeats = outBeats,
            inOutMode = mode,
            ease = ease,
            fallEase = fallEase,
            spreadInBeats = true,
        };
    }

    public static AlpsPhaseSettings In(float beats, AlpsEaseType ease = AlpsEaseType.InOutSine)
    {
        return InOut(beats, 0f, AlpsInOutMode.InOnly, ease, ease);
    }

    public static AlpsPhaseSettings Out(float beats, AlpsEaseType ease = AlpsEaseType.InOutSine)
    {
        return InOut(0f, beats, AlpsInOutMode.OutOnly, ease, ease);
    }

    /// <summary>The wave walks the whole group over this share of a cycle.</summary>
    public static AlpsPhaseSettings Spread(this AlpsPhaseSettings phase, float cycles)
    {
        phase.spread = cycles;
        phase.spreadInBeats = false;
        return phase;
    }

    /// <summary>Each fixture runs this many beats behind the one before it.</summary>
    public static AlpsPhaseSettings Delay(this AlpsPhaseSettings phase, float beats)
    {
        phase.SetSpreadBeats(beats);
        phase.spreadInBeats = true;
        return phase;
    }

    /// <summary>Only this share of the cycles fire, rolled per fixture.</summary>
    public static AlpsPhaseSettings Fire(this AlpsPhaseSettings phase, float chance)
    {
        phase.fireChance = chance;
        return phase;
    }

    public static AlpsPhaseSettings Inverted(this AlpsPhaseSettings phase)
    {
        phase.inverse = true;
        return phase;
    }

    /// <summary>Neighbours in groups of this size move as one.</summary>
    public static AlpsPhaseSettings Grouped(this AlpsPhaseSettings phase, int size)
    {
        phase.fixtureGroupSize = size;
        return phase;
    }
}

/// <summary>Builds effect sets the way the demo's cue sheet reads.</summary>
public static class Cue
{
    public static AlpsClipEffectSet On(AlpsPhaseSettings phase = null)
    {
        var set = new AlpsClipEffectSet();
        if (phase != null)
        {
            set.phase = phase;
        }

        return set;
    }

    /// <summary>Counts the fixtures from the middle outward, both sides alike, and mirrors pan.</summary>
    public static AlpsClipEffectSet Sym(this AlpsClipEffectSet set)
    {
        set.symmetric = true;
        return set;
    }

    public static AlpsClipEffectSet Reverse(this AlpsClipEffectSet set)
    {
        set.order = AlpsOrderMode.Reverse;
        return set;
    }

    public static AlpsClipEffectSet Shuffled(this AlpsClipEffectSet set, int seed)
    {
        set.order = AlpsOrderMode.Random;
        set.orderSeed = seed;
        return set;
    }

    /// <summary>The clip's own tempo, where the song leaves the show's.</summary>
    public static AlpsClipEffectSet Tempo(this AlpsClipEffectSet set, float bpm)
    {
        set.bpm = bpm;
        return set;
    }

    // --- Effects. Adding a kind a second time splits it into even and odd fixtures. ---------

    public static AlpsClipEffectSet Dim(this AlpsClipEffectSet set, V brightness, float fadeIn = 0f, float fadeOut = 0f, float offset = 0f)
    {
        var effect = set.Add(AlpsEffectKind.Brightness);
        brightness.Apply(effect.brightness);
        effect.fadeInBeats = fadeIn;
        effect.fadeOutBeats = fadeOut;
        effect.phaseOffset = offset;
        return set;
    }

    /// <summary>One colour, or a palette the phase walks through.</summary>
    public static AlpsClipEffectSet Color(this AlpsClipEffectSet set, params Color[] colors)
    {
        Palette(set, false, null, 0f, colors);
        return set;
    }

    /// <summary>A palette that steps to its next colour every cycle.</summary>
    public static AlpsClipEffectSet ColorSteps(this AlpsClipEffectSet set, params Color[] colors)
    {
        Palette(set, true, null, 0f, colors);
        return set;
    }

    /// <summary>A palette on a phase of its own, sweeping or stepping.</summary>
    public static AlpsClipEffectSet ColorOn(this AlpsClipEffectSet set, AlpsPhaseSettings phase, bool steps, params Color[] colors)
    {
        Palette(set, steps, phase, 0f, colors);
        return set;
    }

    /// <summary>A gradient the phase runs along, so a spread lays it across the fixtures.</summary>
    public static AlpsClipEffectSet Gradient(this AlpsClipEffectSet set, params Color[] keys)
    {
        return GradientOn(set, null, keys);
    }

    public static AlpsClipEffectSet GradientOn(this AlpsClipEffectSet set, AlpsPhaseSettings phase, params Color[] keys)
    {
        var effect = set.Add(AlpsEffectKind.Color);
        effect.colorStops.Clear();
        effect.colorStops.Add(new AlpsColorStop { isGradient = true, gradient = MakeGradient(keys) });
        effect.colorPhasing.isRange = true;
        effect.colorPhasing.useOwnPhase = phase != null;
        if (phase != null)
        {
            effect.colorPhasing.ownPhase = new AlpsPhaseSettings(phase);
        }

        return set;
    }

    private static void Palette(AlpsClipEffectSet set, bool steps, AlpsPhaseSettings phase, float offset, Color[] colors)
    {
        var effect = set.Add(AlpsEffectKind.Color);
        effect.colorStops.Clear();
        foreach (var color in colors)
        {
            effect.colorStops.Add(new AlpsColorStop(color));
        }

        effect.colorPhasing.isRange = true;
        effect.colorPhasing.timing = steps ? AlpsTimingMode.PerCycle : AlpsTimingMode.WithinCycle;
        effect.colorPhasing.useOwnPhase = phase != null;
        if (phase != null)
        {
            effect.colorPhasing.ownPhase = new AlpsPhaseSettings(phase);
        }

        effect.phaseOffset = offset;
    }

    public static Gradient MakeGradient(params Color[] keys)
    {
        var gradient = new Gradient();
        var count = Mathf.Min(keys.Length, 8);
        var colorKeys = new GradientColorKey[count];
        for (var i = 0; i < count; i++)
        {
            colorKeys[i] = new GradientColorKey(keys[i], count > 1 ? i / (count - 1f) : 0f);
        }

        gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }

    /// <summary>Tilt and pan. <paramref name="blackout"/> darkens the light while the head comes back.</summary>
    public static AlpsClipEffectSet Move(this AlpsClipEffectSet set, V tilt, V pan, bool blackout = false, float offset = 0f, float panLead = 90f)
    {
        var effect = set.Add(AlpsEffectKind.Move);
        effect.moveMode = AlpsMoveMode.Angle;
        tilt.Apply(effect.tilt);
        pan.Apply(effect.pan);
        effect.panTiltPhaseOffsetDegrees = panLead;
        effect.phaseOffset = offset;
        Blackout(effect, blackout);
        return set;
    }

    /// <summary>A ring around a center direction, once round per cycle.</summary>
    public static AlpsClipEffectSet Circle(this AlpsClipEffectSet set, V centerTilt, V centerPan, V radius, float aspect = 1f, bool blackout = false, float offset = 0f)
    {
        var effect = set.Add(AlpsEffectKind.Move);
        effect.moveMode = AlpsMoveMode.Circle;
        centerTilt.Apply(effect.circleCenterTilt);
        centerPan.Apply(effect.circleCenterPan);
        radius.Apply(effect.circleRadius);
        effect.circleAspect = aspect;
        effect.phaseOffset = offset;
        Blackout(effect, blackout);
        return set;
    }

    /// <summary>Follows a user while the show plays. The preview keeps the pose of the layers below.</summary>
    public static AlpsClipEffectSet Track(this AlpsClipEffectSet set, string userName, float speed = 5.5f)
    {
        var effect = set.Add(AlpsEffectKind.Move);
        effect.moveMode = AlpsMoveMode.TrackUser;
        effect.trackUserName = userName;
        effect.trackSpeed = speed;
        return set;
    }

    private static void Blackout(AlpsEffect effect, bool blackout)
    {
        effect.blackoutOnReturn = blackout;
        effect.blackoutFadeIn = blackout ? 0.1f : 0f;
        effect.blackoutFadeOut = blackout ? 0.25f : 0f;
    }

    /// <summary>Cone width in degrees and length, where 50 is the fixture's own cone.</summary>
    public static AlpsClipEffectSet Cone(this AlpsClipEffectSet set, V width, V length)
    {
        var effect = set.Add(AlpsEffectKind.Cone);
        width.Apply(effect.coneWidth);
        length.Apply(effect.coneLength);
        return set;
    }

    /// <summary>Gobos by VRSL number, 1 for the open beam. More than one is a palette like a colour's.</summary>
    public static AlpsClipEffectSet Gobo(this AlpsClipEffectSet set, float turnBeats, float staggerDegrees, bool steps, params int[] gobos)
    {
        var effect = set.Add(AlpsEffectKind.Gobo);
        effect.goboStops.Clear();
        foreach (var gobo in gobos)
        {
            effect.goboStops.Add(new AlpsGoboStop(gobo));
        }

        effect.goboPhasing.isRange = true;
        effect.goboPhasing.timing = steps ? AlpsTimingMode.PerCycle : AlpsTimingMode.WithinCycle;
        effect.goboRotationBeats = turnBeats;
        effect.goboFixtureStaggerDegrees = staggerDegrees;
        return set;
    }

    /// <summary>
    /// Turns a mirror ball once round every <paramref name="beats"/> beats, counted from the
    /// clip's start. A ball no clip turns keeps to its own slow speed.
    /// </summary>
    public static AlpsClipEffectSet Turn(this AlpsClipEffectSet set, float beats)
    {
        return set.Gobo(beats, 0f, false, 1);
    }

    /// <summary>
    /// A laser's beams: how flat its cone is in percent, from a round cone at 0 to a fan at
    /// 100, how many beams it has, how thick they are in percent, and the beats they take to
    /// go once round the cone, 0 to leave them standing. Left out, a value is the demo's fan.
    /// </summary>
    public static AlpsClipEffectSet Laser(this AlpsClipEffectSet set, V? flatness = null, V? beams = null, V? thickness = null, float spinBeats = 0f)
    {
        var effect = set.Add(AlpsEffectKind.Laser);
        (flatness ?? AlpsDemoRig.LaserFanFlatness).Apply(effect.laserFlatness);
        (beams ?? AlpsDemoRig.LaserFanBeams).Apply(effect.laserBeams);
        (thickness ?? AlpsDemoRig.LaserFanThickness).Apply(effect.laserThickness);
        effect.laserSpinBeats = spinBeats;
        return set;
    }

    public static AlpsClipEffectSet Flicker(this AlpsClipEffectSet set, float speed, float strength, float stagger = 0.6f)
    {
        var effect = set.Add(AlpsEffectKind.Flicker);
        effect.flickerSpeed = speed;
        effect.flickerStrength = strength;
        effect.flickerFixtureStagger = stagger;
        return set;
    }
}

/// <summary>The demo's colours, saturated the way stage light is.</summary>
public static class Ink
{
    public static readonly Color White = new Color(1f, 1f, 1f);
    public static readonly Color Warm = new Color(1f, 0.78f, 0.52f);
    public static readonly Color Ice = new Color(0.7f, 0.88f, 1f);
    public static readonly Color Blue = new Color(0.05f, 0.18f, 1f);
    public static readonly Color Azure = new Color(0.15f, 0.45f, 1f);
    public static readonly Color DeepBlue = new Color(0.03f, 0.04f, 0.75f);
    public static readonly Color Cyan = new Color(0f, 0.78f, 1f);
    public static readonly Color Teal = new Color(0f, 1f, 0.62f);
    public static readonly Color Green = new Color(0.1f, 1f, 0.15f);
    public static readonly Color Lime = new Color(0.6f, 1f, 0f);
    public static readonly Color Yellow = new Color(1f, 0.85f, 0f);
    public static readonly Color Amber = new Color(1f, 0.5f, 0.03f);
    public static readonly Color Orange = new Color(1f, 0.26f, 0f);
    public static readonly Color Red = new Color(1f, 0.02f, 0.02f);
    public static readonly Color Pink = new Color(1f, 0.15f, 0.45f);
    public static readonly Color Magenta = new Color(1f, 0f, 0.7f);
    public static readonly Color Violet = new Color(0.45f, 0.08f, 1f);
    public static readonly Color Lavender = new Color(0.6f, 0.48f, 1f);

    /// <summary>Once round the colour wheel, ending where it starts so a scroll has no seam.</summary>
    public static readonly Color[] Rainbow = { Red, Yellow, Green, Cyan, Blue, Magenta, Red };
}
