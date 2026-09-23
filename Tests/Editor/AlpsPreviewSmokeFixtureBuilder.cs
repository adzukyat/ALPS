using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VRSL;
using Object = UnityEngine.Object;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Builds the committed PreviewSmoke scene and timeline: two VRSL fixtures in a group,
    /// a base ALPS track, an accent ALPS track layered above it, and the activation and
    /// animation tracks a build must keep.
    /// </summary>
    internal static class AlpsPreviewSmokeFixtureBuilder
    {
        public const string FolderPath = "Assets/AlpsTestFixtures";
        public const string ScenePath = FolderPath + "/PreviewSmoke.unity";
        public const string TimelinePath = FolderPath + "/PreviewSmoke.playable";

        public const float Bpm = 60f;
        public const float BaseTime = 1f;
        public const float AccentTime = 1.75f;

        public const float BaseBrightness = 70f;
        public const float AccentBrightness = 30f;
        public const float Pan = 30f;
        public const float PanSpread = 10f;
        public const float Tilt = 60f;
        public const float ConeWidth = 45f;
        public const float ConeLength = 25f;
        public const int Gobo = 4;
        public static readonly Color Tint = new Color(0.25f, 0.5f, 1f, 1f);

        public const float ExpectedVrslConeWidth = 2.75f;
        public const float ExpectedVrslConeLength = 5.25f;

        public const double ActivationStart = 0.25;
        public const double ActivationDuration = 1.25;
        public const double AnimationStart = 0.5;
        public const double AnimationDuration = 1.0;

        [MenuItem("ALPS/Tests/Regenerate Preview Smoke Fixture")]
        public static void RegenerateAssets()
        {
            EnsureFolder(FolderPath);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(TimelinePath);

            CreateTimelineAsset();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Opening a new scene can swap the in-memory timeline for a freshly loaded one,
            // so bind against the asset as it is on disk.
            CreateSceneObjects(AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        public static Context OpenFreshScene()
        {
            if (!HasCompleteAssets())
            {
                RegenerateAssets();
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return new Context();
        }

        private static bool HasCompleteAssets()
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            return timeline != null &&
                File.Exists(ScenePath) &&
                timeline.GetRootTracks().OfType<AlpsTimelineTrack>().Count() == 2 &&
                timeline.GetRootTracks().OfType<ActivationTrack>().Any() &&
                timeline.GetRootTracks().OfType<AnimationTrack>().Any();
        }

        private static TimelineAsset CreateTimelineAsset()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "PreviewSmoke";
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 2.0;

            var baseTrack = timeline.CreateTrack<AlpsTimelineTrack>(null, "Base");
            baseTrack.bpm = Bpm;
            var baseClip = baseTrack.CreateClip<AlpsTimelineClip>();
            baseClip.displayName = "Base Cue";
            baseClip.start = 0.0;
            baseClip.duration = 2.0;
            ((AlpsTimelineClip)baseClip.asset).data = BuildBaseSet();

            var accentTrack = timeline.CreateTrack<AlpsTimelineTrack>(null, "Accent");
            accentTrack.bpm = Bpm;
            var accentClip = accentTrack.CreateClip<AlpsTimelineClip>();
            accentClip.displayName = "Accent Cue";
            accentClip.start = 1.5;
            accentClip.duration = 0.5;
            var accent = new AlpsClipEffectSet();
            accent.Add(AlpsEffectKind.Brightness).brightness.value = AccentBrightness;
            ((AlpsTimelineClip)accentClip.asset).data = accent;

            var activationTrack = timeline.CreateTrack<ActivationTrack>(null, "Activation Retained");
            var activationClip = activationTrack.CreateDefaultClip();
            activationClip.start = ActivationStart;
            activationClip.duration = ActivationDuration;

            var animationTrack = timeline.CreateTrack<AnimationTrack>(null, "Animation Retained");
            var retainedAnimation = new AnimationClip { name = "PreviewSmoke Retained AnimationClip", frameRate = 30f };
            AnimationUtility.SetEditorCurve(
                retainedAnimation,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Linear(0f, 0f, 1f, 1f));
            AssetDatabase.AddObjectToAsset(retainedAnimation, timeline);
            var animationClip = animationTrack.CreateClip(retainedAnimation);
            animationClip.start = AnimationStart;
            animationClip.duration = AnimationDuration;

            EditorUtility.SetDirty(timeline);
            return timeline;
        }

        private static AlpsClipEffectSet BuildBaseSet()
        {
            var set = new AlpsClipEffectSet();

            var move = set.Add(AlpsEffectKind.Move);
            move.pan.value = Pan;
            move.pan.hasSpread = true;
            // The group holds two fixtures, so the last one sits a single step on.
            move.pan.spreadRange = new Vector2(Pan, Pan + PanSpread);
            move.pan.spreadRangeEnd = move.pan.spreadRange;
            move.tilt.value = Tilt;

            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.value = ConeWidth;
            cone.coneLength.value = ConeLength;

            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Tint));

            set.Add(AlpsEffectKind.Brightness).brightness.value = BaseBrightness;

            var gobo = set.Add(AlpsEffectKind.Gobo);
            gobo.goboStops.Add(new AlpsGoboStop(Gobo));
            gobo.goboRotationBeats = 0f;
            gobo.goboFixtureStaggerDegrees = 0f;
            return set;
        }

        private static void CreateSceneObjects(TimelineAsset timeline)
        {
            var directorObject = new GameObject("PreviewSmoke Director");
            var director = directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.extrapolationMode = DirectorWrapMode.Hold;

            var groupObject = new GameObject("PreviewSmoke Fixtures");
            var group = groupObject.AddComponent<AlpsFixtureGroup>();
            for (var i = 0; i < 2; i++)
            {
                var fixtureObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fixtureObject.name = "Fixture " + (i + 1);
                fixtureObject.transform.SetParent(groupObject.transform, false);
                var vrslFixture = fixtureObject.AddComponent<VRStageLighting_DMX_Static>();
                vrslFixture.objRenderers = new[] { fixtureObject.GetComponent<MeshRenderer>() };
                vrslFixture.enableDMXChannels = true;
                vrslFixture.enableStrobe = true;
                vrslFixture.globalIntensity = 0f;
                vrslFixture.lightColorTint = Color.black;
                vrslFixture.coneWidth = 0f;
                vrslFixture.coneLength = 0.5f;
                vrslFixture.selectGOBO = 1;
                var alpsFixture = fixtureObject.AddComponent<AlpsVRSLFixture>();
                alpsFixture.target = vrslFixture;
                group.fixtures.Add(alpsFixture);
            }

            foreach (var track in timeline.GetRootTracks())
            {
                if (track is AlpsTimelineTrack)
                {
                    director.SetGenericBinding(track, group);
                }
                else if (track is ActivationTrack)
                {
                    director.SetGenericBinding(track, new GameObject("Retained Activation Target"));
                }
                else if (track is AnimationTrack)
                {
                    director.SetGenericBinding(track, new GameObject("Retained Animation Target").AddComponent<Animator>());
                }
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            }
        }

        /// <summary>The objects of the open PreviewSmoke scene.</summary>
        internal sealed class Context
        {
            public readonly PlayableDirector Director;
            public readonly TimelineAsset Timeline;
            public readonly AlpsFixtureGroup Group;
            public readonly VRStageLighting_DMX_Static[] Fixtures;

            public Context()
            {
                Director = Object.FindObjectOfType<PlayableDirector>();
                Timeline = Director != null ? Director.playableAsset as TimelineAsset : null;
                Group = Object.FindObjectOfType<AlpsFixtureGroup>();
                Fixtures = Group != null
                    ? Group.fixtures.Select(f => ((AlpsVRSLFixture)f).target).ToArray()
                    : Array.Empty<VRStageLighting_DMX_Static>();
            }

            public FixtureState[] Sample(float time)
            {
                Director.time = time;
                Director.Evaluate();
                return Fixtures.Select(FixtureState.Capture).ToArray();
            }
        }

        internal readonly struct FixtureState
        {
            public readonly bool EnableDmx;
            public readonly float Pan;
            public readonly float Tilt;
            public readonly float Intensity;
            public readonly Color Color;
            public readonly float ConeWidth;
            public readonly float ConeLength;
            public readonly int Gobo;

            private FixtureState(VRStageLighting_DMX_Static fixture)
            {
                EnableDmx = fixture.enableDMXChannels;
                Pan = fixture.panOffsetBlueGreen;
                Tilt = fixture.tiltOffsetBlue;
                Intensity = fixture.globalIntensity;
                Color = fixture.lightColorTint;
                ConeWidth = fixture.coneWidth;
                ConeLength = fixture.coneLength;
                Gobo = fixture.selectGOBO;
            }

            public static FixtureState Capture(VRStageLighting_DMX_Static fixture)
            {
                return new FixtureState(fixture);
            }

            public override string ToString()
            {
                return $"dmx={EnableDmx}, pan={Pan:0.###}, tilt={Tilt:0.###}, intensity={Intensity:0.###}, color={Color}, coneWidth={ConeWidth:0.###}, coneLength={ConeLength:0.###}, gobo={Gobo}";
            }
        }
    }
}
