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
        [SetUp]
        public void OpenScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void CloseScene()
        {
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

            var height = view.Query<VisualElement>().ToList().Where(e => e is BaseField<float> field && field.label == "高さ");
            Assert.IsTrue(height.Any(IsShown), "The value slider shows while the saved range is ignored.");
            var ranges = view.Query<AlpsRangeSlider>().ToList().Where(slider => slider.label == "高さ");
            Assert.IsFalse(ranges.Any(IsShown), "No range or spread slider shows.");
        }

        [TestCase(AlpsArrangementShape.Off, new string[0], new[] { "始点", "半径", "辺の数", "幅", "列数", "配置", "並び順", "高さ", "Y回転" })]
        [TestCase(AlpsArrangementShape.Line, new[] { "始点", "終点", "配置", "並び順", "高さ" }, new[] { "半径", "辺の数", "幅", "列数" })]
        [TestCase(AlpsArrangementShape.Circle, new[] { "半径", "回転", "角度" }, new[] { "始点", "辺の数", "幅" })]
        [TestCase(AlpsArrangementShape.Polygon, new[] { "辺の数", "半径", "回転" }, new[] { "角度", "始点", "幅" })]
        [TestCase(AlpsArrangementShape.Rectangle, new[] { "幅", "奥行き" }, new[] { "列数", "半径", "始点" })]
        [TestCase(AlpsArrangementShape.Grid, new[] { "列数", "幅", "奥行き" }, new[] { "辺の数", "半径", "角度" })]
        public void Shapes_ShowTheirOwnRows(AlpsArrangementShape shape, string[] shown, string[] hidden)
        {
            var view = new AlpsArrangementView(new AlpsArrangementSettings { shape = shape });

            foreach (var label in shown)
            {
                Assert.IsTrue(HasVisibleLabel(view, label), $"{shape} should show {label}.");
            }

            foreach (var label in hidden)
            {
                Assert.IsFalse(HasVisibleLabel(view, label), $"{shape} should hide {label}.");
            }
        }

        [Test]
        public void Facing_ShowsTheTargetOnlyWhileFacingIt()
        {
            var settings = new AlpsArrangementSettings { shape = AlpsArrangementShape.Line };
            var view = new AlpsArrangementView(settings);
            Assert.IsFalse(HasVisibleLabel(view, "注視点", typeof(AlpsVectorField)));
            Assert.IsFalse(HasVisibleLabel(view, "シード"));

            settings.facing = AlpsArrangementFacing.Target;
            settings.order = AlpsOrderMode.Random;
            view.Reload();
            Assert.IsTrue(HasVisibleLabel(view, "注視点", typeof(AlpsVectorField)));
            Assert.IsTrue(HasVisibleLabel(view, "シード"));
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
                var shape = view.Query<AlpsSegmentedControl>().ToList().First(control => control.label == "形状");
                shape.value = (int)AlpsArrangementShape.Circle;
                Assert.AreEqual(AlpsArrangementShape.Circle, arrangement.settings.shape);
                AssertPosition(new Vector3(0f, 0f, 3f), children[0]);

                var radius = view.Query<AlpsValueSlider>().ToList().First(slider => slider.label == "半径");
                radius.value = 5f;
                AssertPosition(new Vector3(0f, 0f, 5f), children[0]);

                var start = view.Query<AlpsVectorField>().ToList().First(field => field.label == "始点");
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
