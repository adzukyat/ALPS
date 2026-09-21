using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Timeline;
using static AdzukiSoft.ALPS.Tests.AlpsPreviewSmokeFixtureBuilder;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Level 3 evaluates the real PreviewSmoke timeline in edit mode. Level 4 applies the
    /// show the way a build does and checks the Udon player reproduces the preview.
    /// </summary>
    public class AlpsPreviewSmokeTests
    {
        private const float Tolerance = 0.0005f;

        [TearDown]
        public void CloseScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void Level3_PreviewDrivesTheVrslFixtures()
        {
            var context = OpenFreshScene();
            var states = context.Sample(BaseTime);
            var diagnostics = string.Join("\n", states.Select(s => s.ToString()));

            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i];
                Assert.IsFalse(state.EnableDmx, diagnostics);
                Assert.AreEqual(-(Pan + PanSpread * i), state.Pan, Tolerance, "Pan spread adds per fixture.\n" + diagnostics);
                Assert.AreEqual(Tilt + AlpsShowPlayer.VrslDefaultTiltOffset, state.Tilt, Tolerance, diagnostics);
                Assert.AreEqual(BaseBrightness / 100f, state.Intensity, Tolerance, diagnostics);
                Assert.AreEqual(Tint.r, state.Color.r, Tolerance, diagnostics);
                Assert.AreEqual(Tint.g, state.Color.g, Tolerance, diagnostics);
                Assert.AreEqual(Tint.b, state.Color.b, Tolerance, diagnostics);
                Assert.AreEqual(ExpectedVrslConeWidth, state.ConeWidth, Tolerance, diagnostics);
                Assert.AreEqual(ExpectedVrslConeLength, state.ConeLength, Tolerance, diagnostics);
                Assert.AreEqual(Gobo, state.Gobo, diagnostics);
            }
        }

        [Test]
        public void Level3_LaterTrackOverridesOnlyItsOwnChannels()
        {
            var context = OpenFreshScene();
            var states = context.Sample(AccentTime);

            Assert.AreEqual(AccentBrightness / 100f, states[0].Intensity, Tolerance, "The accent track sets brightness.");
            Assert.AreEqual(Tint.b, states[0].Color.b, Tolerance, "Color still comes from the base track.");
        }

        [Test]
        public void Level3_ClipEditsReachThePreviewWithoutRebuildingTheGraph()
        {
            var context = OpenFreshScene();
            context.Sample(BaseTime);

            var clip = (AlpsTimelineClip)context.Timeline.GetRootTracks().OfType<AlpsTimelineTrack>().First().GetClips().First().asset;
            var brightness = clip.data.effects.First(e => e.kind == AlpsEffectKind.Brightness);
            brightness.brightness.value = 12f;
            AlpsPreviewDriver.MarkDirty();

            var states = context.Sample(BaseTime);
            Assert.AreEqual(0.12f, states[0].Intensity, Tolerance);
        }

        [Test]
        public void Level3_StoppingTheTimelinePreviewRevertsTheFixtures()
        {
            var context = OpenFreshScene();
            var authored = context.Fixtures.Select(FixtureState.Capture).ToArray();

            // Stand in for the Timeline window: gather the track properties inside animation mode.
            var driver = ScriptableObject.CreateInstance<AnimationModeDriver>();
            var collector = new AnimationModeCollector();
            AnimationMode.StartAnimationMode(driver);
            try
            {
                foreach (var track in context.Timeline.GetOutputTracks().OfType<AlpsTimelineTrack>())
                {
                    track.GatherProperties(context.Director, collector);
                }

                foreach (var fixture in context.Fixtures)
                {
                    Assert.That(collector.Registered, Has.Some.EqualTo(((Component)fixture, "enableDMXChannels")));
                    Assert.That(collector.Registered, Has.Some.EqualTo(((Component)fixture, "lightColorTint.b")));
                }

                Assert.IsFalse(context.Sample(BaseTime)[0].EnableDmx, "The preview drives the fixtures.");
            }
            finally
            {
                AnimationMode.StopAnimationMode(driver);
                Object.DestroyImmediate(driver);
            }

            AlpsPreviewDriver.EndPreviewSession();
            AssertRestored(context, authored);
        }

        [Test]
        public void Level3_RebuiltGraphKeepsTheAuthoredDefaults()
        {
            var context = OpenFreshScene();
            var authored = context.Fixtures.Select(FixtureState.Capture).ToArray();
            context.Sample(BaseTime);

            // Move the base clip away and rebuild, like a clip drag in the Timeline window.
            var baseClip = context.Timeline.GetRootTracks().OfType<AlpsTimelineTrack>().First().GetClips().First();
            var start = baseClip.start;
            var duration = baseClip.duration;
            try
            {
                baseClip.start = 1.0;
                baseClip.duration = 1.0;
                context.Director.RebuildGraph();

                var states = context.Sample(0.5f);
                for (var i = 0; i < states.Length; i++)
                {
                    var diagnostics = $"authored: {authored[i]}\nnow: {states[i]}";
                    Assert.AreEqual(authored[i].Pan, states[i].Pan, Tolerance, "Undriven channels fall back to the authored state.\n" + diagnostics);
                    Assert.AreEqual(authored[i].Intensity, states[i].Intensity, Tolerance, diagnostics);
                    Assert.AreEqual(authored[i].Color, states[i].Color, diagnostics);
                    Assert.AreEqual(authored[i].Gobo, states[i].Gobo, diagnostics);
                }
            }
            finally
            {
                baseClip.start = start;
                baseClip.duration = duration;
            }
        }

        [Test]
        public void Level3_BrightnessAbove100ScalesTheTintAndCapturesBack()
        {
            var fixture = OpenFreshScene().Fixtures[0];
            var frame = new float[AlpsShowEvaluator.FrameStride];
            var block = new MaterialPropertyBlock();

            // VRSL's default HDR white reads as white at 200%.
            fixture.globalIntensity = 1f;
            fixture.lightColorTint = new Color(2f, 2f, 2f, 1f);
            AlpsShowPlayer.CaptureVRSL(fixture, frame, 0);
            Assert.AreEqual(200f, frame[AlpsShowEvaluator.FrameBrightness], Tolerance);
            Assert.AreEqual(1f, frame[AlpsShowEvaluator.FrameRed], Tolerance);

            AlpsShowPlayer.ApplyVRSL(fixture, frame, 0, block);
            Assert.AreEqual(1f, fixture.globalIntensity, Tolerance);
            Assert.AreEqual(2f, fixture.lightColorTint.r, Tolerance, "The captured default applies back unchanged.");

            frame[AlpsShowEvaluator.FrameBrightness] = 150f;
            AlpsShowPlayer.ApplyVRSL(fixture, frame, 0, block);
            Assert.AreEqual(1f, fixture.globalIntensity, Tolerance);
            Assert.AreEqual(1.5f, fixture.lightColorTint.g, Tolerance, "Above 100% the tint carries the rest.");

            frame[AlpsShowEvaluator.FrameBrightness] = 50f;
            AlpsShowPlayer.ApplyVRSL(fixture, frame, 0, block);
            Assert.AreEqual(0.5f, fixture.globalIntensity, Tolerance);
            Assert.AreEqual(1f, fixture.lightColorTint.b, Tolerance, "White at or below 100% is a tint of 1.");
        }

        private static void AssertRestored(Context context, FixtureState[] authored)
        {
            for (var i = 0; i < context.Fixtures.Length; i++)
            {
                var restored = FixtureState.Capture(context.Fixtures[i]);
                var diagnostics = $"authored: {authored[i]}\nrestored: {restored}";
                Assert.AreEqual(authored[i].EnableDmx, restored.EnableDmx, diagnostics);
                Assert.IsTrue(context.Fixtures[i].enableStrobe, "Strobe was authored on.\n" + diagnostics);
                Assert.AreEqual(authored[i].Intensity, restored.Intensity, Tolerance, diagnostics);
                Assert.AreEqual(authored[i].Pan, restored.Pan, Tolerance, diagnostics);
                Assert.AreEqual(authored[i].Color, restored.Color, diagnostics);
                Assert.AreEqual(authored[i].Gobo, restored.Gobo, diagnostics);
            }
        }

        /// <summary>Registers gathered properties with animation mode, like the Timeline window does.</summary>
        private sealed class AnimationModeCollector : IPropertyCollector
        {
            public readonly List<(Component, string)> Registered = new List<(Component, string)>();

            public void AddFromName(Component component, string name)
            {
                var property = new SerializedObject(component).FindProperty(name);
                Assert.NotNull(property, $"{component.GetType().Name} has no serialized property '{name}'.");

                string value;
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        value = property.boolValue ? "1" : "0";
                        break;
                    case SerializedPropertyType.Integer:
                        value = property.intValue.ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        value = property.floatValue.ToString("R", CultureInfo.InvariantCulture);
                        break;
                }

                AnimationMode.AddPropertyModification(
                    EditorCurveBinding.FloatCurve(string.Empty, component.GetType(), name),
                    new PropertyModification { target = component, propertyPath = name, value = value },
                    false);
                Registered.Add((component, name));
            }

            public void PushActiveGameObject(GameObject gameObject) { }
            public void PopActiveGameObject() { }
            public void AddFromClip(AnimationClip clip) { }
            public void AddFromClips(IEnumerable<AnimationClip> clips) { }
            public void AddFromName<T>(string name) where T : Component { }
            public void AddFromName(string name) { }
            public void AddFromClip(GameObject obj, AnimationClip clip) { }
            public void AddFromClips(GameObject obj, IEnumerable<AnimationClip> clips) { }
            public void AddFromName<T>(GameObject obj, string name) where T : Component { }
            public void AddFromName(GameObject obj, string name) { }
            public void AddFromComponent(GameObject obj, Component component) { }
            public void AddObjectProperties(Object obj, AnimationClip clip) { }
        }

        [Test]
        public void Level4_AppliedShowMatchesThePreviewAndKeepsOtherTracks()
        {
            var context = OpenFreshScene();
            var preview = context.Sample(AccentTime);

            // Swapping the timeline out and back gives the build conversion a fresh graph.
            context.Director.playableAsset = null;
            context.Director.playableAsset = context.Timeline;

            var player = AlpsShowSetup.GetOrCreatePlayer(context.Director);
            Assert.IsFalse(player.gameObject.activeSelf, "The player waits inactive in the authoring scene.");

            var sourceActivation = context.Timeline.GetRootTracks().OfType<ActivationTrack>().Single();
            var sourceAnimation = context.Timeline.GetRootTracks().OfType<AnimationTrack>().Single();
            var activationBinding = context.Director.GetGenericBinding(sourceActivation);
            var animationBinding = context.Director.GetGenericBinding(sourceAnimation);

            var errors = new List<string>();
            try
            {
                Assert.IsTrue(AlpsShowApplier.Apply(context.Director.gameObject.scene, true, errors), string.Join("\n", errors));

                var build = context.Director.playableAsset as TimelineAsset;
                Assert.NotNull(build);
                Assert.AreNotSame(context.Timeline, build, "The director switches to the build timeline.");
                Assert.IsFalse(AlpsBuildTimeline.HasAlpsTracks(build), "ALPS tracks are replaced by the player.");
                Assert.IsTrue(AlpsBuildTimeline.HasAlpsTracks(context.Timeline), "The authoring timeline is left alone.");

                var buildActivation = build.GetRootTracks().OfType<ActivationTrack>().Single();
                var buildAnimation = build.GetRootTracks().OfType<AnimationTrack>().Single();
                Assert.AreEqual(ActivationStart, buildActivation.GetClips().Single().start, 0.0001);
                Assert.AreEqual(AnimationDuration, buildAnimation.GetClips().Single().duration, 0.0001);
                Assert.AreSame(activationBinding, context.Director.GetGenericBinding(buildActivation));
                Assert.AreSame(animationBinding, context.Director.GetGenericBinding(buildAnimation));

                Assert.IsTrue(player.gameObject.activeSelf);
                Assert.IsNull(Object.FindObjectOfType<AlpsFixture>(), "Authoring components are stripped.");
                Assert.IsNull(Object.FindObjectOfType<AlpsFixtureGroup>());

                player.EvaluateAt(AccentTime);
                for (var i = 0; i < context.Fixtures.Length; i++)
                {
                    var runtime = FixtureState.Capture(context.Fixtures[i]);
                    var diagnostics = $"preview: {preview[i]}\nruntime: {runtime}";
                    Assert.AreEqual(preview[i].Pan, runtime.Pan, Tolerance, diagnostics);
                    Assert.AreEqual(preview[i].Tilt, runtime.Tilt, Tolerance, diagnostics);
                    Assert.AreEqual(preview[i].Intensity, runtime.Intensity, Tolerance, diagnostics);
                    Assert.AreEqual(preview[i].Color, runtime.Color, diagnostics);
                    Assert.AreEqual(preview[i].ConeWidth, runtime.ConeWidth, Tolerance, diagnostics);
                    Assert.AreEqual(preview[i].ConeLength, runtime.ConeLength, Tolerance, diagnostics);
                    Assert.AreEqual(preview[i].Gobo, runtime.Gobo, diagnostics);
                }

                var programAsset = UdonSharpProgramAsset.GetProgramAssetForClass(typeof(AlpsShowPlayer));
                Assert.NotNull(programAsset, "No UdonSharp program asset was created for the player.");
                Assert.NotNull(programAsset.GetRealProgram(), "The player did not compile to Udon.");
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(player);
                Assert.NotNull(backing, "The player has no backing UdonBehaviour.");
                Assert.IsTrue(UdonSharpEditorUtility.IsUdonSharpBehaviour(backing));
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(AlpsShowSetup.GeneratedFolder))
                {
                    AssetDatabase.DeleteAsset(AlpsShowSetup.GeneratedFolder);
                }
            }
        }

        [Test]
        public void Level4_ValidationReportsAMissingPlayer()
        {
            var context = OpenFreshScene();
            var errors = new List<string>();
            var warnings = new List<string>();

            AlpsShowApplier.Validate(context.Director.gameObject.scene, errors, warnings);

            Assert.That(errors, Has.Some.Contains("Set Up Show Player"));
        }
    }
}
