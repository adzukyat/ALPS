using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ManeuverForVRC.Ui;
using ManeuverForVRC.Ui.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ManeuverForVRC.Tests
{
    /// <summary>
    /// Covers the clip inspector: that the whole tree builds, that the conditional
    /// visibility rules hold, and that the odd / even split behaves as intended.
    /// </summary>
    public class MfvClipInspectorViewUiTests
    {
        private static MfvClipEffectSet BuildFullSet()
        {
            var set = new MfvClipEffectSet();
            foreach (var kind in MfvEffectCatalog.Order)
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
            MfvEffectClipboard.Clear();
        }

        [Test]
        public void CustomEditor_BuildsTheInspectorAndResolvesTheStyleSheet()
        {
            var asset = ScriptableObject.CreateInstance<MfvClipEffectSetAsset>();
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.data = BuildFullSet();

            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(asset);
                Assert.IsInstanceOf<MfvClipEffectSetAssetEditor>(
                    inspector,
                    "MfvClipEffectSetAsset must use MfvClipEffectSetAssetEditor.");

                var host = inspector.CreateInspectorGUI();
                Assert.NotNull(host);

                // The host is a rebuild container. The styled tree is the view inside it.
                var view = host.Q<MfvClipInspectorView>();
                Assert.NotNull(view, "The inspector did not build a MfvClipInspectorView.");
                Assert.AreEqual(
                    1,
                    view.styleSheets.count,
                    "MfvInspector.uss did not resolve, so the inspector would render unstyled.");

                // The style tokens hang off this class.
                Assert.IsTrue(view.ClassListContains("mfv-root"));
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
            const string folder = "Assets/MfvUndoTest";
            const string path = folder + "/UndoSet.asset";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "MfvUndoTest");
            }

            var asset = ScriptableObject.CreateInstance<MfvClipEffectSetAsset>();
            asset.data = new MfvClipEffectSet();
            asset.data.Add(MfvEffectKind.Brightness);
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
                var view = host.Q<MfvClipInspectorView>();
                Assert.NotNull(view, "The inspector did not rebuild after undo.");

                var slider = view.Query<MfvValueSlider>().ToList().First(s => s.label == "明るさ");
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
            var view = new MfvClipInspectorView(set);

            Assert.AreEqual(MfvEffectCatalog.Order.Length, set.effects.Count);
            Assert.IsTrue(HasVisibleText(view, "共通設定"));
            Assert.IsTrue(HasVisibleText(view, "並び順"));
            Assert.IsTrue(HasVisibleText(view, "プロファイル"));

            foreach (var kind in MfvEffectCatalog.Order)
            {
                Assert.IsTrue(
                    HasVisibleText(view, MfvEffectCatalog.GetName(kind)),
                    $"Card for {kind} was not rendered.");
            }
        }

        [Test]
        public void SharedSettings_RendersEveryPlannedRow()
        {
            var set = new MfvClipEffectSet();
            set.phase.mode = MfvPhaseMode.PingPong;
            var view = new MfvClipInspectorView(set);

            foreach (var row in new[] { "モード", "イージング", "往復比", "灯体単位", "ディレイ", "速度", "反転" })
            {
                Assert.IsTrue(HasVisibleText(view, row), $"共通設定 is missing the {row} row.");
            }
        }

        [Test]
        public void PingPongRatio_IsOnlyShownForPingPong()
        {
            var set = new MfvClipEffectSet();
            set.phase.mode = MfvPhaseMode.Forward;
            var view = new MfvClipInspectorView(set);

            var ratio = view.Query<MfvValueSlider>().ToList().First(s => s.label == "往復比");
            Assert.AreEqual(DisplayStyle.None, ratio.style.display.value, "往復比 must be hidden outside ピンポン.");

            set.phase.mode = MfvPhaseMode.PingPong;
            view.Query<MfvPhaseSettingsView>().First().Refresh();
            Assert.AreEqual(DisplayStyle.Flex, ratio.style.display.value);
        }

        [Test]
        public void RandomMode_HidesEasingAndInverse()
        {
            var set = new MfvClipEffectSet();
            set.phase.mode = MfvPhaseMode.Random;
            var view = new MfvClipInspectorView(set);

            var easing = view.Query<MfvEasingGrid>().First();
            var inverse = view.Query<MfvToggleSwitch>().ToList().First(t => t.label == "反転");

            Assert.AreEqual(DisplayStyle.None, easing.style.display.value, "イージング must be hidden in ランダム.");
            Assert.AreEqual(DisplayStyle.None, inverse.style.display.value, "反転 must be hidden in ランダム.");
        }

        [Test]
        public void TimingAndOwnMotion_AppearOnlyWithTwoOrMoreStops()
        {
            var set = new MfvClipEffectSet();
            var move = set.Add(MfvEffectKind.Move);
            move.tilt.isRange = false;

            var view = new MfvClipInspectorView(set);
            var timing = view.Query<MfvSegmentedControl>().ToList()
                .First(c => c.label == "タイミング");

            Assert.AreEqual(
                DisplayStyle.None,
                timing.style.display.value,
                "タイミング must stay hidden while the parameter is a single value.");

            move.tilt.isRange = true;
            move.tilt.range = new Vector2(-40f, 35f);
            view.Query<MfvAnimatableView>().First().Refresh();

            Assert.AreEqual(DisplayStyle.Flex, timing.style.display.value);
        }

        [Test]
        public void RangeToggle_SwapsSingleSliderForRangeSlider()
        {
            var set = new MfvClipEffectSet();
            var move = set.Add(MfvEffectKind.Move);
            move.tilt.isRange = false;

            var view = new MfvClipInspectorView(set);
            var animatable = view.Query<MfvAnimatableView>().First();
            var single = animatable.Query<MfvValueSlider>().ToList().First(s => s.label == "Tilt");
            var ranged = animatable.Query<MfvRangeSlider>().ToList().First(s => s.label == "Tilt");

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
            var set = new MfvClipEffectSet();

            Assert.AreEqual("カラー", set.GetAddLabel(MfvEffectKind.Color));
            var first = set.Add(MfvEffectKind.Color);
            Assert.AreEqual(MfvParity.All, first.parity);

            Assert.AreEqual("カラー" + MfvEffectCatalog.OddSuffix, set.GetAddLabel(MfvEffectKind.Color));
            var odd = set.Add(MfvEffectKind.Color);

            Assert.AreEqual(MfvParity.Even, first.parity, "The existing card must become （偶数）.");
            Assert.AreEqual(MfvParity.Odd, odd.parity);
            Assert.AreEqual(1, set.effects.IndexOf(odd), "（奇数） must be inserted directly below （偶数）.");

            // Properties are identical across the pair at the moment of the split.
            Assert.AreEqual(first.colorStops.Count, odd.colorStops.Count);

            Assert.IsNull(set.GetAddLabel(MfvEffectKind.Color), "A fully split kind leaves the add list.");
            Assert.IsFalse(set.CanAdd(MfvEffectKind.Color));
        }

        [Test]
        public void RemovingOneHalfOfAPair_PromotesTheSurvivor()
        {
            var set = new MfvClipEffectSet();
            set.Add(MfvEffectKind.Gobo);
            set.Add(MfvEffectKind.Gobo);

            set.Remove(set.IndexOf(MfvEffectKind.Gobo, MfvParity.Odd));

            Assert.AreEqual(1, set.effects.Count);
            Assert.AreEqual(MfvParity.All, set.effects[0].parity);
            Assert.AreEqual("ゴボ" + MfvEffectCatalog.OddSuffix, set.GetAddLabel(MfvEffectKind.Gobo));
        }

        [Test]
        public void AddCatalog_ListsOnlyAddableKinds()
        {
            var set = new MfvClipEffectSet();
            set.addCatalogExpanded = true;
            set.Add(MfvEffectKind.Color);
            set.Add(MfvEffectKind.Color);

            var catalog = new MfvAddEffectCatalog(set, _ => { });
            var texts = Labels(catalog).Select(label => label.text).ToList();

            Assert.IsTrue(texts.Contains("ムーブ"), "An unused kind is listed unsuffixed.");
            Assert.IsFalse(
                texts.Any(text => text.StartsWith("カラー")),
                "A kind split into both parities must leave the add list.");
        }

        [Test]
        public void MoveEffect_ShowsPhaseOffsetOnlyWhenBothAxesAreRanges()
        {
            var set = new MfvClipEffectSet();
            var move = set.Add(MfvEffectKind.Move);
            move.tilt.isRange = true;
            move.pan.isRange = false;

            var view = new MfvClipInspectorView(set);
            var offset = view.Query<MfvValueSlider>().ToList().First(s => s.label == "位相差");
            Assert.AreEqual(DisplayStyle.None, offset.style.display.value);

            move.pan.isRange = true;
            view.Query<MfvEffectView>().First().Refresh();
            Assert.AreEqual(DisplayStyle.Flex, offset.style.display.value);
        }

        [Test]
        public void MoveEffect_TabSwitchesBetweenAngleAndUserTracking()
        {
            var set = new MfvClipEffectSet();
            var move = set.Add(MfvEffectKind.Move);
            var view = new MfvClipInspectorView(set);

            var tilt = view.Query<MfvValueSlider>().ToList().First(s => s.label == "Tilt");
            var follow = view.Query<MfvValueSlider>().ToList().First(s => s.label == "追従速度");

            Assert.IsTrue(IsShown(tilt), "角度で指定 must show the Tilt row.");
            Assert.IsFalse(IsShown(follow), "追従速度 belongs to the ユーザーを追跡 tab only.");

            move.moveMode = MfvMoveMode.TrackUser;
            view.Query<MfvEffectView>().First().Refresh();

            Assert.IsFalse(IsShown(tilt), "Switching to ユーザーを追跡 must hide the angle rows.");
            Assert.IsTrue(IsShown(follow));
        }


        [Test]
        public void CopyButton_CopiesParametersInsteadOfDuplicatingTheCard()
        {
            var set = new MfvClipEffectSet();
            var even = set.Add(MfvEffectKind.Cone);
            even.coneWidth.range = new Vector2(3f, 45f);
            even.coneWidth.isRange = true;

            var odd = set.Add(MfvEffectKind.Cone);
            Assert.AreEqual(2, set.effects.Count, "偶数 / 奇数 is the only legal way a kind repeats.");
            Assert.AreEqual(MfvParity.Odd, odd.parity);

            odd.coneWidth.range = new Vector2(0f, 1f);
            odd.coneWidth.isRange = false;

            var view = new MfvClipInspectorView(set);
            var cards = view.Query<MfvEffectView>().ToList();

            cards[0].Card.RequestCopy();
            Assert.AreEqual(2, set.effects.Count, "コピー must not add a card.");

            cards[1].Refresh();
            cards[1].Card.RequestPaste();

            var pasted = set.effects[1];
            Assert.AreEqual(2, set.effects.Count, "貼り付け replaces parameters, it does not insert.");
            Assert.AreEqual(new Vector2(3f, 45f), pasted.coneWidth.range);
            Assert.IsTrue(pasted.coneWidth.isRange);
            Assert.AreEqual(MfvParity.Odd, pasted.parity, "貼り付け keeps the card's own identity.");
            Assert.AreEqual(MfvEffectKind.Cone, pasted.kind);
        }

        [Test]
        public void Clipboard_OffersPasteOnlyForTheSameKind()
        {
            var cone = MfvEffect.Create(MfvEffectKind.Cone);
            var color = MfvEffect.Create(MfvEffectKind.Color);

            Assert.IsFalse(MfvEffectClipboard.CanPasteInto(cone), "Nothing has been copied yet.");

            MfvEffectClipboard.Copy(cone);
            Assert.IsTrue(MfvEffectClipboard.CanPasteInto(cone));
            Assert.IsFalse(MfvEffectClipboard.CanPasteInto(color));
            Assert.IsNull(MfvEffectClipboard.Paste(color));
        }

        [Test]
        public void PasteButton_AppearsOnlyWhileACompatiblePayloadExists()
        {
            var set = new MfvClipEffectSet();
            set.Add(MfvEffectKind.Cone);
            set.Add(MfvEffectKind.Brightness);

            var view = new MfvClipInspectorView(set);
            var cards = view.Query<MfvEffectView>().ToList();

            foreach (var card in cards)
            {
                Assert.IsFalse(card.Card.PasteAvailable, "貼り付け must be hidden with an empty clipboard.");
            }

            MfvEffectClipboard.Copy(set.effects[0]);
            var refreshed = new MfvClipInspectorView(set).Query<MfvEffectView>().ToList();

            Assert.IsTrue(refreshed[0].Card.PasteAvailable, "コーン accepts コーン parameters.");
            Assert.IsFalse(refreshed[1].Card.PasteAvailable, "明るさ must not accept them.");
        }

        [Test]
        public void Palettes_StartEmptyWithOnlyTheActionTiles()
        {
            // A pre-filled palette would animate the moment the effect is added.
            Assert.AreEqual(0, MfvEffect.Create(MfvEffectKind.Color).colorStops.Count);
            Assert.AreEqual(0, MfvEffect.Create(MfvEffectKind.Gobo).goboStops.Count);

            var set = new MfvClipEffectSet();
            set.Add(MfvEffectKind.Color);
            set.Add(MfvEffectKind.Gobo);
            var view = new MfvClipInspectorView(set);

            foreach (var palette in new VisualElement[] { view.Q<MfvColorPalette>(), view.Q<MfvGoboPalette>() })
            {
                var swatches = palette.Query(className: "mfv-swatch").ToList();
                Assert.AreEqual(2, swatches.Count, "Only ＋ and 🗑 on an empty palette.");
                Assert.IsTrue(swatches.All(s => s.ClassListContains("mfv-swatch--action")));
                Assert.IsTrue(swatches[1].ClassListContains("mfv-swatch--disabled"), "🗑 has nothing to delete.");
                Assert.AreEqual(DisplayStyle.None, palette.Q(className: "mfv-palette__editor").style.display.value);
            }

            Assert.IsFalse(view.Q<MfvColorPalette>().CanDelete);
            var timings = view.Query<MfvAnimatableView>().ToList()
                .SelectMany(v => v.Query<MfvSegmentedControl>().ToList())
                .Where(c => c.label == "タイミング")
                .ToList();
            Assert.IsNotEmpty(timings);
            Assert.IsTrue(timings.All(t => !IsShown(t)), "Nothing to phase with no stops.");
        }

        [Test]
        public void ColorPalette_EditsTheSelectedStopWithATypeDropdownAndAPicker()
        {
            var set = new MfvClipEffectSet();
            var color = set.Add(MfvEffectKind.Color);
            color.colorStops.Add(new MfvColorStop(Color.red));
            color.selectedColorStop = 0;

            var view = new MfvClipInspectorView(set);
            var editor = view.Q(className: "mfv-palette__editor");
            Assert.IsNotNull(editor, "The selected stop needs an editor row.");

            var type = editor.Q<DropdownField>(className: "mfv-palette__type");
            Assert.IsNotNull(type, "種類 must be a dropdown, not a hidden overlay.");
            Assert.AreEqual("単色", type.value);
            Assert.IsNotNull(editor.Q<ColorField>(), "単色 shows a colour picker.");
            Assert.IsNull(editor.Q<GradientField>());

            view.Q<MfvColorPalette>().ChangeSelectedStopType(true);

            Assert.IsTrue(color.colorStops[0].isGradient);
            var swapped = view.Q(className: "mfv-palette__editor");
            Assert.IsNotNull(swapped.Q<GradientField>(), "グラデーション shows the gradient editor.");
            Assert.IsNull(swapped.Q<ColorField>());
        }


        private static MfvColorPalette BuildColorPalette(out MfvEffect color)
        {
            var set = new MfvClipEffectSet();
            color = set.Add(MfvEffectKind.Color);
            color.colorStops.Clear();
            color.colorStops.Add(new MfvColorStop(Color.red));
            color.colorStops.Add(new MfvColorStop(Color.green));
            color.colorStops.Add(new MfvColorStop(Color.blue));
            color.selectedColorStop = 1;
            return new MfvClipInspectorView(set).Q<MfvColorPalette>();
        }

        private static MfvGoboPalette BuildGoboPalette(out MfvEffect gobo)
        {
            var set = new MfvClipEffectSet();
            gobo = set.Add(MfvEffectKind.Gobo);
            return new MfvClipInspectorView(set).Q<MfvGoboPalette>();
        }

        [Test]
        public void ColorPlus_AddsWhiteRightAfterTheSelection()
        {
            var set = new MfvClipEffectSet();
            var empty = set.Add(MfvEffectKind.Color);
            var first = new MfvClipInspectorView(set).Q<MfvColorPalette>();

            first.RequestAdd();
            Assert.AreEqual(1, empty.colorStops.Count);
            Assert.AreEqual(Color.white, empty.colorStops[0].color);
            Assert.AreEqual(0, first.SelectedIndex);
            Assert.AreEqual(DisplayStyle.Flex, first.Q(className: "mfv-palette__editor").style.display.value,
                "The added stop is selected, so its picker is right there.");

            var palette = BuildColorPalette(out var color);
            palette.RequestAdd();

            CollectionAssert.AreEqual(
                new[] { Color.red, Color.green, Color.white, Color.blue },
                color.colorStops.Select(stop => stop.color).ToArray());
            Assert.AreEqual(2, color.selectedColorStop, "The new stop becomes the selection.");
        }

        [UnityTest]
        public IEnumerator ColorPalette_KeepsItsPickerFieldsAliveAcrossEdits()
        {
            // The colour / gradient picker windows stay bound to the field that opened
            // them. If an edit replaced that field, every later change would be lost.
            // ChangeEvent only fires on a panel, so this runs inside a real window.
            var set = new MfvClipEffectSet();
            var color = set.Add(MfvEffectKind.Color);
            color.colorStops.Add(new MfvColorStop(Color.red));
            color.colorStops.Add(BuildTestGradientStop());
            color.selectedColorStop = 0;

            var window = ScriptableObject.CreateInstance<PanelHostWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            try
            {
                var view = new MfvClipInspectorView(set);
                window.rootVisualElement.Add(view);
                yield return null;

                var palette = view.Q<MfvColorPalette>();
                var field = palette.Q<ColorField>();
                foreach (var next in new[] { Color.green, Color.blue, Color.yellow })
                {
                    field.value = next;
                    Assert.AreEqual(next, color.colorStops[0].color, "Every picker change must reach the model.");
                    Assert.AreSame(field, palette.Q<ColorField>(), "The field the picker is bound to was replaced.");
                    Assert.AreEqual(next, palette.Query(className: "mfv-swatch").First().style.backgroundColor.value,
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

        private static MfvColorStop BuildTestGradientStop()
        {
            var stop = new MfvColorStop(Color.white) { isGradient = true, gradient = new Gradient() };
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
            Assert.IsNull(palette.Q(className: "mfv-gobo-picker"), "The picker starts closed.");

            palette.RequestAdd();
            Assert.IsTrue(gobo.goboPickerExpanded);
            Assert.AreEqual(0, gobo.goboStops.Count, "Opening the picker adds nothing by itself.");

            var plus = palette.Query(className: "mfv-swatch--action").First();
            Assert.IsTrue(plus.ClassListContains("mfv-swatch--active"), "＋ shows that its picker is open.");

            var picker = palette.Q(className: "mfv-gobo-picker");
            Assert.IsNotNull(picker);
            var tiles = picker.Query(className: "mfv-swatch").ToList();
            Assert.AreEqual(MfvGoboLibrary.BuiltInCount + 1, tiles.Count, "OFF plus every built-in gobo.");
            Assert.IsTrue(palette.CatalogStops[0].IsOff);

            // OFF / Gobo3 / OFF: a blink sequence needs OFF more than once.
            var builtIn = palette.CatalogStops;
            palette.AddStop(new MfvGoboStop(builtIn[0]));
            palette.AddStop(new MfvGoboStop(builtIn[3]));
            palette.AddStop(new MfvGoboStop(builtIn[0]));
            Assert.AreEqual(3, gobo.goboStops.Count);
            Assert.IsTrue(gobo.goboStops[0].IsOff);
            Assert.AreSame(builtIn[3].texture, gobo.goboStops[1].texture);
            Assert.IsTrue(gobo.goboStops[2].IsOff);
            Assert.AreEqual(2, gobo.selectedGoboStop);

            // Adding goes right after the selection, not to the end.
            palette.Select(0);
            palette.AddStop(new MfvGoboStop(builtIn[5]));
            Assert.AreSame(builtIn[5].texture, gobo.goboStops[1].texture);
            Assert.AreEqual(1, gobo.selectedGoboStop);

            Assert.IsNotNull(palette.Q(className: "mfv-gobo-picker"), "The picker stays open between additions.");

            palette.RequestAdd();
            Assert.IsFalse(gobo.goboPickerExpanded);
            Assert.IsNull(palette.Q(className: "mfv-gobo-picker"));
        }

        [Test]
        public void GoboOff_IsAnOrdinaryStop()
        {
            var palette = BuildGoboPalette(out var gobo);
            gobo.goboStops.Add(new MfvGoboStop());
            gobo.goboStops.Add(new MfvGoboStop(palette.CatalogStops[1].texture));
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
            Assert.AreEqual(DisplayStyle.None, palette.Q(className: "mfv-palette__editor").style.display.value);

            Assert.IsFalse(palette.CanDelete);
            palette.DeleteSelected();

            var trash = palette.Query(className: "mfv-swatch--action").ToList().Last();
            Assert.IsTrue(trash.ClassListContains("mfv-swatch--disabled"), "🗑 must look disabled when empty.");
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

            var swatches = palette.Query(className: "mfv-swatch").ToList()
                .Where(e => !e.ClassListContains("mfv-swatch--action")).ToList();
            Assert.AreEqual(Color.red, swatches[2].style.backgroundColor.value, "The strip must be rebuilt in the new order.");
            Assert.IsTrue(swatches[2].ClassListContains("mfv-swatch--selected"));
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

            Assert.AreEqual(0, MfvColorPalette.SlotAt(others, new Vector2(5, 14)), "Left half of the first tile.");
            Assert.AreEqual(1, MfvColorPalette.SlotAt(others, new Vector2(20, 14)), "Right half of the first tile.");
            Assert.AreEqual(3, MfvColorPalette.SlotAt(others, new Vector2(200, 14)), "Past the end of row one.");
            Assert.AreEqual(3, MfvColorPalette.SlotAt(others, new Vector2(2, 40)), "Start of row two.");
            Assert.AreEqual(5, MfvColorPalette.SlotAt(others, new Vector2(200, 40)), "Past the end of the strip.");
            Assert.AreEqual(0, MfvColorPalette.SlotAt(others, new Vector2(100, -10)), "Above the strip.");
            Assert.AreEqual(5, MfvColorPalette.SlotAt(others, new Vector2(0, 200)), "Below the strip.");
        }

        [Test]
        public void VectorIcon_ParsesTablerPathsIncludingArcs()
        {
            var contours = new List<List<Vector2>>();
            var closed = new List<bool>();

            foreach (var path in MfvIcons.Copy)
            {
                MfvSvgPath.Flatten(path, contours, closed);
            }

            Assert.AreEqual(MfvIcons.Copy.Length, contours.Count, "Each subpath must produce one contour.");
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
            // The grid draws MfvEase directly, so the contract worth pinning is that the
            // curve library is monotone-anchored at both ends for the featured set.
            foreach (var type in MfvEasingGrid.Featured)
            {
                Assert.AreEqual(0f, MfvEase.Evaluate(type, 0f), 0.001f, $"{type} must start at 0.");
                Assert.AreEqual(1f, MfvEase.Evaluate(type, 1f), 0.001f, $"{type} must end at 1.");
            }
        }

        [Test]
        public void PhaseSettings_PingPongRatioMovesThePeak()
        {
            var settings = new MfvPhaseSettings
            {
                mode = MfvPhaseMode.PingPong,
                ease = MfvEaseType.Linear,
                pingPongRatio = 0.25f,
                fixtureGroupSize = 1,
                delay = 0f,
            };

            Assert.AreEqual(1f, settings.Evaluate(0.25f, 0), 0.01f, "The peak sits at 往復比.");
            Assert.AreEqual(0.5f, settings.Evaluate(0.125f, 0), 0.01f);
            Assert.AreEqual(0.5f, settings.Evaluate(0.625f, 0), 0.01f);
        }

        [Test]
        public void PhaseSettings_FixtureGroupSizeBundlesNeighbours()
        {
            var settings = new MfvPhaseSettings
            {
                mode = MfvPhaseMode.Forward,
                ease = MfvEaseType.Linear,
                fixtureGroupSize = 2,
                delay = 0.25f,
            };

            // A fixture group size of 2 means fixtures 0 and 1 share a phase, 2 and 3 share the next.
            Assert.AreEqual(settings.Evaluate(0.5f, 0), settings.Evaluate(0.5f, 1), 0.0001f);
            Assert.AreNotEqual(settings.Evaluate(0.5f, 0), settings.Evaluate(0.5f, 2));
        }
    }
}
