using System.Collections.Generic;
using AdzukiSoft.ALPS;
using AdzukiSoft.ALPS.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VRSL;

/// <summary>
/// Rebuilds the demo: fixture groups on the scene's VRSL universes, an example timeline
/// with one ALPS track per group, and the show player. Safe to run again at any time.
/// </summary>
public static class AlpsDemoBuilder
{
    private const string ScenePath = "Assets/Scenes/VRCDefaultWorldScene.unity";
    private const string TimelinePath = "Assets/ExampleTimeline.playable";
    private const string GroupPrefix = "Universe_";
    private const float Bpm = 120f;
    private const double Duration = 26.0;

    [MenuItem("ALPS/Demo/Rebuild Example")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RemoveLegacyObjects();

        var groups = BuildGroups(scene);
        var director = Object.FindObjectOfType<PlayableDirector>();
        if (director == null)
        {
            Debug.LogError("[ALPS Demo] The demo scene has no PlayableDirector.");
            return;
        }

        var timeline = BuildTimeline();
        director.playableAsset = timeline;
        foreach (var track in timeline.GetRootTracks())
        {
            if (track is AlpsTimelineTrack && groups.TryGetValue(track.name, out var group))
            {
                director.SetGenericBinding(track, group);
            }
        }

        AlpsShowSetup.GetOrCreatePlayer(director);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[ALPS Demo] Rebuilt the example show.");
    }

    /// <summary>Batch mode entry point.</summary>
    public static void RebuildAndExit()
    {
        var failed = false;
        void OnLog(string condition, string stackTrace, LogType type)
        {
            failed |= type == LogType.Error || type == LogType.Exception;
        }

        Application.logMessageReceived += OnLog;
        try
        {
            Rebuild();
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
        }

        EditorApplication.Exit(failed ? 1 : 0);
    }

    private static void RemoveLegacyObjects()
    {
        foreach (var transform in Object.FindObjectsOfType<Transform>(true))
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
        }

        const string legacySettingsPath = "Assets/StageLightManeuverSettings.asset";
        if (AssetDatabase.LoadAssetAtPath<Object>(legacySettingsPath) != null)
        {
            AssetDatabase.DeleteAsset(legacySettingsPath);
        }
    }

    private static Dictionary<string, AlpsFixtureGroup> BuildGroups(UnityEngine.SceneManagement.Scene scene)
    {
        var groups = new Dictionary<string, AlpsFixtureGroup>();
        foreach (var transform in Object.FindObjectsOfType<Transform>(true))
        {
            if (!transform.name.StartsWith(GroupPrefix))
            {
                continue;
            }

            var group = transform.GetComponent<AlpsFixtureGroup>();
            if (group == null)
            {
                group = transform.gameObject.AddComponent<AlpsFixtureGroup>();
            }

            group.fixtures.Clear();
            foreach (var vrsl in transform.GetComponentsInChildren<VRStageLighting_DMX_Static>(true))
            {
                var fixture = vrsl.GetComponent<AlpsVRSLFixture>();
                if (fixture == null)
                {
                    fixture = vrsl.gameObject.AddComponent<AlpsVRSLFixture>();
                }

                fixture.target = vrsl;
                group.fixtures.Add(fixture);
            }

            EditorUtility.SetDirty(group);
            groups[transform.name] = group;
        }

        return groups;
    }

    private static TimelineAsset BuildTimeline()
    {
        AssetDatabase.DeleteAsset(TimelinePath);
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        timeline.name = "ExampleTimeline";
        AssetDatabase.CreateAsset(timeline, TimelinePath);
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = Duration;

        var wash = Track(timeline, GroupPrefix + "MovingWashLight");
        Clip(wash, "Wash Sweep", 0, 8.5, WashSweep());
        Clip(wash, "Wash Chase", 8, 8.5, WashChase());
        Clip(wash, "Wash Odd Even", 16, 10, WashOddEven());

        var beam = Track(timeline, GroupPrefix + "MovingBeamLight");
        Clip(beam, "Beam Circle", 0, 13.5, BeamCircle());
        Clip(beam, "Beam Pulse", 13, 13, BeamPulse());

        var gobo = Track(timeline, GroupPrefix + "MovingBeamLight_Gobo");
        Clip(gobo, "Gobo Spin", 0, Duration, GoboSpin());

        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
    }

    private static AlpsTimelineTrack Track(TimelineAsset timeline, string name)
    {
        var track = timeline.CreateTrack<AlpsTimelineTrack>(null, name);
        track.bpm = Bpm;
        return track;
    }

    private static void Clip(AlpsTimelineTrack track, string name, double start, double duration, AlpsClipEffectSet set)
    {
        var clip = track.CreateClip<AlpsTimelineClip>();
        clip.displayName = name;
        clip.start = start;
        clip.duration = duration;
        ((AlpsTimelineClip)clip.asset).data = set;
    }

    // ------------------------------------------------------------------ cues

    private static AlpsClipEffectSet WashSweep()
    {
        var set = Set(AlpsPhaseMode.PingPong, AlpsEaseType.InOutSine, 4f);
        set.order = AlpsOrderMode.Symmetric;
        set.phase.spread = 0.5f;

        var move = set.Add(AlpsEffectKind.Move);
        move.tilt.isRange = true;
        move.tilt.range = new Vector2(-30f, 30f);
        move.pan.value = 0f;

        var color = set.Add(AlpsEffectKind.Color);
        color.colorStops.Add(new AlpsColorStop(new Color(1f, 0.1f, 0.2f)));
        color.colorStops.Add(new AlpsColorStop(new Color(0.9f, 0.2f, 1f)));
        color.colorStops.Add(new AlpsColorStop(new Color(0.2f, 0.3f, 1f)));
        color.colorPhasing.timing = AlpsTimingMode.PerCycle;

        set.Add(AlpsEffectKind.Brightness).brightness.value = 100f;
        return set;
    }

    private static AlpsClipEffectSet WashChase()
    {
        var set = Set(AlpsPhaseMode.Forward, AlpsEaseType.OutQuad, 2f);
        set.phase.spread = 1f;

        var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
        brightness.isRange = true;
        brightness.range = new Vector2(0f, 100f);

        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(0.1f, 0.9f, 1f), 0f), new GradientColorKey(new Color(0.6f, 0.1f, 1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop { isGradient = true, gradient = gradient });
        return set;
    }

    private static AlpsClipEffectSet WashOddEven()
    {
        var set = Set(AlpsPhaseMode.PingPong, AlpsEaseType.InOutCubic, 8f);

        set.Add(AlpsEffectKind.Move).tilt.value = -25f;
        set.Add(AlpsEffectKind.Move).tilt.value = 25f;

        set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
        set.Add(AlpsEffectKind.Brightness).brightness.value = 100f;

        var flicker = set.Add(AlpsEffectKind.Flicker);
        flicker.flickerSpeed = 8f;
        flicker.flickerStrength = 0.35f;
        return set;
    }

    private static AlpsClipEffectSet BeamCircle()
    {
        // Forward and linear, so the ring turns at a steady speed. A full spread walks the
        // fixtures right around it, one per position.
        var set = Set(AlpsPhaseMode.Forward, AlpsEaseType.Linear, 4f);
        set.phase.spread = 1f;

        var move = set.Add(AlpsEffectKind.Move);
        move.moveMode = AlpsMoveMode.Circle;
        move.circleCenterTilt.value = 30f;
        move.circleRadius.value = 12f;

        var cone = set.Add(AlpsEffectKind.Cone);
        cone.coneWidth.value = 12f;
        cone.coneLength.value = 40f;

        set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
        set.Add(AlpsEffectKind.Brightness).brightness.value = 100f;
        return set;
    }

    private static AlpsClipEffectSet BeamPulse()
    {
        var set = Set(AlpsPhaseMode.Forward, AlpsEaseType.Linear, 0.5f);

        var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
        brightness.isRange = true;
        brightness.range = new Vector2(0f, 100f);
        brightness.timing = AlpsTimingMode.PerCycle;

        var color = set.Add(AlpsEffectKind.Color);
        color.colorStops.Add(new AlpsColorStop(new Color(1f, 0.85f, 0.1f)));
        color.colorStops.Add(new AlpsColorStop(new Color(1f, 0.45f, 0.05f)));
        color.colorPhasing.timing = AlpsTimingMode.PerCycle;
        color.colorPhasing.useOwnPhase = true;
        color.colorPhasing.ownPhase.beatsPerCycle = 2f;
        return set;
    }

    private static AlpsClipEffectSet GoboSpin()
    {
        var set = Set(AlpsPhaseMode.Forward, AlpsEaseType.Linear, 4f);

        var gobo = set.Add(AlpsEffectKind.Gobo);
        gobo.goboStops.Add(new AlpsGoboStop(4));
        gobo.goboStops.Add(new AlpsGoboStop(7));
        gobo.goboStops.Add(new AlpsGoboStop());
        gobo.goboPhasing.timing = AlpsTimingMode.PerCycle;
        gobo.goboRotationBeats = 2f;
        gobo.goboFixtureStaggerDegrees = 45f;

        var cone = set.Add(AlpsEffectKind.Cone);
        cone.coneWidth.value = 20f;
        cone.coneLength.value = 45f;

        set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(new Color(0.3f, 0.5f, 1f)));
        set.Add(AlpsEffectKind.Brightness).brightness.value = 80f;
        return set;
    }

    private static AlpsClipEffectSet Set(AlpsPhaseMode mode, AlpsEaseType ease, float beatsPerCycle)
    {
        var set = new AlpsClipEffectSet();
        set.phase.mode = mode;
        set.phase.ease = ease;
        set.phase.beatsPerCycle = beatsPerCycle;
        return set;
    }
}
