using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Covers the clip inspector: that the whole tree builds, that the conditional
    /// visibility rules hold, and that the odd / even split behaves as intended.
    /// </summary>
    public class AlpsClipInspectorViewUiTests
    {
        private static AlpsClipEffectSet BuildFullSet()
        {
            var set = new AlpsClipEffectSet();
            foreach (var kind in AlpsEffectCatalog.Order)
            {
                set.Add(kind);
            }

            return set;
        }

        private static IEnumerable<Label> Labels(VisualElement root)
        {
            return root.Query<Label>().ToList();
        }

        private static bool HasVisibleText(VisualElement root, string text)
        {
            return Labels(root).Any(label => label.text == text && IsShown(label));
        }

        private static bool IsShown(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (current.style.display.keyword == StyleKeyword.Undefined &&
                    current.style.display.value == DisplayStyle.None)
                {
                    return false;
                }
            }

            return true;
        }

        [SetUp]
        public void ResetClipboard()
        {
            // The payload is static and lives for the editor session.
            AlpsEffectClipboard.Clear();
        }

        [Test]
        public void CustomEditor_BuildsTheInspectorAndResolvesTheStyleSheet()
        {
            var asset = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.data = BuildFullSet();

            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(asset);
                Assert.IsInstanceOf<AlpsTimelineClipInspector>(
                    inspector,
                    "AlpsTimelineClip must use AlpsTimelineClipInspector.");

                var host = inspector.CreateInspectorGUI();
                Assert.NotNull(host);

                // The host is a rebuild container. The styled tree is the view inside it.
                var view = host.Q<AlpsClipInspectorView>();
                Assert.NotNull(view, "The inspector did not build a AlpsClipInspectorView.");
                Assert.AreEqual(
                    1,
                    view.styleSheets.count,
                    "AlpsInspector.uss did not resolve, so the inspector would render unstyled.");

                // The style tokens hang off this class.
                Assert.IsTrue(view.ClassListContains("alps-root"));
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }

                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CustomEditor_DrawsThroughTheImguiClipInspector()
        {
            // The timeline clip inspector only calls OnInspectorGUI on the clip asset editor.
            // Without an override, Unity falls back to the default fields and the ALPS UI is lost.
            var method = typeof(AlpsTimelineClipInspector).GetMethod(
                "OnInspectorGUI",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(method);
            Assert.AreEqual(
                typeof(AlpsTimelineClipInspector),
                method.DeclaringType,
                "The editor must override OnInspectorGUI for the timeline clip inspector.");

            Assert.IsTrue(
                AlpsImguiHost.IsAvailable,
                "This Unity version no longer exposes the IMGUI container stack, so the view cannot be hung on it.");
        }

        [Test]
        public void CustomEditor_HangsTheViewAfterTheImguiContainer()
        {
            var asset = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.data = BuildFullSet();

            var window = ScriptableObject.CreateInstance<EditorWindow>();
            var inspectorElement = new VisualElement();
            var container = new IMGUIContainer(() => { });
            inspectorElement.Add(container);
            window.rootVisualElement.Add(inspectorElement);

            AlpsTimelineClipInspector inspector = null;
            try
            {
                inspector = (AlpsTimelineClipInspector)UnityEditor.Editor.CreateEditor(asset);

                Assert.IsTrue(inspector.AttachTo(container));
                Assert.AreEqual(2, inspectorElement.childCount, "The view is a sibling of the IMGUI block.");
                var injected = inspectorElement[1];
                Assert.AreEqual(
                    inspectorElement.IndexOf(container) + 1,
                    inspectorElement.IndexOf(injected),
                    "The view follows the IMGUI block it belongs to.");
                Assert.NotNull(injected.Q<AlpsClipInspectorView>(), "The injected element is the ALPS view.");

                // Redrawing keeps one view rather than stacking copies.
                Assert.IsTrue(inspector.AttachTo(container));
                Assert.AreEqual(2, inspectorElement.childCount);

                Assert.IsFalse(inspector.AttachTo(null), "Outside IMGUI there is nothing to hang on.");
                Assert.AreEqual(1, inspectorElement.childCount, "The view is taken down with the inspector.");
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }

                Object.DestroyImmediate(window);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CustomEditor_WaitsForRepaintBeforeTouchingTheHierarchy()
        {
            // Unity throws when the hierarchy changes while the panel measures itself, and the
            // clip inspector runs its IMGUI block inside that pass.
            var asset = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.data = BuildFullSet();

            var parent = new VisualElement();
            var container = new IMGUIContainer(() => { });
            parent.Add(container);

            AlpsTimelineClipInspector inspector = null;
            try
            {
                inspector = (AlpsTimelineClipInspector)UnityEditor.Editor.CreateEditor(asset);

                Assert.IsTrue(inspector.AttachTo(container, false));
                Assert.AreEqual(1, parent.childCount, "Nothing moves while the panel is laying out.");

                Assert.IsTrue(inspector.AttachTo(container, true));
                Assert.AreEqual(2, parent.childCount, "Repainting is when the view can be moved.");
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }

                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CustomEditor_FollowsTheClipInspectorFoldout()
        {
            // Timeline stops calling OnInspectorGUI while its asset title bar is collapsed. The
            // view sits outside IMGUI, so it has to hide itself when a repaint skips the draw.
            var asset = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.data = BuildFullSet();

            var expanded = true;
            AlpsTimelineClipInspector inspector = null;
            System.Action original = null;
            var parent = new VisualElement();
            var container = new IMGUIContainer();
            parent.Add(container);

            try
            {
                inspector = (AlpsTimelineClipInspector)UnityEditor.Editor.CreateEditor(asset);
                var target = inspector;
                original = () =>
                {
                    if (expanded)
                    {
                        target.MarkDrawn();
                    }
                };
                container.onGUIHandler = original;

                Assert.IsTrue(inspector.AttachTo(container, true));
                Assert.AreNotSame(original, container.onGUIHandler, "The container handler is watched.");
                var injected = parent[1];

                inspector.RunContainerPass(true);
                Assert.AreEqual(DisplayStyle.Flex, injected.style.display.value, "Expanded shows the view.");

                expanded = false;
                inspector.RunContainerPass(false);
                Assert.AreEqual(DisplayStyle.Flex, injected.style.display.value, "Only a repaint decides.");

                inspector.RunContainerPass(true);
                Assert.AreEqual(DisplayStyle.None, injected.style.display.value, "Collapsed hides the view.");

                expanded = true;
                inspector.RunContainerPass(true);
                Assert.AreEqual(DisplayStyle.Flex, injected.style.display.value, "Expanding shows it again.");

                Assert.IsFalse(inspector.AttachTo(null, true));
                Assert.AreSame(original, container.onGUIHandler, "Detaching restores the container handler.");
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }

                Object.DestroyImmediate(asset);
            }
        }

        [UnityTest]
        public IEnumerator Undo_RestoresTheModelAndRebindsTheInspector()
        {
            // Undo needs a real asset on disk: RegisterCompleteObjectUndo on a
            // HideAndDontSave instance is not recorded.
            const string folder = "Assets/AlpsUndoTest";
            const string path = folder + "/UndoSet.asset";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "AlpsUndoTest");
            }

            var asset = ScriptableObject.CreateInstance<AlpsTimelineClip>();
            asset.data = new AlpsClipEffectSet();
            asset.data.Add(AlpsEffectKind.Brightness);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(asset);
                var host = inspector.CreateInspectorGUI();

                var before = asset.data.effects[0].brightness.value;

                Undo.RegisterCompleteObjectUndo(asset, "Edit Clip Effects");
                asset.data.effects[0].brightness.value = 42f;
                EditorUtility.SetDirty(asset);

                yield return null;

                Undo.PerformUndo();
                yield return null;

                Assert.AreEqual(
                    before,
                    asset.data.effects[0].brightness.value,
                    0.001f,
                    "Undo did not restore the model.");

                // The view must be rebuilt against whatever instance the asset now holds,
                // not the one captured when the inspector was first created.
                var view = host.Q<AlpsClipInspectorView>();
                Assert.NotNull(view, "The inspector did not rebuild after undo.");

                var slider = view.Query<AlpsValueSlider>().ToList().First(s => s.label == "明るさ");
                Assert.AreEqual(before, slider.value, 0.001f, "The control still shows the pre-undo value.");
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }

                AssetDatabase.DeleteAsset(path);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void EveryEffectKind_BuildsWithoutError()
        {
            var set = BuildFullSet();
            var view = new AlpsClipInspectorView(set);

            Assert.AreEqual(AlpsEffectCatalog.Order.Length, set.effects.Count);
            Assert.IsTrue(HasVisibleText(view, "共通設定"));
            Assert.IsTrue(HasVisibleText(view, "並び順"));
            Assert.IsTrue(HasVisibleText(view, "プロファイル"));

            foreach (var kind in AlpsEffectCatalog.Order)
            {
                Assert.IsTrue(
                    HasVisibleText(view, AlpsEffectCatalog.GetName(kind)),
                    $"Card for {kind} was not rendered.");
            }
        }

        [UnityTest]
        public IEnumerator Bpm_FollowsTheShowUntilItDiffers()
        {
            // ChangeEvent only fires on a panel, so this runs inside a real window.
            var set = new AlpsClipEffectSet();
            var window = ScriptableObject.CreateInstance<PanelHostWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            try
            {
                var view = new AlpsClipInspectorView(set, showBpm: 140f);
                var reported = new List<float>();
                view.ShowBpmChanged += reported.Add;
                window.rootVisualElement.Add(view);
                yield return null;

                var steppers = view.Query<AlpsStepper>().ToList();
                var global = steppers.First(stepper => stepper.label == "全体BPM");
                var bpm = steppers.First(stepper => stepper.label == "BPMオーバーライド");
                Assert.Less(steppers.IndexOf(global), steppers.IndexOf(bpm), "全体BPM sits above the override.");
                Assert.AreEqual(140f, global.value);
                Assert.AreEqual(140f, bpm.value, "An unset clip shows the show's tempo.");

                global.value = 128f;
                CollectionAssert.AreEqual(new[] { 128f }, reported, "The show's tempo goes to the host.");
                Assert.AreEqual(0f, set.bpm, "The show's tempo is not stored on the clip.");
                Assert.AreEqual(128f, bpm.value, "An unset clip keeps showing the show's tempo.");

                bpm.value = 150f;
                Assert.AreEqual(150f, set.bpm);

                global.value = 100f;
                Assert.AreEqual(150f, bpm.value, "A clip with its own tempo keeps it.");

                bpm.value = 100f;
                Assert.AreEqual(0f, set.bpm, "The show's tempo goes back to following the show.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SharedSettings_RendersEveryPlannedRow()
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.PingPong;
            var view = new AlpsClipInspectorView(set);

            foreach (var row in new[] { "モード", "イージング", "往復比", "灯体単位", "ディレイ", "速度", "反転" })
            {
                Assert.IsTrue(HasVisibleText(view, row), $"共通設定 is missing the {row} row.");
            }
        }

        [Test]
        public void Delay_InPercentSnapsWhereTheTripLandsOnTheBeat()
        {
            var twoBeats = DelaySlider(2f);
            Assert.AreEqual("%", twoBeats.Unit);
            CollectionAssert.Contains(twoBeats.Snaps, 100f, "The full trip closes the wave on itself.");
            CollectionAssert.Contains(twoBeats.Snaps, 50f, "Half the trip is one beat of a two beat cycle.");

            // A cycle that is not a round number of beats still gets its beat marks, which is
            // the point of moving them with the speed rather than fixing them at 25% steps.
            var threeBeats = DelaySlider(3f);
            Assert.IsTrue(
                threeBeats.Snaps.Any(snap => Mathf.Abs(snap - 100f / 3f) < 0.01f),
                "One beat of a three beat cycle is a third of the trip.");
            Assert.LessOrEqual(threeBeats.Snaps.Length, AlpsSnapPoints.MaxTicks, "The rail must not crowd.");
        }

        /// <summary>The ディレイ row of a clip whose cycle is <paramref name="beatsPerCycle"/> beats.</summary>
        private static AlpsValueSlider DelaySlider(float beatsPerCycle)
        {
            var set = new AlpsClipEffectSet();
            set.phase.beatsPerCycle = beatsPerCycle;
            set.phase.spread = 1f;
            var view = new AlpsClipInspectorView(set);
            return view.Query<AlpsValueSlider>().ToList().First(slider => slider.label == "ディレイ");
        }

        [UnityTest]
        public IEnumerator DelayBeatsFlag_SwitchesTheUnitWithoutMovingTheLook()
        {
            // ChangeEvent only fires on a panel, so this runs inside a real window.
            var set = new AlpsClipEffectSet();
            set.phase.beatsPerCycle = 2f;
            set.phase.spread = 0.5f;

            var window = ScriptableObject.CreateInstance<PanelHostWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            try
            {
                var view = new AlpsClipInspectorView(set);
                window.rootVisualElement.Add(view);
                yield return null;

                var percent = view.Query<AlpsValueSlider>().ToList().First(slider => slider.label == "ディレイ");
                var beats = view.Query<AlpsStepper>().ToList().First(stepper => stepper.label == "ディレイ");
                var flag = view.Query<AlpsRangeFlag>().ToList().First(f => f.Q<Label>().text == "拍");
                var speed = view.Query<AlpsStepper>().ToList().First(stepper => stepper.label == "速度");
                Assert.AreEqual(50f, percent.value, 0.001f);
                Assert.AreEqual(DisplayStyle.Flex, percent.style.display.value);
                Assert.AreEqual(DisplayStyle.None, beats.style.display.value);
                Assert.AreSame(flag.parent.Children().Last(), flag, "拍 stays on the right edge of the row.");
                Assert.AreEqual(-16f, beats.Minimum, 0.001f);
                Assert.AreEqual(16f, beats.Maximum, 0.001f, "Beats stop at four bars either way.");

                flag.value = true;
                Assert.IsTrue(set.phase.spreadInBeats);
                Assert.AreEqual(DisplayStyle.None, percent.style.display.value);
                Assert.AreEqual(DisplayStyle.Flex, beats.style.display.value, "Beats are counted like the speed, not dragged.");
                Assert.AreEqual(1f, beats.value, 0.001f, "Half the trip of a two beat cycle is one beat.");
                Assert.AreEqual(0.5f, set.phase.SpreadCycles, 0.0001f, "The switch alone must not move the fixtures.");

                speed.value = 4f;
                Assert.AreEqual(1f, beats.value, 0.001f, "A delay in beats stays that many beats.");
                Assert.AreEqual(0.25f, set.phase.SpreadCycles, 0.0001f);

                beats.value = 2f;
                flag.value = false;
                Assert.AreEqual(50f, percent.value, 0.001f, "Two beats of a four beat cycle is half the trip.");
                Assert.AreEqual(0.5f, set.phase.SpreadCycles, 0.0001f);

                speed.value = 0f;
                Assert.IsFalse(flag.enabledSelf, "Without a speed there are no beats to count.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PingPongRatio_IsOnlyShownForPingPong()
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.Forward;
            var view = new AlpsClipInspectorView(set);

            var ratio = view.Query<AlpsValueSlider>().ToList().First(s => s.label == "往復比");
            Assert.AreEqual(DisplayStyle.None, ratio.style.display.value, "往復比 must be hidden outside ピンポン.");

            set.phase.mode = AlpsPhaseMode.PingPong;
            view.Query<AlpsPhaseSettingsView>().First().Refresh();
            Assert.AreEqual(DisplayStyle.Flex, ratio.style.display.value);
        }

        [Test]
        public void RandomMode_HidesEasingAndInverse()
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.Random;
            var view = new AlpsClipInspectorView(set);

            var easing = view.Query<AlpsEasingGrid>().First();
            var inverse = view.Query<AlpsToggleSwitch>().ToList().First(t => t.label == "反転");

            Assert.AreEqual(DisplayStyle.None, easing.style.display.value, "イージング must be hidden in ランダム.");
            Assert.AreEqual(DisplayStyle.None, inverse.style.display.value, "反転 must be hidden in ランダム.");
        }

        [Test]
        public void TimingAndOwnMotion_AppearOnlyWithTwoOrMoreStops()
        {
            var set = new AlpsClipEffectSet();
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.isRange = false;

            var view = new AlpsClipInspectorView(set);
            var timing = view.Query<AlpsSegmentedControl>().ToList()
                .First(c => c.label == "タイミング");

            Assert.AreEqual(
                DisplayStyle.None,
                timing.style.display.value,
                "タイミング must stay hidden while the parameter is a single value.");

            move.tilt.isRange = true;
            move.tilt.range = new Vector2(-40f, 35f);
            view.Query<AlpsAnimatableView>().First().Refresh();

            Assert.AreEqual(DisplayStyle.Flex, timing.style.display.value);
        }

        [Test]
        public void Frames_AreTitledRangeAndSpread()
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.Forward;
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.hasSpread = true;
            move.tilt.isRange = true;
            move.tilt.spreadRange = new Vector2(-10f, 10f);

            var view = new AlpsClipInspectorView(set);
            var tilt = view.Query<AlpsAnimatableView>().First();
            var frames = tilt.Children().Where(c => c.ClassListContains("alps-sub")).ToList();
            var titles = frames.Select(f => f.Q<Label>(className: "alps-sub__title")).ToList();

            CollectionAssert.AreEqual(new[] { "レンジ", "広がり" }, titles.Select(t => t.text).ToArray());
            Assert.IsTrue(frames.All(f => f.style.display.value == DisplayStyle.Flex));
            Assert.IsTrue(frames[0].Children().OfType<AlpsSegmentedControl>().Any(c => c.label == "タイミング"), "Timing lives in the range frame.");
            Assert.IsTrue(frames[1].Query<AlpsValueSlider>().ToList().Any(s => s.label == "オフセット"), "The offset lives in the spread frame.");

            // Without a range the frame has nothing to hold, and without spread neither does the other.
            move.tilt.isRange = false;
            move.tilt.hasSpread = false;
            tilt.Refresh();
            Assert.IsTrue(frames.All(f => f.style.display.value == DisplayStyle.None));

            // Ping-pong alone gives a fixed value nothing to put in the range frame.
            set.phase.mode = AlpsPhaseMode.PingPong;
            tilt.Refresh();
            Assert.AreEqual(DisplayStyle.None, frames[0].style.display.value);
        }

        [Test]
        public void BlackoutOnReturn_ShowsWhileThePhaseDrivingBrightnessPingPongs()
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.Forward;
            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.brightness.isRange = true;
            brightness.brightness.range = new Vector2(0f, 100f);

            var view = new AlpsClipInspectorView(set);
            var effect = view.Query<AlpsEffectView>().First();
            var blackout = view.Query<AlpsToggleSwitch>().ToList().Single(t => t.label == "復路で消灯");
            var fadeFrame = effect.Q<AlpsFadeSlider>().parent;
            Assert.AreEqual(DisplayStyle.None, blackout.style.display.value, "Forward has no return leg.");

            set.phase.mode = AlpsPhaseMode.PingPong;
            effect.Refresh();
            Assert.AreEqual(DisplayStyle.Flex, blackout.style.display.value);
            Assert.AreEqual(DisplayStyle.None, fadeFrame.style.display.value, "Fades wait for the switch.");

            brightness.blackoutOnReturn = true;
            effect.Refresh();
            Assert.AreEqual(DisplayStyle.Flex, fadeFrame.style.display.value);
            Assert.IsNull(fadeFrame.Q<Label>(className: "alps-sub__title"), "The fade frame is untitled.");

            // Own phase takes over which leg is the return.
            brightness.brightness.useOwnPhase = true;
            brightness.brightness.ownPhase.mode = AlpsPhaseMode.Forward;
            effect.Refresh();
            Assert.AreEqual(DisplayStyle.None, blackout.style.display.value);
            Assert.AreEqual(DisplayStyle.None, fadeFrame.style.display.value);
        }

        [Test]
        public void FadeSlider_MirrorsTheOutTrackAndResetsEachSide()
        {
            var fade = new AlpsFadeSlider("フェード", 50f, "%") { DefaultValue = new Vector2(10f, 20f) };
            fade.SetValueWithoutNotify(new Vector2(25f, 40f));

            var tracks = fade.Query<AlpsSliderTrack>().ToList();
            Assert.AreEqual(2, tracks.Count);
            Assert.AreEqual(0.5f, tracks[0].High, 0.0001f, "Fade in fills from the left edge.");
            Assert.AreEqual(0f, tracks[0].Origin);
            Assert.AreEqual(0.2f, tracks[1].High, 0.0001f, "Fade out fills from the right edge.");
            Assert.AreEqual(1f, tracks[1].Origin);

            fade.SetValueWithoutNotify(new Vector2(80f, -5f));
            Assert.AreEqual(new Vector2(50f, 0f), fade.value, "Each side stays within its own half.");

            fade.SetValueWithoutNotify(new Vector2(30f, 30f));
            fade.ResetToDefault(true);
            Assert.AreEqual(new Vector2(10f, 30f), fade.value, "Only the clicked side comes back.");
            fade.ResetToDefault(false);
            Assert.AreEqual(new Vector2(10f, 20f), fade.value);
        }

        [UnityTest]
        public IEnumerator SpreadFlag_TurnsTheRowIntoASpreadAndRChoosesWhichRanges()
        {
            // ChangeEvent only fires on a panel, so this runs inside a real window.
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.Forward;
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.value = 10f;
            move.tilt.spread = 12f;

            var window = ScriptableObject.CreateInstance<PanelHostWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            try
            {
                var view = new AlpsClipInspectorView(set);
                window.rootVisualElement.Add(view);
                yield return null;

                var tilt = view.Query<AlpsAnimatableView>().First();
                // The own phase panel inside the row carries its own 拍 flag, which is not part of the row.
                var flags = tilt.Query<AlpsRangeFlag>().ToList()
                    .Where(f => f.GetFirstAncestorOfType<AlpsPhaseSettingsView>() == null)
                    .ToList();
                var singles = tilt.Query<AlpsValueSlider>().ToList().Where(s => s.label == "Tilt").ToList();
                var ranges = tilt.Query<AlpsRangeSlider>().ToList().Where(s => s.label == "Tilt").ToList();
                var frames = tilt.Children().Where(c => c.ClassListContains("alps-sub")).ToList();
                var offset = frames[1].Q<AlpsValueSlider>();
                var timing = frames[0].Q<AlpsSegmentedControl>();

                Assert.AreEqual(2, flags.Count, "One row, two letters.");
                Assert.AreEqual("R", flags[0].Q<Label>().text);
                Assert.AreEqual("S", flags[1].Q<Label>().text);
                Assert.AreEqual(2, singles.Count, "The value and the spread keep the property's name.");
                Assert.AreEqual(DisplayStyle.None, frames[1].style.display.value);

                flags[1].value = true;
                Assert.IsTrue(move.tilt.hasSpread);
                Assert.AreEqual(DisplayStyle.None, singles[0].style.display.value);
                Assert.AreEqual(DisplayStyle.Flex, singles[1].style.display.value, "The row now edits the spread.");
                Assert.AreEqual(12f, singles[1].value, 0.001f);
                Assert.AreEqual(DisplayStyle.Flex, frames[1].style.display.value);
                Assert.AreEqual(10f, offset.value, 0.001f, "The value moves into the frame as the offset.");

                offset.value = 20f;
                Assert.AreEqual(20f, move.tilt.value, 0.001f);
                Assert.AreEqual(20f, singles[0].value, 0.001f, "Both sliders show the same value.");

                flags[0].value = true;
                Assert.IsTrue(move.tilt.isRange);
                Assert.AreEqual(new Vector2(0f, 45f), move.tilt.spreadRange, "A spread too small to part the thumbs opens to a quarter of the span.");
                Assert.AreEqual(DisplayStyle.Flex, ranges[1].style.display.value, "R with S ranges the spread.");
                Assert.AreEqual(DisplayStyle.None, ranges[0].style.display.value);
                Assert.AreEqual(DisplayStyle.Flex, frames[0].style.display.value);
                Assert.AreEqual(DisplayStyle.Flex, timing.style.display.value);

                // Dragging the spread range shut hides its options again.
                ranges[1].value = new Vector2(5f, 5f);
                Assert.AreEqual(DisplayStyle.None, frames[0].style.display.value);

                flags[1].value = false;
                Assert.AreEqual(DisplayStyle.Flex, ranges[0].style.display.value, "Without S, R ranges the value again.");
                Assert.AreEqual(DisplayStyle.None, frames[1].style.display.value);
                Assert.AreEqual(DisplayStyle.Flex, frames[0].style.display.value);
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ValueSlider_FillsFromZeroWhenTheLimitsSpanIt()
        {
            VisualElement Fill(AlpsValueSlider slider) => slider.Q(className: "alps-slider__fill");

            var signed = new AlpsValueSlider("signed", new Vector2(-10f, 10f));
            signed.SetValueWithoutNotify(5f);
            Assert.AreEqual(50f, Fill(signed).style.left.value.value, 0.01f, "The fill starts at zero.");
            Assert.AreEqual(25f, Fill(signed).style.right.value.value, 0.01f);

            signed.SetValueWithoutNotify(-5f);
            Assert.AreEqual(25f, Fill(signed).style.left.value.value, 0.01f, "Negative values fill to the left of zero.");
            Assert.AreEqual(50f, Fill(signed).style.right.value.value, 0.01f);

            var positive = new AlpsValueSlider("positive", new Vector2(0f, 10f));
            positive.SetValueWithoutNotify(5f);
            Assert.AreEqual(0f, Fill(positive).style.left.value.value, 0.01f, "Without negatives the fill starts at the left edge.");
            Assert.AreEqual(50f, Fill(positive).style.right.value.value, 0.01f);
        }

        [Test]
        public void ValueSlider_MarksSnapPointsInsideTheLimits()
        {
            List<float> Ticks(VisualElement slider) => slider.Query(className: "alps-slider__tick").ToList()
                .Select(t => t.style.left.value.value)
                .ToList();

            var tilt = new AlpsValueSlider("Tilt", new Vector2(-90f, 90f), "°");
            tilt.Snaps = AlpsSnapPoints.Angles(tilt.Limit);
            CollectionAssert.AreEqual(new[] { 25f, 50f, 75f }, Ticks(tilt), "45 degree steps without the limits.");

            var pan = new AlpsRangeSlider("Pan", new Vector2(-360f, 360f), "°");
            pan.Snaps = AlpsSnapPoints.Angles(pan.Limit);
            Assert.AreEqual(7, Ticks(pan).Count, "Dense angle steps widen to 90 degrees.");

            var ratio = new AlpsValueSlider("ratio", new Vector2(0f, 100f)) { Snaps = new[] { 0f, 50f, 100f } };
            CollectionAssert.AreEqual(new[] { 50f }, Ticks(ratio), "Points on the limits get no tick.");

            ratio.Limit = new Vector2(0f, 200f);
            CollectionAssert.AreEqual(new[] { 25f, 50f }, Ticks(ratio), "A wider limit brings the dropped point back.");

            CollectionAssert.IsEmpty(AlpsSnapPoints.Zero(new Vector2(0f, 1f)));
            CollectionAssert.AreEqual(new[] { 0f }, AlpsSnapPoints.Zero(new Vector2(-1f, 1f)));
        }

        [Test]
        public void SliderTrack_SnapsOnlyWithinTheThreshold()
        {
            var snaps = new[] { 0.25f, 0.5f };
            Assert.AreEqual(0.5f, AlpsSliderTrack.Snap(0.52f, snaps, 0.03f));
            Assert.AreEqual(0.25f, AlpsSliderTrack.Snap(0.23f, snaps, 0.03f));
            Assert.AreEqual(0.4f, AlpsSliderTrack.Snap(0.4f, snaps, 0.03f), "Far from every point the value is kept.");
            Assert.AreEqual(0.52f, AlpsSliderTrack.Snap(0.52f, AlpsSnapPoints.None, 0.03f));
        }

        [Test]
        public void SliderTrack_SlidesARangeWithoutChangingItsWidth()
        {
            var none = AlpsSnapPoints.None;
            AssertVector(new Vector2(0.4f, 0.6f), AlpsSliderTrack.Slide(0.2f, 0.4f, 0.4f, none, 0f), "The width is kept.");
            AssertVector(new Vector2(0f, 0.2f), AlpsSliderTrack.Slide(0.2f, 0.4f, -0.3f, none, 0f), "The low end stops at the left edge.");
            AssertVector(new Vector2(0.8f, 1f), AlpsSliderTrack.Slide(0.2f, 0.4f, 0.95f, none, 0f), "The high end stops at the right edge.");

            var snaps = new[] { 0.5f, 0.75f };
            AssertVector(new Vector2(0.5f, 0.7f), AlpsSliderTrack.Slide(0.2f, 0.4f, 0.51f, snaps, 0.03f), "The low end snaps.");
            AssertVector(new Vector2(0.55f, 0.75f), AlpsSliderTrack.Slide(0.2f, 0.4f, 0.54f, snaps, 0.03f), "The high end snaps.");
            AssertVector(new Vector2(0.5f, 0.74f), AlpsSliderTrack.Slide(0.2f, 0.44f, 0.49f, snaps, 0.03f), "The smaller shift wins.");
        }

        private static void AssertVector(Vector2 expected, Vector2 actual, string message)
        {
            Assert.AreEqual(expected.x, actual.x, 0.0001f, message);
            Assert.AreEqual(expected.y, actual.y, 0.0001f, message);
        }

        [Test]
        public void Sliders_ResetToTheirDefaultValue()
        {
            var single = new AlpsValueSlider("single", new Vector2(0f, 10f));
            single.SetValueWithoutNotify(7f);
            single.ResetToDefault();
            Assert.AreEqual(7f, single.value, "Without a default the value stays.");

            single.DefaultValue = 3f;
            single.ResetToDefault();
            Assert.AreEqual(3f, single.value);

            var range = new AlpsRangeSlider("range", new Vector2(0f, 10f)) { DefaultValue = new Vector2(2f, 8f) };
            range.SetValueWithoutNotify(new Vector2(4f, 6f));
            range.ResetToDefault(AlpsSliderTrack.ThumbLow);
            Assert.AreEqual(new Vector2(2f, 6f), range.value, "Only the clicked end comes back.");
            range.ResetToDefault(AlpsSliderTrack.ThumbHigh);
            Assert.AreEqual(new Vector2(2f, 8f), range.value);

            range.SetValueWithoutNotify(new Vector2(0f, 1f));
            range.ResetToDefault(AlpsSliderTrack.ThumbLow);
            Assert.AreEqual(new Vector2(2f, 8f), range.value, "An end that would pass the other brings back the whole range.");
        }

        [UnityTest]
        public IEnumerator EffectSliders_ResetToTheValuesOfAFreshEffect()
        {
            // ChangeEvent only fires on a panel, so this runs inside a real window.
            var set = new AlpsClipEffectSet();
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.isRange = true;
            move.tilt.range = new Vector2(-10f, 10f);
            move.panTiltPhaseOffsetDegrees = 180f;

            var window = ScriptableObject.CreateInstance<PanelHostWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            try
            {
                var view = new AlpsClipInspectorView(set);
                window.rootVisualElement.Add(view);
                yield return null;

                var tilt = view.Query<AlpsRangeSlider>().ToList().First(s => s.label == "Tilt");
                tilt.ResetToDefault(AlpsSliderTrack.ThumbHigh);
                Assert.AreEqual(new Vector2(-10f, 35f), move.tilt.range, "The high end returns to the Move default.");

                var offset = view.Query<AlpsValueSlider>().ToList().First(s => s.label == "位相差");
                offset.ResetToDefault();
                Assert.AreEqual(90f, move.panTiltPhaseOffsetDegrees, 0.001f);
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RangeToggle_SwapsSingleSliderForRangeSlider()
        {
            var set = new AlpsClipEffectSet();
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.isRange = false;

            var view = new AlpsClipInspectorView(set);
            var animatable = view.Query<AlpsAnimatableView>().First();
            var single = animatable.Query<AlpsValueSlider>().ToList().First(s => s.label == "Tilt");
            var ranged = animatable.Query<AlpsRangeSlider>().ToList().First(s => s.label == "Tilt");

            Assert.AreEqual(DisplayStyle.Flex, single.style.display.value);
            Assert.AreEqual(DisplayStyle.None, ranged.style.display.value);

            move.tilt.isRange = true;
            animatable.Refresh();

            Assert.AreEqual(DisplayStyle.None, single.style.display.value);
            Assert.AreEqual(DisplayStyle.Flex, ranged.style.display.value);
        }

        [Test]
        public void AddingAnExistingEffect_SplitsItIntoEvenAndOdd()
        {
            var set = new AlpsClipEffectSet();

            Assert.AreEqual("カラー", set.GetAddLabel(AlpsEffectKind.Color));
            var first = set.Add(AlpsEffectKind.Color);
            Assert.AreEqual(AlpsParity.All, first.parity);

            Assert.AreEqual("カラー" + AlpsEffectCatalog.OddSuffix, set.GetAddLabel(AlpsEffectKind.Color));
            var odd = set.Add(AlpsEffectKind.Color);

            Assert.AreEqual(AlpsParity.Even, first.parity, "The existing card must become （偶数）.");
            Assert.AreEqual(AlpsParity.Odd, odd.parity);
            Assert.AreEqual(1, set.effects.IndexOf(odd), "（奇数） must be inserted directly below （偶数）.");

            // Properties are identical across the pair at the moment of the split.
            Assert.AreEqual(first.colorStops.Count, odd.colorStops.Count);

            Assert.IsNull(set.GetAddLabel(AlpsEffectKind.Color), "A fully split kind leaves the add list.");
            Assert.IsFalse(set.CanAdd(AlpsEffectKind.Color));
        }

        [Test]
        public void Effects_StayInCatalogOrderWhateverOrderTheyAreAdded()
        {
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Gobo);
            set.Add(AlpsEffectKind.Brightness);
            set.Add(AlpsEffectKind.Cone);
            set.Add(AlpsEffectKind.Move);
            set.Add(AlpsEffectKind.Flicker);
            set.Add(AlpsEffectKind.Color);
            set.Add(AlpsEffectKind.Color);
            set.Add(AlpsEffectKind.Move);

            CollectionAssert.AreEqual(
                new[]
                {
                    "ムーブ（偶数）", "ムーブ（奇数）", "カラー（偶数）", "カラー（奇数）",
                    "明るさ", "コーン", "フリッカー", "ゴボ",
                },
                set.effects.Select(e => AlpsEffectCatalog.GetTitle(e.kind, e.parity)).ToArray());
        }

        [Test]
        public void SortEffects_NormalizesAStackSavedInAddOrder()
        {
            var set = new AlpsClipEffectSet();
            set.effects.Add(new AlpsEffect { kind = AlpsEffectKind.Cone });
            set.effects.Add(new AlpsEffect { kind = AlpsEffectKind.Color, parity = AlpsParity.Odd });
            set.effects.Add(new AlpsEffect { kind = AlpsEffectKind.Color, parity = AlpsParity.Even });
            set.effects.Add(new AlpsEffect { kind = AlpsEffectKind.Move });

            set.OnAfterDeserialize();

            CollectionAssert.AreEqual(
                new[] { "ムーブ", "カラー（偶数）", "カラー（奇数）", "コーン" },
                set.effects.Select(e => AlpsEffectCatalog.GetTitle(e.kind, e.parity)).ToArray());
        }

        [Test]
        public void RemovingOneHalfOfAPair_PromotesTheSurvivor()
        {
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Gobo);
            set.Add(AlpsEffectKind.Gobo);

            set.Remove(set.IndexOf(AlpsEffectKind.Gobo, AlpsParity.Odd));

            Assert.AreEqual(1, set.effects.Count);
            Assert.AreEqual(AlpsParity.All, set.effects[0].parity);
            Assert.AreEqual("ゴボ" + AlpsEffectCatalog.OddSuffix, set.GetAddLabel(AlpsEffectKind.Gobo));
        }

        [Test]
        public void AddCatalog_ListsOnlyAddableKinds()
        {
            var set = new AlpsClipEffectSet();
            set.addCatalogExpanded = true;
            set.Add(AlpsEffectKind.Color);
            set.Add(AlpsEffectKind.Color);

            var catalog = new AlpsAddEffectCatalog(set, _ => { });
            var texts = Labels(catalog).Select(label => label.text).ToList();

            Assert.IsTrue(texts.Contains("ムーブ"), "An unused kind is listed unsuffixed.");
            Assert.IsFalse(
                texts.Any(text => text.StartsWith("カラー")),
                "A kind split into both parities must leave the add list.");
        }

        [Test]
        public void MoveEffect_ShowsPhaseOffsetOnlyWhenBothAxesAreRanges()
        {
            var set = new AlpsClipEffectSet();
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.isRange = true;
            move.pan.isRange = false;

            var view = new AlpsClipInspectorView(set);
            var offset = view.Query<AlpsValueSlider>().ToList().First(s => s.label == "位相差");
            Assert.AreEqual(DisplayStyle.None, offset.style.display.value);

            move.pan.isRange = true;
            view.Query<AlpsEffectView>().First().Refresh();
            Assert.AreEqual(DisplayStyle.Flex, offset.style.display.value);
        }

        [Test]
        public void MoveEffect_TabSwitchesBetweenAngleCircleAndUserTracking()
        {
            var set = new AlpsClipEffectSet();
            var move = set.Add(AlpsEffectKind.Move);
            var view = new AlpsClipInspectorView(set);

            AlpsValueSlider Row(string label) => view.Query<AlpsValueSlider>().ToList().First(s => s.label == label);

            var tilt = Row("Tilt");
            var radius = Row("半径");
            var follow = Row("追従速度");

            Assert.IsTrue(IsShown(tilt), "角度指定 must show the Tilt row.");
            Assert.IsFalse(IsShown(radius), "半径 belongs to the 円 tab only.");
            Assert.IsFalse(IsShown(follow), "追従速度 belongs to the ユーザー追跡 tab only.");

            move.moveMode = AlpsMoveMode.Circle;
            view.Query<AlpsEffectView>().First().Refresh();

            Assert.IsFalse(IsShown(tilt), "Switching to 円 must hide the angle rows.");
            Assert.IsTrue(IsShown(Row("中心 Tilt")));
            Assert.IsTrue(IsShown(Row("半径")));
            Assert.IsFalse(IsShown(Row("追従速度")));

            move.moveMode = AlpsMoveMode.TrackUser;
            view.Query<AlpsEffectView>().First().Refresh();

            Assert.IsFalse(IsShown(Row("Tilt")), "Switching to ユーザー追跡 must hide the angle rows.");
            Assert.IsFalse(IsShown(Row("半径")));
            Assert.IsTrue(IsShown(Row("追従速度")));
        }

        [UnityTest]
        public IEnumerator MoveEffect_CircleRowsWriteToTheModel()
        {
            // ChangeEvent only fires on a panel, so this runs inside a real window.
            var set = new AlpsClipEffectSet();
            var move = set.Add(AlpsEffectKind.Move);
            move.moveMode = AlpsMoveMode.Circle;

            var window = ScriptableObject.CreateInstance<PanelHostWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            try
            {
                var view = new AlpsClipInspectorView(set);
                window.rootVisualElement.Add(view);
                yield return null;

                AlpsValueSlider Row(string label) => view.Query<AlpsValueSlider>().ToList().First(s => s.label == label);

                Row("中心 Tilt").value = 33f;
                Row("中心 Pan").value = -15f;
                Row("半径").value = 8f;
                Row("縦横比").value = 2f;

                Assert.AreEqual(33f, move.circleCenterTilt.value, 0.001f);
                Assert.AreEqual(-15f, move.circleCenterPan.value, 0.001f);
                Assert.AreEqual(8f, move.circleRadius.value, 0.001f);
                Assert.AreEqual(2f, move.circleAspect, 0.001f);
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ValueSpreadAndPhaseSpread_AcceptNegativeValues()
        {
            var cone = new AlpsAnimatableView("幅", new AlpsAnimatableValue(5f, new Vector2(0f, 40f)), new AlpsPhaseSettings(), null);
            var spread = cone.Query<AlpsValueSlider>().ToList().Where(s => s.label == "幅").ElementAt(1);
            Assert.AreEqual(-40f, spread.Limit.x, 0.001f, "A negative spread fans the other way.");
            Assert.AreEqual(40f, spread.Limit.y, 0.001f, "Both directions reach as far as the value's span.");

            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Move);
            var view = new AlpsClipInspectorView(set);
            var sliders = view.Query<AlpsValueSlider>().ToList();

            var phaseSpread = sliders.First(s => s.label == "ディレイ");
            Assert.AreEqual(-200f, phaseSpread.Limit.x, 0.001f, "A negative spread runs the order backwards.");
            Assert.AreEqual(200f, phaseSpread.Limit.y, 0.001f, "Two trips across the group is as far as a wave reads.");
        }

        [Test]
        public void CopyButton_CopiesParametersInsteadOfDuplicatingTheCard()
        {
            var set = new AlpsClipEffectSet();
            var even = set.Add(AlpsEffectKind.Cone);
            even.coneWidth.range = new Vector2(3f, 45f);
            even.coneWidth.isRange = true;

            var odd = set.Add(AlpsEffectKind.Cone);
            Assert.AreEqual(2, set.effects.Count, "偶数 / 奇数 is the only legal way a kind repeats.");
            Assert.AreEqual(AlpsParity.Odd, odd.parity);

            odd.coneWidth.range = new Vector2(0f, 1f);
            odd.coneWidth.isRange = false;

            var view = new AlpsClipInspectorView(set);
            var cards = view.Query<AlpsEffectView>().ToList();

            cards[0].Card.RequestCopy();
            Assert.AreEqual(2, set.effects.Count, "コピー must not add a card.");

            cards[1].Refresh();
            cards[1].Card.RequestPaste();

            var pasted = set.effects[1];
            Assert.AreEqual(2, set.effects.Count, "貼り付け replaces parameters, it does not insert.");
            Assert.AreEqual(new Vector2(3f, 45f), pasted.coneWidth.range);
            Assert.IsTrue(pasted.coneWidth.isRange);
            Assert.AreEqual(AlpsParity.Odd, pasted.parity, "貼り付け keeps the card's own identity.");
            Assert.AreEqual(AlpsEffectKind.Cone, pasted.kind);
        }

        [Test]
        public void Clipboard_OffersPasteOnlyForTheSameKind()
        {
            var cone = AlpsEffect.Create(AlpsEffectKind.Cone);
            var color = AlpsEffect.Create(AlpsEffectKind.Color);

            Assert.IsFalse(AlpsEffectClipboard.CanPasteInto(cone), "Nothing has been copied yet.");

            AlpsEffectClipboard.Copy(cone);
            Assert.IsTrue(AlpsEffectClipboard.CanPasteInto(cone));
            Assert.IsFalse(AlpsEffectClipboard.CanPasteInto(color));
            Assert.IsNull(AlpsEffectClipboard.Paste(color));
        }

        [Test]
        public void SharedSettings_CopyAndPasteAcrossClips()
        {
            var source = new AlpsClipEffectSet();
            source.phase.mode = AlpsPhaseMode.Random;
            source.phase.spread = -0.25f;
            source.phase.beatsPerCycle = 8f;

            var target = new AlpsClipEffectSet();
            target.Add(AlpsEffectKind.Brightness);

            var sourceCard = new AlpsClipInspectorView(source).Query<AlpsEffectCard>().First();
            var targetView = new AlpsClipInspectorView(target);
            var targetCard = targetView.Query<AlpsEffectCard>().First();
            Assert.AreEqual("共通設定", targetCard.Title);
            Assert.IsFalse(targetCard.PasteAvailable, "貼り付け must be hidden with an empty clipboard.");

            var effect = AlpsEffect.Create(AlpsEffectKind.Cone);
            AlpsEffectClipboard.Copy(effect);
            sourceCard.RequestCopy();
            Assert.IsTrue(AlpsEffectClipboard.CanPasteInto(effect), "Copying shared settings keeps a copied effect.");

            var refreshed = new AlpsClipInspectorView(target);
            var refreshedCard = refreshed.Query<AlpsEffectCard>().First();
            Assert.IsTrue(refreshedCard.PasteAvailable);

            var changed = 0;
            refreshed.Changed += () => changed++;
            refreshedCard.RequestPaste();

            Assert.Greater(changed, 0, "貼り付け reports the edit so the host records undo.");
            Assert.AreNotSame(source.phase, target.phase, "貼り付け copies the values, not the instance.");
            Assert.AreEqual(AlpsPhaseMode.Random, target.phase.mode);
            Assert.AreEqual(-0.25f, target.phase.spread, 0.0001f);
            Assert.AreEqual(8f, target.phase.beatsPerCycle, 0.0001f);
            Assert.AreEqual(1, target.effects.Count, "貼り付け leaves the effects alone.");
            Assert.IsFalse(HasVisibleText(refreshed, "イージング"), "The rebuilt view shows the pasted mode.");
        }

        [Test]
        public void PasteButton_AppearsOnlyWhileACompatiblePayloadExists()
        {
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Cone);
            set.Add(AlpsEffectKind.Brightness);

            var view = new AlpsClipInspectorView(set);
            var cards = view.Query<AlpsEffectView>().ToList();

            foreach (var card in cards)
            {
                Assert.IsFalse(card.Card.PasteAvailable, "貼り付け must be hidden with an empty clipboard.");
            }

            var cone = set.IndexOf(AlpsEffectKind.Cone, AlpsParity.All);
            var brightness = set.IndexOf(AlpsEffectKind.Brightness, AlpsParity.All);
            AlpsEffectClipboard.Copy(set.effects[cone]);
            var refreshed = new AlpsClipInspectorView(set).Query<AlpsEffectView>().ToList();

            Assert.IsTrue(refreshed[cone].Card.PasteAvailable, "コーン accepts コーン parameters.");
            Assert.IsFalse(refreshed[brightness].Card.PasteAvailable, "明るさ must not accept them.");
        }

        [Test]
        public void Palettes_StartEmptyWithOnlyTheActionTiles()
        {
            // A pre-filled palette would animate the moment the effect is added.
            Assert.AreEqual(0, AlpsEffect.Create(AlpsEffectKind.Color).colorStops.Count);
            Assert.AreEqual(0, AlpsEffect.Create(AlpsEffectKind.Gobo).goboStops.Count);

            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Color);
            set.Add(AlpsEffectKind.Gobo);
            var view = new AlpsClipInspectorView(set);

            foreach (var palette in new VisualElement[] { view.Q<AlpsColorPalette>(), view.Q<AlpsGoboPalette>() })
            {
                var swatches = palette.Query(className: "alps-swatch").ToList();
                Assert.AreEqual(2, swatches.Count, "Only ＋ and 🗑 on an empty palette.");
                Assert.IsTrue(swatches.All(s => s.ClassListContains("alps-swatch--action")));
                Assert.IsTrue(swatches[1].ClassListContains("alps-swatch--disabled"), "🗑 has nothing to delete.");
                Assert.AreEqual(DisplayStyle.None, palette.Q(className: "alps-palette__editor").style.display.value);
            }

            Assert.IsFalse(view.Q<AlpsColorPalette>().CanDelete);
            var timings = view.Query<AlpsAnimatableView>().ToList()
                .SelectMany(v => v.Query<AlpsSegmentedControl>().ToList())
                .Where(c => c.label == "タイミング")
                .ToList();
            Assert.IsNotEmpty(timings);
            Assert.IsTrue(timings.All(t => !IsShown(t)), "Nothing to phase with no stops.");
        }

        [Test]
        public void ColorPalette_EditsTheSelectedStopWithATypeDropdownAndAPicker()
        {
            var set = new AlpsClipEffectSet();
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.selectedColorStop = 0;

            var view = new AlpsClipInspectorView(set);
            var editor = view.Q(className: "alps-palette__editor");
            Assert.IsNotNull(editor, "The selected stop needs an editor row.");

            var type = editor.Q<DropdownField>(className: "alps-palette__type");
            Assert.IsNotNull(type, "種類 must be a dropdown, not a hidden overlay.");
            Assert.AreEqual("単色", type.value);
            Assert.IsNotNull(editor.Q<ColorField>(), "単色 shows a colour picker.");
            Assert.IsNull(editor.Q<GradientField>());

            view.Q<AlpsColorPalette>().ChangeSelectedStopType(true);

            Assert.IsTrue(color.colorStops[0].isGradient);
            var swapped = view.Q(className: "alps-palette__editor");
            Assert.IsNotNull(swapped.Q<GradientField>(), "グラデーション shows the gradient editor.");
            Assert.IsNull(swapped.Q<ColorField>());
        }


        private static AlpsColorPalette BuildColorPalette(out AlpsEffect color)
        {
            var set = new AlpsClipEffectSet();
            color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Clear();
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(new AlpsColorStop(Color.green));
            color.colorStops.Add(new AlpsColorStop(Color.blue));
            color.selectedColorStop = 1;
            return new AlpsClipInspectorView(set).Q<AlpsColorPalette>();
        }

        private static AlpsGoboPalette BuildGoboPalette(out AlpsEffect gobo)
        {
            var set = new AlpsClipEffectSet();
            gobo = set.Add(AlpsEffectKind.Gobo);
            return new AlpsClipInspectorView(set).Q<AlpsGoboPalette>();
        }

        [Test]
        public void ColorPlus_DuplicatesTheSelectionRightAfterIt()
        {
            var set = new AlpsClipEffectSet();
            var empty = set.Add(AlpsEffectKind.Color);
            var first = new AlpsClipInspectorView(set).Q<AlpsColorPalette>();

            first.RequestAdd();
            Assert.AreEqual(1, empty.colorStops.Count);
            Assert.AreEqual(Color.white, empty.colorStops[0].color, "An empty palette has nothing to copy, so it starts from white.");
            Assert.AreEqual(0, first.SelectedIndex);
            Assert.AreEqual(DisplayStyle.Flex, first.Q(className: "alps-palette__editor").style.display.value,
                "The added stop is selected, so its picker is right there.");

            var palette = BuildColorPalette(out var color);
            palette.RequestAdd();

            CollectionAssert.AreEqual(
                new[] { Color.red, Color.green, Color.green, Color.blue },
                color.colorStops.Select(stop => stop.color).ToArray(),
                "With a selection, + copies it.");
            Assert.AreEqual(2, color.selectedColorStop, "The new stop becomes the selection.");

            color.colorStops[2].color = Color.yellow;
            Assert.AreEqual(Color.green, color.colorStops[1].color, "The copy is edited on its own.");
        }

        [UnityTest]
        public IEnumerator ColorPalette_KeepsItsPickerFieldsAliveAcrossEdits()
        {
            // The colour / gradient picker windows stay bound to the field that opened
            // them. If an edit replaced that field, every later change would be lost.
            // ChangeEvent only fires on a panel, so this runs inside a real window.
            var set = new AlpsClipEffectSet();
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(BuildTestGradientStop());
            color.selectedColorStop = 0;

            var window = ScriptableObject.CreateInstance<PanelHostWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            try
            {
                var view = new AlpsClipInspectorView(set);
                window.rootVisualElement.Add(view);
                yield return null;

                var palette = view.Q<AlpsColorPalette>();
                var field = palette.Q<ColorField>();
                foreach (var next in new[] { Color.green, Color.blue, Color.yellow })
                {
                    field.value = next;
                    Assert.AreEqual(next, color.colorStops[0].color, "Every picker change must reach the model.");
                    Assert.AreSame(field, palette.Q<ColorField>(), "The field the picker is bound to was replaced.");
                    Assert.AreEqual(next, palette.Query(className: "alps-swatch").First().style.backgroundColor.value,
                        "The swatch must follow the edit.");
                }

                palette.Select(1);
                var gradientField = palette.Q<GradientField>();
                for (var i = 0; i < 3; i++)
                {
                    var gradient = new Gradient();
                    var tint = new[] { Color.cyan, Color.magenta, Color.gray }[i];
                    gradient.SetKeys(
                        new[] { new GradientColorKey(tint, 0f), new GradientColorKey(Color.black, 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

                    gradientField.value = gradient;
                    Assert.AreEqual(tint, color.colorStops[1].gradient.Evaluate(0f));
                    Assert.AreSame(gradientField, palette.Q<GradientField>(), "The gradient field was replaced.");
                }
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        private static AlpsColorStop BuildTestGradientStop()
        {
            var stop = new AlpsColorStop(Color.white) { isGradient = true, gradient = new Gradient() };
            stop.gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return stop;
        }

        private sealed class PanelHostWindow : EditorWindow
        {
        }

        [Test]
        public void PaletteDuplicate_CopiesTheSelectedStopRightAfterIt()
        {
            var palette = BuildColorPalette(out var color);

            palette.DuplicateSelected();

            CollectionAssert.AreEqual(
                new[] { Color.red, Color.green, Color.green, Color.blue },
                color.colorStops.Select(stop => stop.color).ToArray());
            Assert.AreEqual(2, color.selectedColorStop, "The copy becomes the selection.");
            Assert.AreNotSame(color.colorStops[1], color.colorStops[2], "The copy must be editable on its own.");

            color.colorStops[2].color = Color.yellow;
            Assert.AreEqual(Color.green, color.colorStops[1].color);
        }

        [Test]
        public void GoboPlus_TogglesAPickerThatAddsAfterTheSelection()
        {
            var palette = BuildGoboPalette(out var gobo);
            Assert.IsNull(palette.Q(className: "alps-gobo-picker"), "The picker starts closed.");

            palette.RequestAdd();
            Assert.IsTrue(gobo.goboPickerExpanded);
            Assert.AreEqual(0, gobo.goboStops.Count, "Opening the picker adds nothing by itself.");

            var plus = palette.Query(className: "alps-swatch--action").First();
            Assert.IsTrue(plus.ClassListContains("alps-swatch--active"), "＋ shows that its picker is open.");

            var picker = palette.Q(className: "alps-gobo-picker");
            Assert.IsNotNull(picker);
            var tiles = picker.Query(className: "alps-swatch").ToList();
            Assert.AreEqual(AlpsGoboLibrary.GoboCount, tiles.Count, "OFF plus every patterned gobo.");
            Assert.IsTrue(palette.CatalogStops[0].IsOff);
            Assert.AreEqual(AlpsGoboLibrary.FirstPatternIndex, palette.CatalogStops[1].goboIndex);

            // OFF / gobo 4 / OFF: a blink sequence needs OFF more than once.
            var builtIn = palette.CatalogStops;
            palette.AddStop(new AlpsGoboStop(builtIn[0]));
            palette.AddStop(new AlpsGoboStop(builtIn[3]));
            palette.AddStop(new AlpsGoboStop(builtIn[0]));
            Assert.AreEqual(3, gobo.goboStops.Count);
            Assert.IsTrue(gobo.goboStops[0].IsOff);
            Assert.AreEqual(builtIn[3].goboIndex, gobo.goboStops[1].goboIndex);
            Assert.IsTrue(gobo.goboStops[2].IsOff);
            Assert.AreEqual(2, gobo.selectedGoboStop);

            // Adding goes right after the selection, not to the end.
            palette.Select(0);
            palette.AddStop(new AlpsGoboStop(builtIn[5]));
            Assert.AreEqual(builtIn[5].goboIndex, gobo.goboStops[1].goboIndex);
            Assert.AreEqual(1, gobo.selectedGoboStop);

            Assert.IsNotNull(palette.Q(className: "alps-gobo-picker"), "The picker stays open between additions.");

            palette.RequestAdd();
            Assert.IsFalse(gobo.goboPickerExpanded);
            Assert.IsNull(palette.Q(className: "alps-gobo-picker"));
        }

        [Test]
        public void GoboOff_IsAnOrdinaryStop()
        {
            var palette = BuildGoboPalette(out var gobo);
            gobo.goboStops.Add(new AlpsGoboStop());
            gobo.goboStops.Add(new AlpsGoboStop(palette.CatalogStops[1]));
            palette.Select(0);

            palette.DuplicateSelected();
            Assert.AreEqual(3, gobo.goboStops.Count);
            Assert.IsTrue(gobo.goboStops[0].IsOff && gobo.goboStops[1].IsOff);

            palette.Reorder(1, 2);
            Assert.IsTrue(gobo.goboStops[2].IsOff);
            Assert.AreEqual(2, gobo.selectedGoboStop);

            Assert.IsTrue(palette.CanDelete);
            palette.DeleteSelected();
            palette.Select(0);
            palette.DeleteSelected();
            Assert.AreEqual(1, gobo.goboStops.Count, "Both OFF stops could be deleted.");
            Assert.IsFalse(gobo.goboStops[0].IsOff);
        }

        [Test]
        public void PaletteTrash_DeletesDownToEmpty()
        {
            var palette = BuildColorPalette(out var color);

            palette.DeleteSelected();
            CollectionAssert.AreEqual(
                new[] { Color.red, Color.blue },
                color.colorStops.Select(stop => stop.color).ToArray());
            Assert.AreEqual(1, color.selectedColorStop, "The selection lands on the stop that took its place.");

            palette.DeleteSelected();
            Assert.AreEqual(1, color.colorStops.Count);
            Assert.AreEqual(0, color.selectedColorStop, "Deleting the last position selects the new last stop.");

            Assert.IsTrue(palette.CanDelete, "The last stop can go too, since an empty palette is OFF.");
            palette.DeleteSelected();
            Assert.AreEqual(0, color.colorStops.Count);
            Assert.AreEqual(-1, palette.SelectedIndex);
            Assert.AreEqual(DisplayStyle.None, palette.Q(className: "alps-palette__editor").style.display.value);

            Assert.IsFalse(palette.CanDelete);
            palette.DeleteSelected();

            var trash = palette.Query(className: "alps-swatch--action").ToList().Last();
            Assert.IsTrue(trash.ClassListContains("alps-swatch--disabled"), "🗑 must look disabled when empty.");
        }

        [Test]
        public void PaletteReorder_MovesTheStopAndTheSelectionFollowsIt()
        {
            var palette = BuildColorPalette(out var color);

            palette.Reorder(0, 2);

            CollectionAssert.AreEqual(
                new[] { Color.green, Color.blue, Color.red },
                color.colorStops.Select(stop => stop.color).ToArray());
            Assert.AreEqual(2, color.selectedColorStop);

            var swatches = palette.Query(className: "alps-swatch").ToList()
                .Where(e => !e.ClassListContains("alps-swatch--action")).ToList();
            Assert.AreEqual(Color.red, swatches[2].style.backgroundColor.value, "The strip must be rebuilt in the new order.");
            Assert.IsTrue(swatches[2].ClassListContains("alps-swatch--selected"));
        }

        [Test]
        public void PaletteDrag_FindsTheSlotInReadingOrderAcrossWrappedRows()
        {
            // Two rows of 28px tiles on a 32px pitch.
            var others = new List<Rect>
            {
                new Rect(0, 0, 28, 28), new Rect(32, 0, 28, 28), new Rect(64, 0, 28, 28),
                new Rect(0, 32, 28, 28), new Rect(32, 32, 28, 28),
            };

            Assert.AreEqual(0, AlpsColorPalette.SlotAt(others, new Vector2(5, 14)), "Left half of the first tile.");
            Assert.AreEqual(1, AlpsColorPalette.SlotAt(others, new Vector2(20, 14)), "Right half of the first tile.");
            Assert.AreEqual(3, AlpsColorPalette.SlotAt(others, new Vector2(200, 14)), "Past the end of row one.");
            Assert.AreEqual(3, AlpsColorPalette.SlotAt(others, new Vector2(2, 40)), "Start of row two.");
            Assert.AreEqual(5, AlpsColorPalette.SlotAt(others, new Vector2(200, 40)), "Past the end of the strip.");
            Assert.AreEqual(0, AlpsColorPalette.SlotAt(others, new Vector2(100, -10)), "Above the strip.");
            Assert.AreEqual(5, AlpsColorPalette.SlotAt(others, new Vector2(0, 200)), "Below the strip.");
        }

        [Test]
        public void VectorIcon_ParsesTablerPathsIncludingArcs()
        {
            var contours = new List<List<Vector2>>();
            var closed = new List<bool>();

            foreach (var path in AlpsIcons.Copy)
            {
                AlpsSvgPath.Flatten(path, contours, closed);
            }

            Assert.AreEqual(AlpsIcons.Copy.Length, contours.Count, "Each subpath must produce one contour.");
            Assert.IsTrue(contours.All(c => c.Count >= 2));
            Assert.IsTrue(
                contours.SelectMany(c => c).All(p => !float.IsNaN(p.x) && !float.IsNaN(p.y)),
                "Arc flattening produced a NaN.");

            // The copy icon lives inside a 24x24 view box.
            var all = contours.SelectMany(c => c).ToList();
            Assert.GreaterOrEqual(all.Min(p => p.x), -1f);
            Assert.LessOrEqual(all.Max(p => p.x), 25f);
            Assert.GreaterOrEqual(all.Min(p => p.y), -1f);
            Assert.LessOrEqual(all.Max(p => p.y), 25f);
        }

        [Test]
        public void EasingThumbnails_MatchTheRuntimeCurves()
        {
            // The grid draws AlpsEase directly, so the contract worth pinning is that the
            // curve library is anchored at both ends for every curve.
            foreach (AlpsEaseType type in System.Enum.GetValues(typeof(AlpsEaseType)))
            {
                Assert.AreEqual(0f, AlpsEase.Evaluate(type, 0f), 0.001f, $"{type} must start at 0.");
                Assert.AreEqual(1f, AlpsEase.Evaluate(type, 1f), 0.001f, $"{type} must end at 1.");
            }
        }

        [Test]
        public void PhaseSettings_PingPongRatioMovesThePeak()
        {
            // 往復比 0.25 puts the triangle's peak a quarter of the way into the cycle.
            float Phase(float cycles) => AlpsShowEvaluator.Phase(
                AlpsShowEvaluator.PhasePingPong, (int)AlpsEaseType.Linear, 0.25f, false, cycles, 0, 0);

            Assert.AreEqual(1f, Phase(0.25f), 0.01f, "The peak sits at 往復比.");
            Assert.AreEqual(0.5f, Phase(0.125f), 0.01f);
            Assert.AreEqual(0.5f, Phase(0.625f), 0.01f);
        }

        [Test]
        public void PhaseSettings_FixtureGroupSizeBundlesNeighbours()
        {
            // A fixture group size of 2 means fixtures 0 and 1 share a phase, 2 and 3 share the next.
            int Position(int fixture) => AlpsShowEvaluator.OrderPosition(AlpsShowEvaluator.OrderNormal, 0, fixture, 4, 2);

            Assert.AreEqual(Position(0), Position(1));
            Assert.AreNotEqual(Position(0), Position(2));
        }
    }
}
