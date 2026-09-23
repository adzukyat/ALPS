using System.Collections;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Covers editing several clips at once: an edit on the shown clip carries only what
    /// changed to the others, and whole part edits are repeated on each clip.
    /// </summary>
    public class AlpsMultiClipEditTests
    {
        private static AlpsClipEffectSet WithBrightness(float value)
        {
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Brightness).brightness.value = value;
            return set;
        }

        [SetUp]
        public void ResetClipboard()
        {
            AlpsEffectClipboard.Clear();
        }

        [Test]
        public void Apply_CarriesOnlyTheChangedField()
        {
            var shown = WithBrightness(100f);
            var other = WithBrightness(30f);
            other.phase.beatsPerCycle = 8f;

            var before = new AlpsClipEffectSet(shown);
            shown.effects[0].brightness.isRange = true;
            AlpsClipEditSync.Apply(before, shown, other);

            var brightness = other.effects[0].brightness;
            Assert.IsTrue(brightness.isRange, "The toggled switch is carried.");
            Assert.AreEqual(30f, brightness.value, 0.0001f, "An untouched value keeps its own setting.");
            Assert.AreEqual(8f, other.phase.beatsPerCycle, 0.0001f, "Untouched shared settings keep their own values.");
        }

        [Test]
        public void Apply_CarriesNestedPhaseSettings()
        {
            var shown = WithBrightness(100f);
            var other = WithBrightness(100f);
            other.phase.spread = 0.25f;

            var before = new AlpsClipEffectSet(shown);
            shown.phase.mode = AlpsPhaseMode.Random;
            shown.effects[0].brightness.ownPhase.beatsPerCycle = 16f;
            AlpsClipEditSync.Apply(before, shown, other);

            Assert.AreEqual(AlpsPhaseMode.Random, other.phase.mode);
            Assert.AreEqual(0.25f, other.phase.spread, 0.0001f);
            Assert.AreEqual(16f, other.effects[0].brightness.ownPhase.beatsPerCycle, 0.0001f);
            Assert.AreNotSame(shown.phase, other.phase, "Nothing is shared between clips.");
        }

        [Test]
        public void Apply_MatchesEffectsByKindAndSkipsMissingOnes()
        {
            var shown = new AlpsClipEffectSet();
            shown.Add(AlpsEffectKind.Move);
            shown.Add(AlpsEffectKind.Brightness);
            var other = WithBrightness(100f);

            var before = new AlpsClipEffectSet(shown);
            shown.effects[shown.IndexOf(AlpsEffectKind.Move, AlpsParity.All)].pan.value = 45f;
            shown.effects[shown.IndexOf(AlpsEffectKind.Brightness, AlpsParity.All)].brightness.value = 150f;
            AlpsClipEditSync.Apply(before, shown, other);

            Assert.AreEqual(1, other.effects.Count, "A kind the other clip lacks is not created.");
            Assert.AreEqual(150f, other.effects[0].brightness.value, 0.0001f, "Brightness is found at another index.");
        }

        [Test]
        public void Apply_LeavesInspectorStateAlone()
        {
            var shown = WithBrightness(100f);
            var other = WithBrightness(100f);

            var before = new AlpsClipEffectSet(shown);
            shown.effects[0].expanded = false;
            shown.phaseExpanded = false;
            AlpsClipEditSync.Apply(before, shown, other);

            Assert.IsTrue(other.effects[0].expanded);
            Assert.IsTrue(other.phaseExpanded);
        }

        [Test]
        public void Apply_RepeatsASingleStopInsertOrRemove()
        {
            var shown = new AlpsClipEffectSet();
            shown.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.red));
            var other = new AlpsClipEffectSet();
            other.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.green));

            var before = new AlpsClipEffectSet(shown);
            shown.effects[0].colorStops.Add(new AlpsColorStop(Color.blue));
            AlpsClipEditSync.Apply(before, shown, other);

            var stops = other.effects[0].colorStops;
            Assert.AreEqual(2, stops.Count);
            Assert.AreEqual(Color.green, stops[0].color, "The existing stop stays.");
            Assert.AreEqual(Color.blue, stops[1].color, "The added stop is appended.");
            Assert.AreNotSame(shown.effects[0].colorStops[1], stops[1]);

            before = new AlpsClipEffectSet(shown);
            shown.effects[0].colorStops.RemoveAt(0);
            AlpsClipEditSync.Apply(before, shown, other);

            Assert.AreEqual(1, stops.Count);
            Assert.AreEqual(Color.blue, stops[0].color, "The stop at the removed position goes.");
        }

        [Test]
        public void Apply_CarriesAGradientEdit()
        {
            var shown = new AlpsClipEffectSet();
            shown.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop { isGradient = true });
            var other = new AlpsClipEffectSet(shown);

            var before = new AlpsClipEffectSet(shown);
            shown.effects[0].colorStops[0].gradient.SetKeys(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(1f, 0f) });
            AlpsClipEditSync.Apply(before, shown, other);

            var gradient = other.effects[0].colorStops[0].gradient;
            Assert.AreEqual(Color.blue, gradient.Evaluate(1f));
            Assert.AreNotSame(shown.effects[0].colorStops[0].gradient, gradient);
        }

        [Test]
        public void MixedValues_FlagsOnlyTheFieldsThatDiffer()
        {
            var shown = WithBrightness(100f);
            shown.Add(AlpsEffectKind.Move);
            var other = WithBrightness(30f);

            var mixed = new AlpsMixedValues(shown, new[] { other });
            var brightness = shown.effects[shown.IndexOf(AlpsEffectKind.Brightness, AlpsParity.All)].brightness;
            var move = shown.effects[shown.IndexOf(AlpsEffectKind.Move, AlpsParity.All)];

            Assert.IsTrue(mixed.IsMixed(brightness, nameof(AlpsAnimatableValue.value)));
            Assert.IsFalse(mixed.IsMixed(brightness, nameof(AlpsAnimatableValue.isRange)), "An equal field is not mixed.");
            Assert.IsFalse(mixed.IsMixed(shown, nameof(AlpsClipEffectSet.order)));
            Assert.IsFalse(mixed.IsMixed(move.pan, nameof(AlpsAnimatableValue.value)), "An effect the other clip lacks has nothing to differ from.");
        }

        [UnityTest]
        public IEnumerator Inspector_ShowsMixedValuesAndUnifiesThemOnCommit()
        {
            var a = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            var b = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            a.hideFlags = HideFlags.HideAndDontSave;
            b.hideFlags = HideFlags.HideAndDontSave;
            a.data = WithBrightness(100f);
            b.data = WithBrightness(30f);
            b.data.order = AlpsOrderMode.Reverse;

            var window = ScriptableObject.CreateInstance<EditorWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(new Object[] { a, b });
                var host = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(host);
                yield return null;

                var view = host.Q<AlpsClipInspectorView>();
                var order = view.Query<AlpsSegmentedControl>().ToList().First(control => control.label == "並び順");
                var slider = view.Query<AlpsValueSlider>().ToList().First(control => control.label == "明るさ");
                var box = slider.Q<AlpsNumberBox>().Q<TextField>();

                Assert.IsTrue(order.showMixedValue);
                Assert.IsFalse(order.Query(className: "alps-seg__item--selected").ToList().Any(), "A mixed choice selects nothing.");
                Assert.AreEqual(AlpsMixedValues.MixedText, box.value, "A mixed number shows a dash.");

                var speed = view.Query<AlpsStepper>().ToList().First(control => control.label == "速度");
                Assert.IsFalse(speed.showMixedValue, "An equal value shows as usual.");

                // Picking the value the shown clip already has still reaches the other clip.
                order.value = (int)AlpsOrderMode.Normal;
                Assert.AreEqual(AlpsOrderMode.Normal, b.data.order);
                Assert.IsFalse(order.showMixedValue);

                slider.value = 100f;
                var brightness = b.data.effects[b.data.IndexOf(AlpsEffectKind.Brightness, AlpsParity.All)].brightness;
                Assert.AreEqual(100f, brightness.value, 0.0001f);
                Assert.IsFalse(slider.showMixedValue);
                Assert.AreNotEqual(AlpsMixedValues.MixedText, box.value);
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }

                window.Close();
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [UnityTest]
        public IEnumerator Inspector_CarriesEditsAndDeletesToEverySelectedClip()
        {
            var a = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            var b = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            a.hideFlags = HideFlags.HideAndDontSave;
            b.hideFlags = HideFlags.HideAndDontSave;
            a.data = WithBrightness(100f);
            a.data.Add(AlpsEffectKind.Cone);
            b.data = WithBrightness(30f);
            b.data.Add(AlpsEffectKind.Cone);
            b.data.Add(AlpsEffectKind.Move);
            var c = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            c.hideFlags = HideFlags.HideAndDontSave;
            c.data = WithBrightness(100f);

            var window = ScriptableObject.CreateInstance<EditorWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(new Object[] { a, b, c });
                Assert.IsInstanceOf<AlpsTimelineClipInspector>(inspector);

                var host = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(host);
                yield return null;

                var view = host.Q<AlpsClipInspectorView>();
                Assert.NotNull(view, "Several clips still get the ALPS view.");
                Assert.NotNull(view.Q<HelpBox>(className: "alps-multi-notice"), "The view says it edits several clips.");

                var order = view.Query<AlpsSegmentedControl>().ToList().First(control => control.label == "並び順");
                order.value = (int)AlpsOrderMode.Symmetric;
                Assert.AreEqual(AlpsOrderMode.Symmetric, a.data.order);
                Assert.AreEqual(AlpsOrderMode.Symmetric, b.data.order, "The edit reaches the other clip.");
                Assert.AreEqual(30f, b.data.effects[b.data.IndexOf(AlpsEffectKind.Brightness, AlpsParity.All)].brightness.value, 0.0001f);

                    var coneCard = view.Query<AlpsEffectCard>().ToList()
                    .First(card => card.Title == AlpsEffectCatalog.GetName(AlpsEffectKind.Cone));
                coneCard.RequestDelete();
                Assert.AreEqual(-1, a.data.IndexOf(AlpsEffectKind.Cone, AlpsParity.All));
                Assert.AreEqual(-1, b.data.IndexOf(AlpsEffectKind.Cone, AlpsParity.All), "The delete is repeated on the other clip.");

                // Adding Move to the shown clip adds it where it is missing, but does not
                // split the Move the second clip already has into even and odd.
                view.Q<AlpsAddEffectCatalog>().RequestAdd(AlpsEffectKind.Move);
                Assert.AreEqual(0, a.data.IndexOf(AlpsEffectKind.Move, AlpsParity.All));
                Assert.AreEqual(1, b.data.effects.Count(effect => effect.kind == AlpsEffectKind.Move));
                Assert.AreEqual(0, b.data.IndexOf(AlpsEffectKind.Move, AlpsParity.All));
                Assert.AreEqual(0, c.data.IndexOf(AlpsEffectKind.Move, AlpsParity.All), "A clip without Move gets one too.");
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }

                window.Close();
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(c);
            }
        }
    }
}
