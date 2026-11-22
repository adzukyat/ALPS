using System.Collections.Generic;
using System.Linq;
using AdzukiSoft.ALPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using static AlpsDemoRig;

/// <summary>
/// Writes the demo's show into its timeline: 112 bars at 128 BPM, three and a half minutes,
/// in the sections <see cref="Sections"/> lists.
///
/// Every container has a lighting track with layers below it, the way a show is usually
/// laid out: the beam (cone and gobo), the move, the colour, the dimmer and effects on top,
/// so each can change on its own. The whole rig has one more track at the end, which lies
/// above everything and takes the accents that hit every light at once.
///
/// Read <see cref="AlpsDemoRig"/> for what tilt and pan mean on each group. Where a clip is
/// symmetric, a pan spread opens outward with a negative pan on the hung movers and with a
/// positive one on the standing ones, since the two are mounted the other way up. The lasers
/// are fans: tilt lifts and lowers the sheet of beams, and pan rolls it. The pixel bars are
/// 48 segments in a row, so their phases are spread and delayed along the riser.
/// </summary>
public sealed class AlpsDemoShow
{
    public const string TimelinePath = "Assets/ExampleTimeline.playable";
    public const string ProfileFolder = AlpsDemoVenue.Folder + "/Profiles";
    public const float Bpm = 128f;
    public const int Bars = 112;

    public const string Beam = "Beam";
    public const string Move = "Move";
    public const string Colour = "Colour";
    public const string Dimmer = "Dimmer";
    public const string Fx = "FX";
    public const string Accent = "Accent";

    /// <summary>The cone length that takes a laser's beams from the back truss to the back of the hall.</summary>
    private const float LaserReach = 240f;

    /// <summary>The song: each section's name, its first bar and how many bars it lasts.</summary>
    public static readonly (string name, int bar, int bars)[] Sections =
    {
        ("Intro", 1, 8),
        ("Build", 9, 8),
        ("Drop A", 17, 8),
        ("Drop B", 25, 8),
        ("Break", 33, 8),
        ("Verse", 41, 16),
        ("Riser", 57, 8),
        ("Drop C", 65, 8),
        ("Rainbow", 73, 8),
        ("Bridge", 81, 8),
        ("Drop A again", 89, 8),
        ("Rainbow again", 97, 8),
        ("Outro", 105, 8),
    };

    private static readonly string[] Movers = { FrontWash, SideWash, Halo, BackSpots, MidSpots, FloorSpots, TotemSpots, Lasers };
    private static readonly string[] Spots = { BackSpots, MidSpots, FloorSpots, TotemSpots };

    private readonly TimelineAsset _timeline;
    private readonly Dictionary<(string container, string lane), AlpsLightingTrack> _lanes =
        new Dictionary<(string container, string lane), AlpsLightingTrack>();
    private readonly Dictionary<string, AlpsClipProfile> _profiles = new Dictionary<string, AlpsClipProfile>();

    /// <summary>While set, clips follow a profile saved under this name, so a section played twice shares its looks.</summary>
    private string _library;

    /// <summary>Seconds from the start of the show to the start of bar 1 + <paramref name="bars"/>.</summary>
    public static double Seconds(double bars)
    {
        return bars * 4.0 * 60.0 / Bpm;
    }

    public static double Duration => Seconds(Bars) + 2.0;

    public static TimelineAsset Build(PlayableDirector director, Dictionary<string, AlpsTarget> targets)
    {
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }

        // The asset is kept and emptied, so the scene's reference to it holds.
        foreach (var track in timeline.GetRootTracks().ToList())
        {
            timeline.DeleteTrack(track);
        }

        timeline.name = "ExampleTimeline";
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = Duration;
        timeline.editorSettings.frameRate = 60.0;

        director.playableAsset = timeline;
        var show = new AlpsDemoShow(timeline);
        show.AddTracks(director, targets);
        show.Write();
        show.RemoveUnusedProfiles();

        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        return timeline;
    }

    private AlpsDemoShow(TimelineAsset timeline)
    {
        _timeline = timeline;
    }

    // ======================================================================================
    // Tracks
    // ======================================================================================

    private void AddTracks(PlayableDirector director, Dictionary<string, AlpsTarget> targets)
    {
        var deck = _timeline.CreateTrack<GroupTrack>(null, "Deck");
        var roof = _timeline.CreateTrack<GroupTrack>(null, "Roof");
        var audience = _timeline.CreateTrack<GroupTrack>(null, "Audience");
        foreach (var container in Containers)
        {
            var onDeck = container == CycPars || container == ApronBars || container == PixelBars || container == TotemBars
                         || container == FloorSpots || container == TotemSpots;
            var lanes = Movers.Contains(container)
                ? new[] { Beam, Move, Colour, Dimmer, Fx }
                : new[] { Colour, Dimmer, Fx };
            AddTrack(director, onDeck ? deck : roof, container, targets[container], lanes);
        }

        AddTrack(director, audience, PenlightSweep, targets[PenlightSweep], new[] { Colour, Dimmer });
        AddTrack(director, audience, PenlightRipple, targets[PenlightRipple], new[] { Colour, Dimmer });

        // Last, so it lies above every other track.
        AddTrack(director, null, All, targets[All], new[] { Accent });
    }

    /// <summary>
    /// A lighting track bound to the target, which takes the first lane, and a layer below
    /// it for each of the others, in the order they cover each other.
    /// </summary>
    private void AddTrack(PlayableDirector director, GroupTrack group, string name, AlpsTarget target, string[] lanes)
    {
        var root = _timeline.CreateTrack<AlpsLightingTrack>(group, $"{name}  ·  {lanes[0]}");
        root.bpm = Bpm;
        director.SetGenericBinding(root, target);
        _lanes[(name, lanes[0])] = root;
        for (var i = 1; i < lanes.Length; i++)
        {
            var layer = _timeline.CreateTrack<AlpsLightingTrack>(root, lanes[i]);
            layer.bpm = Bpm;
            _lanes[(name, lanes[i])] = layer;
        }
    }

    // ======================================================================================
    // Clips
    // ======================================================================================

    /// <summary>
    /// A clip on a container's lane from <paramref name="bar"/>, counted from 1, for
    /// <paramref name="bars"/> bars.
    /// </summary>
    private TimelineClip Put(string container, string lane, float bar, float bars, string name, AlpsClipEffectSet set)
    {
        var clip = _lanes[(container, lane)].CreateClip<AlpsLightingClip>();
        clip.displayName = name;
        clip.start = Seconds(bar - 1.0);
        // The end is worked out the way the next clip's start is, so neighbours meet exactly.
        clip.duration = Seconds(bar - 1.0 + bars) - clip.start;
        var asset = (AlpsLightingClip)clip.asset;
        asset.name = name;
        asset.data = set;
        if (_library != null)
        {
            asset.profile = Profile($"{_library} - {container} {lane} - {name}", set);
            asset.syncProfile = true;
            asset.data = new AlpsClipEffectSet(set);
        }

        return clip;
    }

    private TimelineClip B(string container, float bar, float bars, string name, AlpsClipEffectSet set) => Put(container, Beam, bar, bars, name, set);
    private TimelineClip M(string container, float bar, float bars, string name, AlpsClipEffectSet set) => Put(container, Move, bar, bars, name, set);
    private TimelineClip C(string container, float bar, float bars, string name, AlpsClipEffectSet set) => Put(container, Colour, bar, bars, name, set);
    private TimelineClip D(string container, float bar, float bars, string name, AlpsClipEffectSet set) => Put(container, Dimmer, bar, bars, name, set);
    private TimelineClip F(string container, float bar, float bars, string name, AlpsClipEffectSet set) => Put(container, Fx, bar, bars, name, set);

    /// <summary>One colour for the whole clip.</summary>
    private TimelineClip C(string container, float bar, float bars, string name, Color color) => C(container, bar, bars, name, Cue.On().Color(color));

    /// <summary>One level for the whole clip.</summary>
    private TimelineClip D(string container, float bar, float bars, float level) => D(container, bar, bars, $"{level:0}%", Cue.On().Dim(level));

    /// <summary>Lets the earlier clip run on under the later one, so Timeline blends the two over <paramref name="beats"/>.</summary>
    private static void Cross(TimelineClip earlier, TimelineClip later, float beats)
    {
        var overlap = Seconds(beats / 4.0);
        earlier.duration += overlap;
        earlier.blendOutDuration = overlap;
        later.blendInDuration = overlap;
    }

    private static TimelineClip Ease(TimelineClip clip, float inBeats, float outBeats)
    {
        clip.easeInDuration = Seconds(inBeats / 4.0);
        clip.easeOutDuration = Seconds(outBeats / 4.0);
        return clip;
    }

    private AlpsClipProfile Profile(string name, AlpsClipEffectSet set)
    {
        if (_profiles.TryGetValue(name, out var known))
        {
            return known;
        }

        var path = $"{ProfileFolder}/{name.Replace('/', '-')}.asset";
        var profile = AlpsDemoVenue.Asset<AlpsClipProfile>(path, ScriptableObject.CreateInstance<AlpsClipProfile>);
        profile.data = new AlpsClipEffectSet(set);
        EditorUtility.SetDirty(profile);
        _profiles[name] = profile;
        return profile;
    }

    /// <summary>Profiles of an earlier build that this one no longer writes.</summary>
    private void RemoveUnusedProfiles()
    {
        if (!AssetDatabase.IsValidFolder(ProfileFolder))
        {
            return;
        }

        var used = new HashSet<string>(_profiles.Values.Select(AssetDatabase.GetAssetPath));
        foreach (var guid in AssetDatabase.FindAssets("t:AlpsClipProfile", new[] { ProfileFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!used.Contains(path))
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }

    // ======================================================================================
    // The show
    // ======================================================================================

    private void Write()
    {
        Intro(1);
        BuildUp(9);
        Library("Drop A", () => DropA(17));
        DropB(25);
        Break(33);
        Verse(41);
        Riser(57);
        DropC(65);
        Library("Rainbow", () => Rainbow(73));
        Bridge(81);
        Library("Drop A", () => DropA(89));
        FinaleExtras(89);
        Library("Rainbow", () => Rainbow(97));
        Outro(105);
    }

    private void Library(string name, System.Action section)
    {
        _library = name;
        section();
        _library = null;
    }

    // --- Shared looks ---------------------------------------------------------------------

    /// <summary>The side washes thrown across the deck from both wings.</summary>
    private void SideAcross(float bar, float bars, float cone = 55f)
    {
        B(SideWash, bar, bars, "Wide", Cue.On().Cone(cone, 45));
        M(SideWash, bar, bars, "Across The Deck", Cue.On().Sym().Move(38, 90));
    }

    /// <summary>The front washes turned round onto the band.</summary>
    private void FrontOnStage(float bar, float bars)
    {
        B(FrontWash, bar, bars, "Wide", Cue.On().Cone(50, 90));
        M(FrontWash, bar, bars, "On The Band", Cue.On().Move(-32, 0));
    }

    /// <summary>The front washes swept out over the audience and back.</summary>
    private void FrontOverCrowd(float bar, float bars, float beats)
    {
        B(FrontWash, bar, bars, "Medium", Cue.On().Cone(30, 100));
        M(FrontWash, bar, bars, "Over The Crowd", Cue.On(P.Wave(beats)).Sym().Move(V.R(78, 50), V.S(0, -40)));
    }

    /// <summary>The totem spots straight up, fanned a little away from the middle.</summary>
    private void TotemsUp(float bar, float bars)
    {
        B(TotemSpots, bar, bars, "Needle", Cue.On().Cone(4, 140));
        M(TotemSpots, bar, bars, "Up And Out", Cue.On().Sym().Move(V.S(10, 30), 80));
    }

    private static AlpsClipEffectSet Kick(float low, float high)
    {
        return Cue.On(P.Hit(1, 0.1f, 0.6f)).Dim(V.R(low, high));
    }

    private static AlpsClipEffectSet Strobe(float chance)
    {
        return Cue.On(P.Hit(0.5f, 0.3f, 0.15f).Fire(chance)).Dim(V.R(0, 100));
    }

    /// <summary>A flash of the whole rig that Timeline eases out of, back into the look below.</summary>
    private void Flash(float bar)
    {
        Ease(Put(All, Accent, bar, 0.5f, "White Flash", Cue.On().Color(Ink.White).Dim(140)), 0f, 1.5f);
    }

    // --- 1: Intro -------------------------------------------------------------------------
    // The room wakes up: a ring of light over the stage, the cyclorama breathing, and four
    // bars in, the back truss lifting out of the floor.
    private void Intro(float b)
    {
        C(CycPars, b, 8, "Deep Blue", Ink.DeepBlue);
        D(CycPars, b, 8, "Breathe", Cue.On(P.Wave(16).Spread(0.5f)).Sym().Dim(V.R(6, 30), fadeIn: 4));

        C(TotemBars, b, 8, "Cyan", Ink.Cyan);
        D(TotemBars, b, 8, "Rise From The Middle", Cue.On(P.InOut(8, 8).Delay(1.5f)).Sym().Dim(V.R(0, 22)));

        C(ApronBars, b, 8, "Deep Blue", Ink.DeepBlue);
        D(ApronBars, b, 8, "Drift", Cue.On(P.Wave(8).Spread(1)).Dim(V.R(2, 12), fadeIn: 4));

        C(PixelBars, b, 8, "Deep Blue", Ink.DeepBlue);
        D(PixelBars, b, 8, "Comet", Cue.On(P.Wave(8, 0.08f, 0.02f, 0.5f).Spread(1)).Dim(V.R(0, 60), fadeIn: 8));

        B(Halo, b, 8, "Pencil", Cue.On().Cone(10, 110));
        M(Halo, b, 8, "Straight Down", Cue.On().Move(0, 0));
        C(Halo, b, 8, "Deep Blue", Ink.DeepBlue);
        D(Halo, b, 8, "Slow Chase", Cue.On(P.Wave(8, 0.3f, 0.1f, 0.4f).Spread(1)).Dim(V.R(4, 40), fadeIn: 8));

        B(BackSpots, b + 4, 4, "Pencil", Cue.On().Cone(7, 120));
        M(BackSpots, b + 4, 4, "Lift", Cue.On(P.In(14).Delay(0.4f)).Sym().Move(V.R(0, 62), V.S(0, -30)));
        C(BackSpots, b + 4, 4, "Ice", Ink.Ice);
        D(BackSpots, b + 4, 4, "Fade Up", Cue.On(P.In(8).Delay(0.4f)).Sym().Dim(V.R(0, 55)));

        C(PenlightRipple, b, 8, "Deep Blue", Ink.DeepBlue);
        D(PenlightRipple, b, 8, "Slow Ripple", Cue.On(P.Wave(8, 0.3f, 0.1f, 0.5f).Spread(1)).Dim(V.R(0, 45), fadeIn: 8));
    }

    // --- 9: Build -------------------------------------------------------------------------
    // The fan opens, the floor package leans out, and everything stops half a bar early.
    private void BuildUp(float b)
    {
        const float len = 7.5f;
        C(CycPars, b, len, "Blue To Violet", Cue.On(P.Wave(16).Spread(0.5f)).Color(Ink.DeepBlue, Ink.Violet));
        D(CycPars, b, len, 34);

        B(BackSpots, b, len, "Pencil", Cue.On().Cone(7, 120));
        M(BackSpots, b, len, "Fan Opens", Cue.On(P.In(24)).Sym().Move(62, V.SR(0, -30, 0, -62)));
        C(BackSpots, b, len, "Ice To Cyan", Cue.On(P.In(24)).Color(Ink.Ice, Ink.Cyan));
        D(BackSpots, b, len, 60);

        B(FloorSpots, b, len, "Needle", Cue.On().Cone(5, 140));
        M(FloorSpots, b, len, "Lean Out", Cue.On(P.In(20)).Sym().Move(V.R(0, 32), V.S(0, 50)));
        C(FloorSpots, b, len, "Blue", Ink.Blue);
        D(FloorSpots, b, len, "Fade Up", Cue.On(P.In(16)).Dim(V.R(0, 60)));

        B(MidSpots, b, len, "Gobo", Cue.On().Cone(16, 100).Gobo(8, 30, false, 4));
        M(MidSpots, b, len, "Down On The Band", Cue.On().Sym().Move(10, V.S(0, 18)));
        C(MidSpots, b, len, "White", Ink.White);
        D(MidSpots, b, len, "Sparkle", Cue.On(P.Hit(1, 0.15f, 0.5f).Fire(0.22f)).Dim(V.R(0, 70)));

        C(TotemBars, b, len, "Cyan", Ink.Cyan);
        D(TotemBars, b, len, "On The One", Cue.On(P.Hit(4, 0.03f, 0.3f)).Dim(V.R(0, 45)));

        C(ApronBars, b, len, "Blue", Ink.Blue);
        D(ApronBars, b, len, "Chase Out", Cue.On(P.Wave(2, 0.1f, 0.1f, 0.3f).Spread(1)).Sym().Dim(V.R(0, 30)));

        C(PixelBars, b, len, "Blue Through Cyan", Cue.On(P.Saw(8).Spread(1)).Gradient(Ink.Blue, Ink.Cyan, Ink.Blue));
        D(PixelBars, b, len, "Chase Out", Cue.On(P.Wave(2, 0.1f, 0.1f, 0.4f).Spread(1)).Sym().Dim(V.R(0, 70)));

        FrontOnStage(b, len);
        C(FrontWash, b, len, "Deep Blue", Ink.DeepBlue);
        D(FrontWash, b, len, 35);

        SideAcross(b, len);
        C(SideWash, b, len, "Violet", Ink.Violet);
        D(SideWash, b, len, 30);

        B(Halo, b, len, "Soft", Cue.On().Cone(14, 110));
        M(Halo, b, len, "Tip Out", Cue.On(P.In(24)).Move(V.R(0, 28), 0));
        C(Halo, b, len, "Blue Through Cyan", Cue.On(P.Saw(4).Spread(1)).Gradient(Ink.Blue, Ink.Cyan, Ink.Blue));
        D(Halo, b, len, "Chase", Cue.On(P.Wave(4, 0.3f, 0.1f, 0.4f).Spread(1)).Dim(V.R(8, 50)));

        C(Blinders, b + 6, 1.5f, "Warm", Ink.Warm);
        D(Blinders, b + 6, 1.5f, "Swell", Cue.On(P.In(6, AlpsEaseType.InExpo)).Dim(V.R(0, 85)));

        C(PenlightSweep, b, len, "Blue", Ink.Blue);
        D(PenlightSweep, b, len, "Wave", Cue.On(P.Wave(4).Spread(1)).Dim(V.R(5, 70)));
    }

    // --- 17 and 89: Drop A ----------------------------------------------------------------
    // Magenta and cyan. Mids fly out with the light off on the way back, the back truss
    // scissors, the floor package twirls.
    private void DropA(float b)
    {
        C(Blinders, b, 8, "Warm", Ink.Warm);
        D(Blinders, b, 8, "Every Fourth Bar", Cue.On(P.Hit(16, 0.02f, 0.14f)).Dim(V.R(0, 100)));

        B(MidSpots, b, 8, "Beam", Cue.On().Cone(9, 120));
        M(MidSpots, b, 8, "Fly Out", Cue.On(P.Wave(2, 0.6f, 0f, 0.4f, AlpsEaseType.OutCubic).Spread(1)).Sym()
            .Move(V.R(5, 82), V.S(0, -30), blackout: true));
        C(MidSpots, b, 8, "Magenta", Ink.Magenta);
        D(MidSpots, b, 8, 100);

        B(BackSpots, b, 8, "Gobo Spin", Cue.On().Cone(17, 110).Gobo(4, 36, false, 3));
        M(BackSpots, b, 8, "Scissor Pan", Cue.On(P.Wave(4)).Move(52, V.R(-55, 55)).Move(52, V.R(55, -55)));
        C(BackSpots, b, 8, "Cyan", Ink.Cyan);
        D(BackSpots, b, 8, 90);

        B(FloorSpots, b, 8, "Needle", Cue.On().Cone(5, 140));
        M(FloorSpots, b, 8, "Twirl", Cue.On(P.Saw(4).Spread(1)).Circle(28, 0, 24));
        C(FloorSpots, b, 8, "White / Cyan", Cue.On(P.Saw(4)).ColorSteps(Ink.White, Ink.Cyan));
        D(FloorSpots, b, 8, 100);

        TotemsUp(b, 8);
        C(TotemSpots, b, 8, "White", Ink.White);
        D(TotemSpots, b, 8, "Strobe", Strobe(0.5f));

        B(Halo, b, 8, "Medium", Cue.On().Cone(14, 110));
        M(Halo, b, 8, "Out", Cue.On().Move(35, 0));
        C(Halo, b, 8, "Magenta Through Blue", Cue.On(P.Saw(4).Spread(1)).Gradient(Ink.Magenta, Ink.Blue, Ink.Magenta));
        D(Halo, b, 8, "Two Runners", Cue.On(P.Wave(2, 0.05f, 0.15f, 0.3f).Spread(2)).Dim(V.R(10, 100)));

        SideAcross(b, 8);
        C(SideWash, b, 8, "Magenta / Cyan", Cue.On(P.Saw(4)).ColorSteps(Ink.Magenta, Ink.Cyan));
        D(SideWash, b, 8, 70);

        FrontOverCrowd(b, 8, 8);
        C(FrontWash, b, 8, "Blue", Ink.Blue);
        D(FrontWash, b, 8, 45);

        B(Lasers, b, 8, "Wide Fan", Cue.On().Cone(34, LaserReach));
        M(Lasers, b, 8, "Liquid Sky", Cue.On(P.Wave(4).Spread(1)).Sym().Move(V.R(-24, 4), 0));
        C(Lasers, b, 8, "Cyan", Ink.Cyan);
        D(Lasers, b, 8, "Kick", Kick(35, 100));

        C(CycPars, b, 8, "Magenta / Cyan / Blue", Cue.On(P.Saw(2).Spread(0.5f)).ColorSteps(Ink.Magenta, Ink.Cyan, Ink.Blue));
        D(CycPars, b, 8, "Kick", Kick(35, 90));

        C(TotemBars, b, 8, "Magenta", Ink.Magenta);
        D(TotemBars, b, 8, "Kick From The Wings", Cue.On(P.Hit(1, 0.1f, 0.5f).Delay(0.125f)).Sym().Reverse().Dim(V.R(0, 80)));

        C(ApronBars, b, 8, "White", Ink.White);
        D(ApronBars, b, 8, "Run", Cue.On(P.Hit(1, 0.2f, 0.3f).Spread(1)).Dim(V.R(0, 90)));

        C(PixelBars, b, 8, "Magenta Through Cyan", Cue.On(P.Saw(2).Spread(2)).Gradient(Ink.Magenta, Ink.Cyan, Ink.Magenta));
        D(PixelBars, b, 8, "Kick From The Middle", Cue.On(P.Hit(1, 0.1f, 0.6f).Delay(0.02f)).Sym().Dim(V.R(8, 100)));

        C(PenlightSweep, b, 8, "Magenta / Cyan", Cue.On(P.Saw(2)).ColorSteps(Ink.Magenta, Ink.Cyan));
        D(PenlightSweep, b, 8, "Run", Cue.On(P.Wave(2, 0.1f, 0.2f, 0.4f).Spread(1)).Dim(V.R(0, 100)));
    }

    // --- 25: Drop B -----------------------------------------------------------------------
    // The same energy in amber and red, with the moves traded between the trusses.
    private void DropB(float b)
    {
        C(Blinders, b, 8, "Warm", Ink.Warm);
        D(Blinders, b, 7, "Every Fourth Bar", Cue.On(P.Hit(16, 0.02f, 0.14f)).Dim(V.R(0, 100)));
        D(Blinders, b + 7, 1, "Eighths", Cue.On(P.Hit(0.5f, 0.3f, 0.4f)).Dim(V.R(0, 100)));

        B(MidSpots, b, 8, "Beam", Cue.On().Cone(9, 120));
        M(MidSpots, b, 8, "Big Circle", Cue.On(P.Saw(4).Spread(0.5f)).Sym().Circle(45, 0, 30));
        C(MidSpots, b, 8, "Amber", Ink.Amber);
        D(MidSpots, b, 8, 100);

        B(BackSpots, b, 8, "Beam", Cue.On().Cone(8, 120));
        M(BackSpots, b, 8, "Tilt Run", Cue.On(P.Wave(2, 0.6f, 0f, 0.4f, AlpsEaseType.OutCubic).Spread(1))
            .Move(V.R(18, 90), 0, blackout: true));
        C(BackSpots, b, 8, "Red", Ink.Red);
        D(BackSpots, b, 8, 100);

        B(FloorSpots, b, 8, "Needle", Cue.On().Cone(5, 140));
        M(FloorSpots, b, 8, "Cross", Cue.On(P.Wave(4)).Move(40, V.R(-50, 50)).Move(40, V.R(50, -50)));
        C(FloorSpots, b, 8, "White", Ink.White);
        D(FloorSpots, b, 8, 100);

        TotemsUp(b, 8);
        C(TotemSpots, b, 8, "Amber", Ink.Amber);
        D(TotemSpots, b, 8, "Strobe", Strobe(0.5f));

        B(Halo, b, 8, "Medium", Cue.On().Cone(14, 110));
        M(Halo, b, 8, "Nod", Cue.On(P.Wave(2).Spread(1)).Move(V.R(10, 60), 0));
        C(Halo, b, 8, "Amber / Red", Cue.On(P.Saw(2)).ColorSteps(Ink.Amber, Ink.Red));
        D(Halo, b, 8, 100);

        SideAcross(b, 8);
        C(SideWash, b, 8, "Red / Amber", Cue.On(P.Saw(4)).ColorSteps(Ink.Red, Ink.Amber));
        D(SideWash, b, 8, 70);

        FrontOverCrowd(b, 8, 4);
        C(FrontWash, b, 8, "Amber", Ink.Amber);
        D(FrontWash, b, 8, 45);

        B(Lasers, b, 8, "Blade", Cue.On().Cone(22, LaserReach));
        M(Lasers, b, 8, "Blades, On End", Cue.On(P.Wave(2).Spread(0.5f)).Sym().Move(V.R(-28, 28), 90));
        C(Lasers, b, 8, "Red", Ink.Red);
        D(Lasers, b, 8, 100);

        C(CycPars, b, 8, "Red / Amber", Cue.On(P.Saw(2).Spread(0.5f)).ColorSteps(Ink.Red, Ink.Amber));
        D(CycPars, b, 8, "Kick", Kick(35, 90));

        C(TotemBars, b, 8, "Amber", Ink.Amber);
        D(TotemBars, b, 8, "Kick From The Middle", Cue.On(P.Hit(1, 0.1f, 0.5f).Delay(0.125f)).Sym().Dim(V.R(0, 80)));

        C(ApronBars, b, 8, "Red", Ink.Red);
        D(ApronBars, b, 8, "Run Back", Cue.On(P.Hit(1, 0.2f, 0.3f).Spread(1)).Reverse().Dim(V.R(0, 90)));

        C(PixelBars, b, 8, "Amber / Red", Cue.On(P.Saw(2)).ColorSteps(Ink.Amber, Ink.Red));
        D(PixelBars, b, 8, "Two Runners", Cue.On(P.Wave(1, 0.05f, 0.1f, 0.3f).Spread(2)).Dim(V.R(0, 100)));

        C(PenlightSweep, b, 8, "Amber / Red", Cue.On(P.Saw(2)).ColorSteps(Ink.Amber, Ink.Red));
        D(PenlightSweep, b, 8, "Run Back", Cue.On(P.Wave(2, 0.1f, 0.2f, 0.4f).Spread(1)).Reverse().Dim(V.R(0, 100)));
    }

    // --- 33: Break ------------------------------------------------------------------------
    // Nearly dark. The ring opens into a crown, the mids pick the centre out, and the side
    // washes are ready to follow a player.
    private void Break(float b)
    {
        B(Halo, b, 8, "Pencil", Cue.On().Cone(6, 120));
        M(Halo, b, 8, "Crown Opens", Cue.On(P.InOut(16, 12)).Move(V.R(0, 32), 0));
        C(Halo, b, 8, "Amber", Ink.Amber);
        D(Halo, b, 8, "Glow", Cue.On().Dim(70, fadeIn: 4, fadeOut: 4));

        B(MidSpots, b, 8, "Slow Gobo", Cue.On().Cone(12, 110).Gobo(16, 0, false, 6));
        M(MidSpots, b, 8, "Converge", Cue.On().Sym().Move(V.S(10, 45), V.S(0, 85)));
        C(MidSpots, b, 8, "Warm", Ink.Warm);
        D(MidSpots, b, 8, "The Middle Pair", Cue.On().Sym().Dim(V.S(100, -100), fadeIn: 2, fadeOut: 2));

        C(CycPars, b, 8, "Embers", Cue.On(P.Wander(8).Spread(0.7f)).Gradient(Ink.Amber, Ink.Red, Ink.Violet));
        D(CycPars, b, 8, "Low", Cue.On().Dim(22, fadeIn: 2, fadeOut: 2));

        C(TotemBars, b, 8, "Warm", Ink.Warm);
        D(TotemBars, b, 8, "Twinkle", Cue.On(P.Wave(0.5f, 0.2f, 0.1f, 0.6f).Fire(0.12f)).Dim(V.R(0, 60)));

        C(ApronBars, b, 8, "Amber", Ink.Amber);
        D(ApronBars, b, 8, "Breathe", Cue.On(P.Wave(8).Spread(0.5f)).Sym().Dim(V.R(3, 14)));

        C(PixelBars, b, 8, "Warm", Ink.Warm);
        D(PixelBars, b, 8, "Twinkle", Cue.On(P.Wave(0.5f, 0.2f, 0.1f, 0.6f).Fire(0.1f)).Dim(V.R(0, 70)));

        FrontOnStage(b, 8);
        C(FrontWash, b, 8, "Warm", Ink.Warm);
        D(FrontWash, b, 8, "Low", Cue.On().Dim(30, fadeIn: 4, fadeOut: 2));

        B(SideWash, b, 8, "Medium", Cue.On().Cone(30, 100));
        M(SideWash, b, 8, "Centre Stage", Cue.On().Sym().Move(35, 90));
        C(SideWash, b, 8, "Warm", Ink.Warm);
        D(SideWash, b, 8, "Low", Cue.On().Dim(40, fadeIn: 4, fadeOut: 2));
        // Above the move: once a display name is typed into this clip, the heads follow that player.
        F(SideWash, b, 8, "Follow A Player (name one)", Cue.On().Track(string.Empty));

        C(PenlightRipple, b, 8, "Amber", Ink.Amber);
        D(PenlightRipple, b, 8, "Ripple", Cue.On(P.Wave(8, 0.3f, 0.1f, 0.5f).Spread(1)).Dim(V.R(0, 70)));
    }

    // --- 41: Verse ------------------------------------------------------------------------
    // Sixteen bars of groove in teal and violet. The back truss crossfades from one move
    // into another half way, and the mids wander on a random phase.
    private void Verse(float b)
    {
        B(BackSpots, b, 16, "Beam", Cue.On().Cone(10, 120));
        var scissors = M(BackSpots, b, 8, "Scissors", Cue.On(P.Wave(8)).Sym()
            .Move(V.R(70, 30), V.S(0, -20)).Move(V.R(30, 70), V.S(0, -20)));
        var fan = M(BackSpots, b + 8, 8, "Fan Breathes", Cue.On(P.Wave(8)).Sym().Move(54, V.SR(0, 40, 0, -45)));
        Cross(scissors, fan, 4);
        C(BackSpots, b, 16, "Teal / Violet", Cue.On(P.Saw(8)).ColorSteps(Ink.Teal, Ink.Violet));
        D(BackSpots, b, 16, 70);

        B(MidSpots, b, 16, "Gobo", Cue.On().Cone(18, 100).Gobo(8, 20, false, 5));
        M(MidSpots, b, 16, "Wander", Cue.On(P.Wander(10).Spread(0.7f))
            .Move(V.R(45, 5), V.R(-40, 40).Own(P.Wander(7).Spread(0.5f))));
        C(MidSpots, b, 16, "Lavender", Ink.Lavender);
        D(MidSpots, b, 16, 60);

        B(FloorSpots, b, 16, "Needle", Cue.On().Cone(6, 130));
        M(FloorSpots, b, 16, "Sway", Cue.On(P.Wave(8).Delay(0.5f)).Sym().Move(V.R(50, 15), V.S(0, 35)));
        C(FloorSpots, b, 16, "Teal", Ink.Teal);
        D(FloorSpots, b, 16, "Swell", Cue.On(P.Wave(8).Delay(0.5f)).Sym().Dim(V.R(30, 80)));

        B(TotemSpots, b + 8, 8, "Needle", Cue.On().Cone(5, 140));
        M(TotemSpots, b + 8, 8, "Fans", Cue.On(P.Wave(8).Spread(0.5f)).Sym().Move(V.R(30, 2), V.S(5, 32)));
        C(TotemSpots, b + 8, 8, "Teal", Ink.Teal);
        D(TotemSpots, b + 8, 8, "Join In", Cue.On().Dim(55, fadeIn: 4));

        B(Halo, b, 16, "Soft", Cue.On().Cone(16, 110));
        M(Halo, b, 16, "Out", Cue.On().Move(22, 0));
        C(Halo, b, 16, "Teal Through Violet", Cue.On(P.Saw(16).Spread(1)).Gradient(Ink.Teal, Ink.Violet, Ink.Teal));
        D(Halo, b, 16, 45);

        SideAcross(b, 16);
        C(SideWash, b, 16, "Violet To Teal", Cue.On(P.Wave(16)).Color(Ink.Violet, Ink.Teal));
        D(SideWash, b, 16, 45);

        FrontOnStage(b, 16);
        C(FrontWash, b, 16, "Teal To Violet", Cue.On(P.Wave(16)).Color(Ink.Teal, Ink.Violet));
        D(FrontWash, b, 16, 40);

        C(CycPars, b, 16, "Split, Swapping", Cue.On(P.Saw(8)).ColorSteps(Ink.Teal, Ink.Violet).ColorSteps(Ink.Violet, Ink.Teal));
        D(CycPars, b, 16, 40);

        C(TotemBars, b, 16, "Lavender", Ink.Lavender);
        D(TotemBars, b, 16, "Off Beat", Cue.On(P.Hit(1, 0.1f, 0.4f)).Dim(V.R(0, 50), offset: 0.5f));

        C(ApronBars, b, 16, "Violet", Ink.Violet);
        D(ApronBars, b, 16, "Swell", Cue.On(P.Wave(4).Spread(1)).Sym().Dim(V.R(5, 35)));

        C(PixelBars, b, 16, "Teal Through Violet", Cue.On(P.Saw(8).Spread(1)).Gradient(Ink.Teal, Ink.Violet, Ink.Teal));
        D(PixelBars, b, 16, "Swell", Cue.On(P.Wave(4).Spread(1)).Sym().Dim(V.R(10, 60)));

        C(PenlightSweep, b, 16, "Teal", Ink.Teal);
        D(PenlightSweep, b, 16, "Long Wave", Cue.On(P.Wave(16).Spread(1)).Dim(V.R(10, 60)));
        C(PenlightRipple, b + 8, 8, "Violet", Ink.Violet);
        D(PenlightRipple, b + 8, 8, "Ripple", Cue.On(P.Wave(4, 0.3f, 0.1f, 0.5f).Spread(1)).Dim(V.R(0, 80), fadeIn: 4));
    }

    // --- 57: Riser ------------------------------------------------------------------------
    // Every beam climbs for eight bars while the pulse doubles every two. The mids' climb is
    // one clip split in two: the second half carries a Clip In and plays on.
    private void Riser(float b)
    {
        var beats = new[] { 2f, 1f, 0.5f, 0.25f };
        var names = new[] { "Halves", "Quarters", "Eighths", "Sixteenths" };

        var reach = Cue.On(P.In(32, AlpsEaseType.InQuad)).Sym().Move(V.R(2, 88), V.S(0, -20));
        M(MidSpots, b, 4, "Reach", reach);
        var playsOn = M(MidSpots, b + 4, 4, "Reach (split, plays on)", new AlpsClipEffectSet(reach));
        playsOn.clipIn = Seconds(4);
        M(BackSpots, b, 8, "Reach", Cue.On(P.In(32, AlpsEaseType.InQuad)).Sym().Move(V.R(2, 88), V.S(0, -35)));
        M(FloorSpots, b, 8, "Stand Up", Cue.On(P.In(32, AlpsEaseType.InQuad)).Sym().Move(V.R(65, 0), V.S(0, 30)));
        M(TotemSpots, b, 8, "Stand Up", Cue.On(P.In(32, AlpsEaseType.InQuad)).Sym().Move(V.R(55, 0), V.S(5, 25)));

        foreach (var spots in Spots)
        {
            B(spots, b, 8, "Tighten", Cue.On(P.In(32)).Cone(V.R(14, 4), 130));
            C(spots, b, 8, "Blue To White", Cue.On(P.In(32)).Gradient(Ink.Blue, Ink.Ice, Ink.White));
            for (var i = 0; i < 4; i++)
            {
                D(spots, b + i * 2, 2, names[i], Cue.On(P.Hit(beats[i], 0.3f, 0.3f)).Dim(V.R(15, 100)));
            }
        }

        F(MidSpots, b + 6, 2, "Flicker", Cue.On().Flicker(22, 0.6f));
        F(BackSpots, b + 6, 2, "Flicker", Cue.On().Flicker(22, 0.6f));

        B(Lasers, b, 8, "Close Up", Cue.On(P.In(32)).Cone(V.R(40, 3), LaserReach));
        M(Lasers, b, 8, "Climb", Cue.On(P.In(32, AlpsEaseType.InQuad)).Move(V.R(-22, 8), 0));
        C(Lasers, b, 8, "Blue To White", Cue.On(P.In(32)).Gradient(Ink.Blue, Ink.Ice, Ink.White));

        B(Halo, b, 8, "Medium", Cue.On().Cone(12, 110));
        M(Halo, b, 8, "Lift", Cue.On(P.In(32)).Move(V.R(10, 55), 0));
        C(Halo, b, 8, "Blue To White", Cue.On(P.In(32)).Gradient(Ink.Blue, Ink.Ice, Ink.White));
        for (var i = 0; i < 4; i++)
        {
            var bar = b + i * 2;
            D(Halo, bar, 2, "Runner " + names[i], Cue.On(P.Wave(beats[i] * 2f, 0.05f, 0.2f, 0.3f).Spread(1)).Dim(V.R(5, 100)));
            D(TotemBars, bar, 2, names[i], Cue.On(P.Hit(beats[i], 0.3f, 0.3f)).Dim(V.R(0, 80)));
            D(Lasers, bar, 2, names[i], Cue.On(P.Hit(beats[i], 0.3f, 0.3f)).Dim(V.R(15, 100)));
            D(ApronBars, bar, 2, names[i], Cue.On(P.Hit(beats[i], 0.3f, 0.3f).Spread(1)).Sym().Dim(V.R(0, 80)));
            D(PixelBars, bar, 2, names[i], Cue.On(P.Hit(beats[i], 0.3f, 0.3f).Spread(1)).Sym().Dim(V.R(0, 100)));
            D(PenlightSweep, bar, 2, "Runner " + names[i], Cue.On(P.Wave(beats[i] * 2f, 0.1f, 0.2f, 0.4f).Spread(1)).Dim(V.R(10, 100)));
        }

        C(TotemBars, b, 8, "White", Ink.White);
        C(ApronBars, b, 8, "White", Ink.White);
        C(PixelBars, b, 8, "Blue To White", Cue.On(P.In(32)).Gradient(Ink.Blue, Ink.Ice, Ink.White));
        C(PenlightSweep, b, 8, "Blue To White", Cue.On(P.In(32)).Gradient(Ink.Blue, Ink.Ice, Ink.White));

        C(CycPars, b, 8, "Blue To White", Cue.On(P.In(32)).Gradient(Ink.DeepBlue, Ink.Ice, Ink.White));
        D(CycPars, b, 8, "Ramp", Cue.On(P.In(32, AlpsEaseType.InQuad)).Dim(V.R(10, 100)));

        B(FrontWash, b, 8, "Medium", Cue.On().Cone(30, 100));
        M(FrontWash, b, 8, "Lift Over The Crowd", Cue.On(P.In(32)).Sym().Move(V.R(45, 85), V.S(0, -35)));
        C(FrontWash, b, 8, "Blue To White", Cue.On(P.In(32)).Gradient(Ink.Blue, Ink.Ice, Ink.White));
        D(FrontWash, b, 8, "Ramp", Cue.On(P.In(32, AlpsEaseType.InQuad)).Dim(V.R(10, 70)));

        SideAcross(b, 8);
        C(SideWash, b, 8, "Blue", Ink.Blue);
        D(SideWash, b, 8, 30);

        C(Blinders, b + 6, 2, "Warm", Ink.Warm);
        D(Blinders, b + 6, 2, "Swell", Cue.On(P.In(8, AlpsEaseType.InExpo)).Dim(V.R(0, 100)));
    }

    // --- 65: Drop C -----------------------------------------------------------------------
    // Red and white, twice as fast, opened by a flash of the whole rig.
    private void DropC(float b)
    {
        Flash(b);

        C(Blinders, b, 8, "Warm", Ink.Warm);
        D(Blinders, b, 8, "Every Other Bar", Cue.On(P.Hit(8, 0.03f, 0.2f)).Dim(V.R(0, 100)));

        B(MidSpots, b, 8, "Beam", Cue.On().Cone(6, 130));
        M(MidSpots, b, 8, "Ellipse", Cue.On(P.Saw(2).Spread(1)).Circle(40, 0, 32, aspect: 1.6f));
        C(MidSpots, b, 8, "White", Ink.White);
        D(MidSpots, b, 8, 100);

        B(BackSpots, b, 8, "Beam", Cue.On().Cone(9, 120));
        M(BackSpots, b, 8, "Fly Out", Cue.On(P.Wave(2, 0.6f, 0f, 0.4f, AlpsEaseType.OutCubic).Spread(1)).Reverse()
            .Move(V.R(15, 88), 0, blackout: true));
        C(BackSpots, b, 8, "Red", Ink.Red);
        D(BackSpots, b, 8, 100);

        B(FloorSpots, b, 8, "Needle", Cue.On().Cone(5, 140));
        M(FloorSpots, b, 8, "Cross Fast", Cue.On(P.Wave(2)).Move(42, V.R(-60, 60)).Move(42, V.R(60, -60)));
        C(FloorSpots, b, 8, "White", Ink.White);
        D(FloorSpots, b, 8, 100);
        F(FloorSpots, b, 8, "Flicker", Cue.On().Flicker(16, 0.45f));

        TotemsUp(b, 8);
        C(TotemSpots, b, 8, "Red", Ink.Red);
        D(TotemSpots, b, 8, "Strobe", Strobe(0.5f));

        B(Halo, b, 8, "Medium", Cue.On().Cone(12, 110));
        M(Halo, b, 8, "Nod", Cue.On(P.Wave(4).Spread(0.5f)).Move(V.R(50, 15), 0));
        C(Halo, b, 8, "White / Red", Cue.On(P.Saw(1)).ColorSteps(Ink.White, Ink.Red));
        D(Halo, b, 8, "Random Strobe", Strobe(0.55f));

        SideAcross(b, 8);
        C(SideWash, b, 8, "Red", Ink.Red);
        D(SideWash, b, 8, 80);

        FrontOverCrowd(b, 8, 4);
        C(FrontWash, b, 8, "Red", Ink.Red);
        D(FrontWash, b, 8, 50);

        B(Lasers, b, 8, "Snap Open", Cue.On(P.Hit(1, 0.1f, 0.6f)).Cone(V.R(8, 42), LaserReach));
        M(Lasers, b, 8, "Rock", Cue.On(P.Wave(4).Spread(1)).Move(-12, V.R(-28, 28)));
        C(Lasers, b, 8, "White / Red", Cue.On(P.Saw(2)).ColorSteps(Ink.White, Ink.Red));
        D(Lasers, b, 8, 100);

        C(CycPars, b, 8, "Red / White", Cue.On(P.Saw(1).Spread(0.5f)).ColorSteps(Ink.Red, Ink.White));
        D(CycPars, b, 8, "Kick", Kick(35, 90));

        C(TotemBars, b, 8, "White", Ink.White);
        D(TotemBars, b, 8, "Sixteenths, By Chance", Cue.On(P.Hit(0.25f, 0.4f, 0.2f).Fire(0.5f)).Dim(V.R(0, 80)));

        C(ApronBars, b, 8, "Red", Ink.Red);
        D(ApronBars, b, 8, "Run Out", Cue.On(P.Hit(1, 0.2f, 0.3f).Spread(1)).Sym().Dim(V.R(0, 90)));

        C(PixelBars, b, 8, "White / Red", Cue.On(P.Saw(1)).ColorSteps(Ink.White, Ink.Red));
        D(PixelBars, b, 8, "Sparks", Cue.On(P.Hit(0.25f, 0.4f, 0.2f).Fire(0.4f)).Dim(V.R(0, 100)));

        C(PenlightSweep, b, 8, "Red / White", Cue.On(P.Saw(1)).ColorSteps(Ink.Red, Ink.White));
        D(PenlightSweep, b, 8, "Run Out", Cue.On(P.Wave(2, 0.1f, 0.2f, 0.4f).Spread(1)).Sym().Dim(V.R(0, 100)));
    }

    // --- 73 and 97: Rainbow ---------------------------------------------------------------
    // Every group scrolls the colour wheel at its own speed.
    private void Rainbow(float b)
    {
        Flash(b);

        C(Blinders, b, 8, "Warm", Ink.Warm);
        D(Blinders, b, 6, "Every Other Bar", Cue.On(P.Hit(8, 0.03f, 0.2f)).Dim(V.R(0, 100)));
        D(Blinders, b + 6, 2, "Every Bar", Cue.On(P.Hit(4, 0.03f, 0.25f)).Dim(V.R(0, 100)));

        B(MidSpots, b, 8, "Beam", Cue.On().Cone(8, 120));
        M(MidSpots, b, 8, "Loops", Cue.On(P.Wave(4).Spread(1)).Move(V.R(70, 20), V.R(-50, 50)));
        C(MidSpots, b, 8, "Rainbow", Cue.On(P.Saw(4).Spread(1)).Gradient(Ink.Rainbow));
        D(MidSpots, b, 8, 100);

        B(BackSpots, b, 8, "Gobo Spin", Cue.On().Cone(11, 120).Gobo(2, 36, false, 7));
        M(BackSpots, b, 8, "Big Circle", Cue.On(P.Saw(4).Spread(1)).Sym().Circle(50, 0, 34));
        C(BackSpots, b, 8, "Rainbow", Cue.On(P.Saw(8).Spread(1)).Gradient(Ink.Rainbow));
        D(BackSpots, b, 8, 100);

        B(FloorSpots, b, 8, "Needle", Cue.On().Cone(5, 140));
        M(FloorSpots, b, 8, "Fan Snaps", Cue.On(P.Wave(2, 0.5f, 0f, 0.5f, AlpsEaseType.OutBack)).Sym().Move(V.SR(0, 5, 0, 55), 90));
        C(FloorSpots, b, 8, "Rainbow", Cue.On(P.Saw(2).Spread(1)).Gradient(Ink.Rainbow));
        D(FloorSpots, b, 8, 100);

        B(TotemSpots, b, 8, "Needle", Cue.On().Cone(4, 140));
        M(TotemSpots, b, 8, "Fans", Cue.On(P.Wave(4).Spread(0.5f)).Sym().Move(V.R(35, 2), V.S(5, 35)));
        C(TotemSpots, b, 8, "Rainbow", Cue.On(P.Saw(4).Spread(1)).Gradient(Ink.Rainbow));
        D(TotemSpots, b, 8, 100);

        B(Halo, b, 8, "Medium", Cue.On().Cone(14, 110));
        M(Halo, b, 8, "Nod", Cue.On(P.Wave(2).Spread(1)).Move(V.R(15, 55), 0));
        C(Halo, b, 8, "Rainbow", Cue.On(P.Saw(4).Spread(1)).Gradient(Ink.Rainbow));
        D(Halo, b, 8, "Three Runners", Cue.On(P.Wave(1, 0.05f, 0.2f, 0.3f).Spread(3)).Dim(V.R(15, 100)));

        SideAcross(b, 8);
        C(SideWash, b, 8, "Rainbow", Cue.On(P.Saw(8).Spread(1)).Gradient(Ink.Rainbow));
        D(SideWash, b, 8, 80);

        FrontOverCrowd(b, 8, 4);
        C(FrontWash, b, 8, "Rainbow", Cue.On(P.Saw(8).Spread(1)).Gradient(Ink.Rainbow));
        D(FrontWash, b, 8, 55);

        // A fan is the same one a half turn on, so a pan that runs from -90 to 90 turns it for good.
        B(Lasers, b, 8, "Wide Fan", Cue.On().Cone(38, LaserReach));
        M(Lasers, b, 8, "Propellers", Cue.On(P.Saw(8).Spread(1)).Sym().Move(0, V.R(-90, 90)));
        C(Lasers, b, 8, "Rainbow", Cue.On(P.Saw(4).Spread(1)).Gradient(Ink.Rainbow));
        D(Lasers, b, 8, 100);

        C(CycPars, b, 8, "Rainbow", Cue.On(P.Saw(4).Spread(1)).Gradient(Ink.Rainbow));
        D(CycPars, b, 8, "Kick", Kick(40, 100));

        C(TotemBars, b, 8, "Rainbow", Cue.On(P.Saw(2).Spread(1)).Gradient(Ink.Rainbow));
        D(TotemBars, b, 8, "Kick", Kick(10, 80));

        C(ApronBars, b, 8, "Rainbow", Cue.On(P.Saw(2).Spread(1)).Gradient(Ink.Rainbow));
        D(ApronBars, b, 8, "Run", Cue.On(P.Hit(1, 0.2f, 0.3f).Spread(1)).Dim(V.R(10, 90)));

        C(PixelBars, b, 8, "Rainbow", Cue.On(P.Saw(2).Spread(1)).Gradient(Ink.Rainbow));
        D(PixelBars, b, 8, "Kick", Kick(30, 100));

        C(PenlightSweep, b, 8, "Rainbow", Cue.On(P.Saw(4).Spread(1)).Gradient(Ink.Rainbow));
        D(PenlightSweep, b, 8, "Wave", Cue.On(P.Wave(2).Spread(1)).Dim(V.R(30, 100)));
    }

    // --- 81: Bridge -----------------------------------------------------------------------
    // Half time: these clips carry their own tempo of 64 BPM, so their beats are twice as long.
    private void Bridge(float b)
    {
        const float half = Bpm / 2f;

        B(BackSpots, b, 8, "Beam", Cue.On().Cone(16, 120));
        M(BackSpots, b, 8, "Wall", Cue.On(P.Wave(4)).Tempo(half).Move(V.R(82, 44), 0));
        C(BackSpots, b, 8, "Blue", Ink.Blue);
        D(BackSpots, b, 8, 110);

        B(MidSpots, b, 8, "Beam", Cue.On().Cone(16, 120));
        M(MidSpots, b, 8, "Wall, Half A Cycle Late", Cue.On(P.Wave(4)).Tempo(half).Move(V.R(82, 44), 0, offset: 0.5f));
        C(MidSpots, b, 8, "Azure", Ink.Azure);
        D(MidSpots, b, 8, 110);

        B(FloorSpots, b, 8, "Thread", Cue.On().Cone(3, 160));
        M(FloorSpots, b, 8, "Straight Up", Cue.On().Move(0, 0));
        C(FloorSpots, b, 8, "White", Ink.White);
        D(FloorSpots, b, 8, "Slow Hits", Cue.On(P.Hit(2, 0.05f, 0.7f).Delay(0.25f)).Sym().Tempo(half).Dim(V.R(0, 100)));

        B(Halo, b, 8, "Pencil", Cue.On().Cone(8, 120));
        M(Halo, b, 8, "Straight Down", Cue.On().Move(0, 0));
        C(Halo, b, 8, "Deep Blue", Ink.DeepBlue);
        D(Halo, b, 8, 25);

        C(CycPars, b, 8, "Violet", Ink.Violet);
        D(CycPars, b, 8, 25);

        B(Lasers, b, 8, "Breathing Fan", Cue.On(P.Wave(8)).Tempo(half).Cone(V.R(26, 44), LaserReach));
        M(Lasers, b, 8, "Ceiling Comes Down", Cue.On(P.Wave(8).Delay(0.5f)).Sym().Tempo(half).Move(V.R(2, -24), 0));
        C(Lasers, b, 8, "Azure", Ink.Azure);
        D(Lasers, b, 8, "Glow", Cue.On().Dim(80, fadeIn: 4, fadeOut: 2));

        C(TotemBars, b, 8, "Blue", Ink.Blue);
        D(TotemBars, b, 8, "Slow Hits", Cue.On(P.Hit(2, 0.05f, 0.6f)).Tempo(half).Dim(V.R(0, 45)));

        C(ApronBars, b, 8, "Deep Blue", Ink.DeepBlue);
        D(ApronBars, b, 8, "Breathe", Cue.On(P.Wave(4).Spread(0.5f)).Sym().Tempo(half).Dim(V.R(3, 16)));

        C(PixelBars, b, 8, "Azure", Ink.Azure);
        D(PixelBars, b, 8, "Slow Comets", Cue.On(P.Wave(4, 0.08f, 0.02f, 0.5f).Spread(2)).Tempo(half).Dim(V.R(0, 70)));

        SideAcross(b, 8);
        C(SideWash, b, 8, "Deep Blue", Ink.DeepBlue);
        D(SideWash, b, 8, 40);

        C(PenlightRipple, b, 8, "Blue", Ink.Blue);
        D(PenlightRipple, b, 8, "Slow Ripple", Cue.On(P.Wave(4, 0.3f, 0.1f, 0.5f).Spread(1)).Tempo(half).Dim(V.R(0, 70)));
    }

    // --- 89: what the last Drop A adds ------------------------------------------------------
    private void FinaleExtras(float b)
    {
        Flash(b);
        F(MidSpots, b, 8, "Flicker", Cue.On().Flicker(18, 0.4f));
        F(BackSpots, b, 8, "Flicker", Cue.On().Flicker(14, 0.35f));
        C(PenlightRipple, b, 8, "White", Ink.White);
        D(PenlightRipple, b, 8, "Fast Ripple", Cue.On(P.Hit(2, 0.1f, 0.5f).Spread(1)).Dim(V.R(0, 70)));
    }

    // --- 105: Outro -----------------------------------------------------------------------
    // One last hit, then the beams gather on the middle of the deck and everything fades.
    private void Outro(float b)
    {
        Flash(b);
        C(Blinders, b, 2, "Warm", Ink.Warm);
        D(Blinders, b, 2, "Last Hit", Cue.On(P.Out(8, AlpsEaseType.OutQuad)).Dim(V.R(0, 100)));

        B(MidSpots, b, 6, "Tighten", Cue.On(P.In(16)).Cone(V.R(14, 4), 120));
        M(MidSpots, b, 6, "Gather", Cue.On(P.In(16)).Sym().Move(V.R(50, 8), V.S(0, 40)));
        C(MidSpots, b, 6, "Ice", Ink.Ice);
        D(MidSpots, b, 6, "Fade", Cue.On().Dim(70, fadeOut: 12));

        B(BackSpots, b, 6, "Tighten", Cue.On(P.In(16)).Cone(V.R(14, 4), 120));
        M(BackSpots, b, 6, "Gather", Cue.On(P.In(16)).Sym().Move(V.R(60, 15), V.S(0, 50)));
        C(BackSpots, b, 6, "Blue", Ink.Blue);
        D(BackSpots, b, 6, "Fade", Cue.On().Dim(70, fadeOut: 12));

        B(FloorSpots, b, 4, "Thread", Cue.On().Cone(4, 150));
        M(FloorSpots, b, 4, "Straight Up", Cue.On().Move(0, 0));
        C(FloorSpots, b, 4, "Blue", Ink.Blue);
        D(FloorSpots, b, 4, "Fade", Cue.On().Dim(60, fadeOut: 8));

        B(Halo, b, 8, "Pencil", Cue.On().Cone(8, 120));
        M(Halo, b, 8, "Straight Down", Cue.On().Move(0, 0));
        C(Halo, b, 8, "Deep Blue", Ink.DeepBlue);
        D(Halo, b, 8, "Slow Chase, Fading", Cue.On(P.Wave(8, 0.3f, 0.1f, 0.4f).Spread(1)).Dim(V.R(10, 50), fadeOut: 16));

        B(Lasers, b, 6, "Close Up", Cue.On(P.In(20)).Cone(V.R(40, 2), LaserReach));
        M(Lasers, b, 6, "Lift Away", Cue.On(P.In(20)).Move(V.R(-16, 12), 0));
        C(Lasers, b, 6, "Blue", Ink.Blue);
        D(Lasers, b, 6, "Fade", Cue.On().Dim(70, fadeOut: 12));

        C(CycPars, b, 8, "Violet To Deep Blue", Cue.On(P.In(24)).Color(Ink.Violet, Ink.DeepBlue));
        D(CycPars, b, 8, "Fade", Cue.On().Dim(35, fadeOut: 24));

        C(TotemBars, b, 6, "Warm", Ink.Warm);
        D(TotemBars, b, 6, "Twinkle, Fading", Cue.On(P.Wave(0.5f, 0.2f, 0.1f, 0.6f).Fire(0.12f)).Dim(V.R(0, 60), fadeOut: 8));

        C(ApronBars, b, 6, "Deep Blue", Ink.DeepBlue);
        D(ApronBars, b, 6, "Breathe, Fading", Cue.On(P.Wave(8).Spread(0.5f)).Sym().Dim(V.R(3, 14), fadeOut: 12));

        C(PixelBars, b, 6, "Blue", Ink.Blue);
        D(PixelBars, b, 6, "Comet, Fading", Cue.On(P.Wave(8, 0.08f, 0.02f, 0.5f).Spread(1)).Dim(V.R(0, 60), fadeOut: 12));

        FrontOnStage(b, 4);
        C(FrontWash, b, 4, "Warm", Ink.Warm);
        D(FrontWash, b, 4, "Fade", Cue.On().Dim(30, fadeOut: 12));

        C(PenlightRipple, b, 8, "Blue", Ink.Blue);
        D(PenlightRipple, b, 8, "Ripple, Fading", Cue.On(P.Wave(8, 0.3f, 0.1f, 0.5f).Spread(1)).Dim(V.R(0, 60), fadeOut: 16));
    }
}
