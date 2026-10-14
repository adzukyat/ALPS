using System.Collections.Generic;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VRSL;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// The gobo's projection switch, which VRSL cannot take from DMX: how it composes on the
    /// CPU (<see cref="AlpsProjection"/>) the way the evaluator composes the gobo, and the
    /// Animation Tracks a build keys it into (<see cref="AlpsProjectionTracks"/>).
    /// </summary>
    public class AlpsProjectionTests
    {
        private const float Bpm = 60f;
        private const string Folder = "Assets/AlpsTestFixtures";
        private const string TimelinePath = Folder + "/ProjectionTracks.playable";
        private const string MoverPrefab = "Packages/com.acchosen.vr-stage-lighting/Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Mover-Spotlight-H-13CH.prefab";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            foreach (var created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
            AssetDatabase.DeleteAsset(TimelinePath);
        }

        [Test]
        public void OffClip_HidesOnlyWhileItPlays()
        {
            var show = AlpsShowCompiler.CompileStandalone(1, Bpm, new AlpsStandaloneClip { set = Gobo(false), start = 1f, end = 3f });

            Assert.IsTrue(AlpsProjection.IsLit(show, 0, 0.5f));
            Assert.IsFalse(AlpsProjection.IsLit(show, 0, 2f));
            Assert.IsTrue(AlpsProjection.IsLit(show, 0, 3.5f));

            var switches = AlpsProjection.Switches(show, 0, out var litAtStart);
            Assert.IsTrue(litAtStart);
            CollectionAssert.AreEqual(new[] { 1f, 3f }, switches);
        }

        [Test]
        public void UpperLayerGobo_ShowsItAgain()
        {
            var show = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = Gobo(false), start = 0f, end = 4f, layer = 0 },
                new AlpsStandaloneClip { set = Gobo(true), start = 1f, end = 2f, layer = 1 });

            var switches = AlpsProjection.Switches(show, 0, out var litAtStart);
            Assert.IsFalse(litAtStart);
            CollectionAssert.AreEqual(new[] { 1f, 2f, 4f }, switches);
        }

        [Test]
        public void UpperLayerWithoutGobo_LeavesItOff()
        {
            var brightness = new AlpsClipEffectSet();
            brightness.Add(AlpsEffectKind.Brightness);
            var show = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = Gobo(false), start = 0f, end = 4f, layer = 0 },
                new AlpsStandaloneClip { set = brightness, start = 1f, end = 2f, layer = 1 });

            Assert.IsFalse(AlpsProjection.IsLit(show, 0, 1.5f));
        }

        [Test]
        public void OddEffect_HidesOnlyOddFixtures()
        {
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Gobo);
            set.Add(AlpsEffectKind.Gobo).goboProjectionOff = true;
            var show = AlpsShowCompiler.CompileStandalone(4, Bpm, new AlpsStandaloneClip { set = set, end = 4f });

            // Odd and even count from 1, so the first fixture is odd.
            Assert.IsFalse(AlpsProjection.IsLit(show, 0, 1f));
            Assert.IsTrue(AlpsProjection.IsLit(show, 1, 1f));
            Assert.IsFalse(AlpsProjection.IsLit(show, 2, 1f));
            Assert.IsTrue(AlpsProjection.IsLit(show, 3, 1f));
        }

        [Test]
        public void Crossfade_SwitchesWhereTheIncomingClipOutweighs()
        {
            var show = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = Gobo(false), start = 0f, end = 4f, mixOut = 2f },
                new AlpsStandaloneClip { set = Gobo(true), start = 2f, end = 6f, mixIn = 2f });

            var switches = AlpsProjection.Switches(show, 0, out var litAtStart);
            Assert.IsFalse(litAtStart);
            Assert.AreEqual(1, switches.Count, string.Join(", ", switches));
            Assert.AreEqual(3f, switches[0], AlpsProjection.MixSampleSeconds);
        }

        [Test]
        public void ShowWithoutOffGobo_AddsNoTracks()
        {
            var show = AlpsShowCompiler.CompileStandalone(1, Bpm, new AlpsStandaloneClip { set = Gobo(true), end = 4f });
            var timeline = CreateTimeline();

            AlpsProjectionTracks.Write(timeline, show);

            foreach (var track in timeline.GetOutputTracks())
            {
                Assert.IsNotInstanceOf<AnimationTrack>(track);
            }
        }

        [Test]
        public void VrslFixture_FindsItsProjectionAndHidesItInThePreview()
        {
            var fixture = CreateFixture(CreateRig(), "Mover");
            var projection = Projection(fixture);
            Assert.AreEqual(1, projection.Count, "The stock mover has one projection renderer.");
            StringAssert.EndsWith("/Projection", projection[0].sharedMaterial.shader.name);

            var frame = new float[AlpsShowLayout.FrameStride];
            fixture.CaptureDefault(frame, 0);
            fixture.SetPreviewProjection(false);
            Assert.IsFalse(projection[0].enabled);

            fixture.RestoreAuthored(frame, 0);
            Assert.IsTrue(projection[0].enabled);
        }

        [Test]
        public void Build_KeysTheSwitchesAndBindsAnAnimatorAboveTheFixtures()
        {
            var show = AlpsShowCompiler.CompileStandalone(2, Bpm, new AlpsStandaloneClip { set = Gobo(false), start = 1f, end = 3f });
            var rig = CreateRig();
            var first = CreateFixture(rig, "Left");
            var second = CreateFixture(rig, "Right");
            show.fixtures.Add(first);
            show.fixtures.Add(second);
            var timeline = CreateTimeline();

            AlpsProjectionTracks.Write(timeline, show);

            AnimationTrack projectionTrack = null;
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track.name == AlpsProjectionTracks.TrackName(0))
                {
                    projectionTrack = track as AnimationTrack;
                }
            }

            Assert.NotNull(projectionTrack, "The build timeline has no projection track.");
            var clip = ((AnimationPlayableAsset)new List<TimelineClip>(projectionTrack.GetClips())[0].asset).clip;
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.AreEqual(2, bindings.Length);
            foreach (var binding in bindings)
            {
                Assert.AreEqual("m_Enabled", binding.propertyName);
                Assert.AreEqual(typeof(MeshRenderer), binding.type);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                Assert.AreEqual(1f, curve.Evaluate(0.5f));
                Assert.AreEqual(0f, curve.Evaluate(2f));
                Assert.AreEqual(1f, curve.Evaluate(3.5f));
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    AnimationUtility.CalculateTransformPath(Projection(first)[0].transform, rig.transform),
                    AnimationUtility.CalculateTransformPath(Projection(second)[0].transform, rig.transform),
                },
                new[] { bindings[0].path, bindings[1].path });

            var director = new GameObject("Director").AddComponent<PlayableDirector>();
            _created.Add(director.gameObject);
            director.playableAsset = timeline;
            var errors = new List<string>();
            AlpsProjectionTracks.Bind(director, timeline, show, errors);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            var animator = rig.GetComponent<Animator>();
            Assert.NotNull(animator, "The rig above both fixtures should carry the Animator.");
            Assert.AreSame(animator, director.GetGenericBinding(projectionTrack));
        }

        private static AlpsClipEffectSet Gobo(bool projection)
        {
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Gobo).goboProjectionOff = !projection;
            return set;
        }

        private GameObject CreateRig()
        {
            var rig = new GameObject("Rig");
            _created.Add(rig);
            return rig;
        }

        private static AlpsVRSLFixture CreateFixture(GameObject rig, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoverPrefab);
            Assert.NotNull(prefab, MoverPrefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.SetParent(rig.transform, false);
            var fixture = instance.AddComponent<AlpsVRSLFixture>();
            fixture.target = instance.GetComponentInChildren<VRStageLighting_DMX_Static>();
            return fixture;
        }

        private static List<Renderer> Projection(AlpsFixture fixture)
        {
            var renderers = new List<Renderer>();
            fixture.CollectProjectionRenderers(renderers);
            return renderers;
        }

        private static TimelineAsset CreateTimeline()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", Folder.Substring("Assets/".Length));
            }

            AssetDatabase.DeleteAsset(TimelinePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 8.0;
            return timeline;
        }
    }
}
