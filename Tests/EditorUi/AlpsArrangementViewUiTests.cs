using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Covers the container inspector: that it builds styled, that its rows leave R out,
    /// that each shape shows its own rows and off shows none, and that an edit moves the
    /// children.
    /// </summary>
    public class AlpsArrangementViewUiTests
    {
        private AlpsLanguage _language;

        [SetUp]
        public void OpenScene()
        {
            // Pin the language so the labels the tests resolve match the ones the views build,
            // whatever the machine's system language.
            _language = AlpsStrings.Language;
            AlpsStrings.Language = AlpsLanguage.English;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void CloseScene()
        {
            AlpsStrings.Language = _language;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void CustomEditor_BuildsTheViewAndLaysTheChildrenOut()
        {
            var arrangement = Rig(3, initialized: false);

            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(arrangement);
                Assert.IsInstanceOf<AlpsContainerEditor>(inspector);

                var host = inspector.CreateInspectorGUI();
                var view = host.Q<AlpsArrangementView>();
                Assert.NotNull(view, "The inspector did not build an AlpsArrangementView.");
                Assert.AreEqual(1, view.styleSheets.count, "AlpsInspector.uss did not resolve.");
                Assert.IsTrue(view.ClassListContains("alps-root"));
                Assert.IsTrue(arrangement.Initialized, "Opening the inspector gives the first layout.");
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }
            }
        }

        [Test]
        public void Rows_LeaveRangeOutAndReadASavedRangeAsOff()
        {
            var settings = new AlpsArrangementSettings { shape = AlpsArrangementShape.Line };
            settings.height.isRange = true;
            settings.height.range = new Vector2(-3f, 3f);

            var view = new AlpsArrangementView(settings);

            var letters = view.Query<Label>(className: "alps-rangeflag__box").ToList().Select(box => box.text).ToList();
            Assert.Contains("S", letters);
            Assert.IsFalse(letters.Contains("R"), "Only S is offered.");

            var heightLabel = AlpsStrings.Tr("common.height");
            var height = view.Query<VisualElement>().ToList().Where(e => e is BaseField<float> field && field.label == heightLabel);
            Assert.IsTrue(height.Any(IsShown), "The value slider shows while the saved range is ignored.");
            var ranges = view.Query<AlpsRangeSlider>().ToList().Where(slider => slider.label == heightLabel);
            Assert.IsFalse(ranges.Any(IsShown), "No range or spread slider shows.");
        }

        // The rows are named by their string key, resolved to text in the body, so the cases do
        // not depend on the active language.
        [TestCase(AlpsArrangementShape.Off, new string[0], new[] { "common.start", "common.radius", "arrangement.sides", "common.width", "arrangement.columns", "arrangement.spacing", "common.order", "common.symmetric", "common.height", "common.rotationY" })]
        [TestCase(AlpsArrangementShape.Line, new[] { "common.start", "common.end", "arrangement.spacing", "common.order", "common.symmetric", "common.height" }, new[] { "common.radius", "arrangement.sides", "common.width", "arrangement.columns" })]
        [TestCase(AlpsArrangementShape.Circle, new[] { "common.radius", "common.rotation", "arrangement.sweep" }, new[] { "common.start", "arrangement.sides", "common.width" })]
        [TestCase(AlpsArrangementShape.Polygon, new[] { "arrangement.sides", "common.radius", "common.rotation" }, new[] { "arrangement.sweep", "common.start", "common.width" })]
        [TestCase(AlpsArrangementShape.Rectangle, new[] { "common.width", "arrangement.depth" }, new[] { "arrangement.columns", "common.radius", "common.start" })]
        [TestCase(AlpsArrangementShape.Grid, new[] { "arrangement.columns", "common.width", "arrangement.depth" }, new[] { "arrangement.sides", "common.radius", "arrangement.sweep" })]
        public void Shapes_ShowTheirOwnRows(AlpsArrangementShape shape, string[] shown, string[] hidden)
        {
            var view = new AlpsArrangementView(new AlpsArrangementSettings { shape = shape });

            foreach (var key in shown)
            {
                var label = AlpsStrings.Tr(key);
                Assert.IsTrue(HasVisibleLabel(view, label), $"{shape} should show {label}.");
            }

            foreach (var key in hidden)
            {
                var label = AlpsStrings.Tr(key);
                Assert.IsFalse(HasVisibleLabel(view, label), $"{shape} should hide {label}.");
            }
        }

        [Test]
        public void Facing_ShowsTheTargetOnlyWhileFacingIt()
        {
            var settings = new AlpsArrangementSettings { shape = AlpsArrangementShape.Line };
            var view = new AlpsArrangementView(settings);
            Assert.IsFalse(HasVisibleLabel(view, AlpsStrings.Tr("arrangement.target"), typeof(AlpsVectorField)));
            Assert.IsFalse(HasVisibleLabel(view, AlpsStrings.Tr("common.seed")));

            settings.facing = AlpsArrangementFacing.Target;
            settings.order = AlpsOrderMode.Random;
            view.Reload();
            Assert.IsTrue(HasVisibleLabel(view, AlpsStrings.Tr("arrangement.target"), typeof(AlpsVectorField)));
            Assert.IsTrue(HasVisibleLabel(view, AlpsStrings.Tr("common.seed")));
        }

        [UnityTest]
        public IEnumerator Editing_MovesTheChildren()
        {
            // ChangeEvent only fires on a panel, so this runs inside a real window.
            var arrangement = Rig(3, initialized: true);
            var children = Enumerable.Range(0, 3).Select(arrangement.transform.GetChild).ToArray();
            var window = ScriptableObject.CreateInstance<PanelHostWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            window.ShowUtility();
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(arrangement);
                var host = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(host);
                yield return null;

                var view = host.Q<AlpsArrangementView>();
                var shape = view.Query<AlpsSegmentedControl>().ToList().First(control => control.label == AlpsStrings.Tr("arrangement.shape"));
                shape.value = (int)AlpsArrangementShape.Circle;
                Assert.AreEqual(AlpsArrangementShape.Circle, arrangement.settings.shape);
                AssertPosition(new Vector3(0f, 0f, 3f), children[0]);

                var radius = view.Query<AlpsValueSlider>().ToList().First(slider => slider.label == AlpsStrings.Tr("common.radius"));
                radius.value = 5f;
                AssertPosition(new Vector3(0f, 0f, 5f), children[0]);

                var start = view.Query<AlpsVectorField>().ToList().First(field => field.label == AlpsStrings.Tr("common.start"));
                arrangement.settings.radius.value = 2f;
                arrangement.settings.lineStart = new Vector3(1f, 2f, 3f);
                view.Reload();
                Assert.AreEqual(2f, radius.value, 0.0001f, "Reload shows a value changed elsewhere.");
                Assert.AreEqual(new Vector3(1f, 2f, 3f), start.value);
            }
            finally
            {
                if (inspector != null)
                {
                    Object.DestroyImmediate(inspector);
                }

                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        private static AlpsContainer Rig(int count, bool initialized)
        {
            var rig = new GameObject("Rig");
            for (var i = 0; i < count; i++)
            {
                var child = new GameObject($"Child {i}").transform;
                child.SetParent(rig.transform, false);
                child.localPosition = new Vector3(i, 0f, 7f);
            }

            var arrangement = rig.AddComponent<AlpsContainer>();
            arrangement.settings.shape = AlpsArrangementShape.Line;
            arrangement.Initialized = initialized;
            return arrangement;
        }

        private static bool HasVisibleLabel(VisualElement root, string text, System.Type fieldType = null)
        {
            return root.Query<Label>().ToList().Any(label =>
                label.text == text &&
                label.ClassListContains("unity-base-field__label") &&
                (fieldType == null || fieldType.IsInstanceOfType(label.parent)) &&
                IsShown(label));
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

        private static void AssertPosition(Vector3 expected, Transform transform)
        {
            Assert.That(Vector3.Distance(expected, transform.localPosition), Is.LessThan(1e-3f),
                $"{transform.name} expected {expected:F3} but was {transform.localPosition:F3}");
        }

        private sealed class PanelHostWindow : EditorWindow
        {
        }
    }
}
