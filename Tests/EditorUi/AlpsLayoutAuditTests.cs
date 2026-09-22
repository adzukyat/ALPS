using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Layout audit. Yoga computes real geometry for an editor panel in batchmode, so the
    /// inspector's actual boxes can be measured rather than merely built: nothing may
    /// collapse, overflow its parent, or overlap a sibling, at any inspector width.
    ///
    /// This does not verify the rasterised pixels (font hinting, painter2D strokes,
    /// border-radius antialiasing). It verifies the box model the renderer is handed.
    /// </summary>
    public class AlpsLayoutAuditTests
    {
        /// <summary>
        /// The 476px minimum card width plus two wider
        /// inspectors. Narrower hosts scroll (see the min-width on .alps-root).
        /// </summary>
        private static readonly float[] Widths = { 476f, 720f, 1000f };

        private static AlpsClipEffectSet BuildFixtureSet()
        {
            return AlpsInspectorFixture.Build();
        }

        private static IEnumerator Measure(float width, System.Action<VisualElement> assert)
        {
            var window = ScriptableObject.CreateInstance<LayoutProbeWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;

            // A tall panel so vertical flex-shrink never masks a real defect. The
            // Inspector hosts this inside a ScrollView, which is equally unconstrained.
            window.minSize = new Vector2(width, 3200f);
            window.maxSize = new Vector2(width, 3200f);
            window.ShowUtility();
            window.position = new Rect(0f, 0f, width, 3200f);

            var view = new AlpsClipInspectorView(BuildFixtureSet());
            window.rootVisualElement.Add(view);

            for (var i = 0; i < 16; i++)
            {
                window.Repaint();
                yield return null;
            }

            try
            {
                assert(view);
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        /// <summary>Walks the tree the user can actually see.</summary>
        private static IEnumerable<VisualElement> Visible(VisualElement root)
        {
            if (root.resolvedStyle.display == DisplayStyle.None)
            {
                yield break;
            }

            yield return root;

            foreach (var child in root.Children())
            {
                foreach (var descendant in Visible(child))
                {
                    yield return descendant;
                }
            }
        }

        private static string Describe(VisualElement element)
        {
            var classes = string.Join(".", element.GetClasses());
            var text = element is TextElement textElement ? $" \"{textElement.text}\"" : string.Empty;
            return $"{element.GetType().Name}[{classes}]{text} {element.layout}";
        }

        [UnityTest]
        public IEnumerator NothingCollapses([ValueSource(nameof(Widths))] float width)
        {
            yield return Measure(width, view =>
            {
                var collapsed = Visible(view)
                    .OfType<TextElement>()
                    .Where(e => !string.IsNullOrEmpty(e.text))
                    .Where(e => e.layout.width < 1f || e.layout.height < 1f)
                    .ToList();

                Assert.IsEmpty(
                    collapsed.Select(Describe),
                    $"Visible text collapsed to zero size at width {width}.");
            });
        }

        [UnityTest]
        public IEnumerator NothingOverflowsItsParent([ValueSource(nameof(Widths))] float width)
        {
            yield return Measure(width, view =>
            {
                var overflowing = new List<string>();

                // The right edge of the card's content box. Nothing may cross it.
                var contentRight = view.worldBound.xMax - view.resolvedStyle.paddingRight;

                foreach (var element in Visible(view))
                {
                    if (element == view || element.resolvedStyle.position == Position.Absolute)
                    {
                        continue;
                    }

                    var parent = element.parent;
                    var rect = element.layout;

                    // A container that collapsed to nothing while still holding a sized
                    // child hides the overflow from a parent-relative check, so test it
                    // against the card itself as well.
                    if (element.worldBound.xMax > contentRight + 0.6f)
                    {
                        overflowing.Add(
                            $"{Describe(element)} reaches {element.worldBound.xMax:0.#}, " +
                            $"card content ends at {contentRight:0.#}");
                        continue;
                    }

                    if (parent == null || parent.layout.width <= 0f)
                    {
                        if (parent != null && rect.width > 0.6f)
                        {
                            overflowing.Add($"{Describe(element)} sits in a collapsed parent {Describe(parent)}");
                        }

                        continue;
                    }

                    if (rect.x < -0.6f || rect.xMax > parent.layout.width + 0.6f)
                    {
                        overflowing.Add($"{Describe(element)} in parent width {parent.layout.width}");
                    }
                }

                Assert.IsEmpty(overflowing, $"Elements overflow their parent at width {width}.");
            });
        }

        [UnityTest]
        public IEnumerator NoContainerCollapses([ValueSource(nameof(Widths))] float width)
        {
            yield return Measure(width, view =>
            {
                var collapsed = Visible(view)
                    .Where(e => e.resolvedStyle.position != Position.Absolute)
                    .Where(e => e.childCount > 0)
                    .Where(e => e.layout.width < 0.6f || e.layout.height < 0.6f)
                    .Where(e => e.Children().Any(c =>
                        c.resolvedStyle.display != DisplayStyle.None &&
                        (c.layout.width > 0.6f || c.layout.height > 0.6f)))
                    .ToList();

                Assert.IsEmpty(
                    collapsed.Select(Describe),
                    $"A container collapsed while still holding sized children at width {width}.");
            });
        }

        [UnityTest]
        public IEnumerator SiblingsDoNotOverlap([ValueSource(nameof(Widths))] float width)
        {
            yield return Measure(width, view =>
            {
                var overlapping = new List<string>();

                foreach (var element in Visible(view))
                {
                    var flow = element.Children()
                        .Where(c => c.resolvedStyle.display != DisplayStyle.None)
                        .Where(c => c.resolvedStyle.position != Position.Absolute)
                        .Where(c => c.layout.height > 0f)
                        .ToList();

                    for (var i = 1; i < flow.Count; i++)
                    {
                        var above = flow[i - 1].layout;
                        var below = flow[i].layout;
                        if (Mathf.Abs(above.x - below.x) < 0.6f &&
                            above.width > 1f &&
                            below.y < above.yMax - 0.6f)
                        {
                            overlapping.Add($"{Describe(flow[i - 1])} overlaps {Describe(flow[i])}");
                        }
                    }
                }

                Assert.IsEmpty(overlapping, $"Sibling rows overlap at width {width}.");
            });
        }

        [UnityTest]
        public IEnumerator NoTextIsClipped([ValueSource(nameof(Widths))] float width)
        {
            yield return Measure(width, view =>
            {
                var clipped = new List<string>();

                foreach (var element in Visible(view).OfType<TextElement>())
                {
                    if (string.IsNullOrEmpty(element.text) ||
                        element.resolvedStyle.whiteSpace == WhiteSpace.Normal)
                    {
                        continue;
                    }

                    // What the glyphs need versus what the box actually allots.
                    var needed = element.MeasureTextSize(
                        element.text,
                        0f, VisualElement.MeasureMode.Undefined,
                        0f, VisualElement.MeasureMode.Undefined);

                    // A label with overflow: visible is allowed to spill into the gap after
                    // it, as the cancel-on-return label does in the 96px label column.
                    // What must never happen is spilling far enough to reach the next
                    // element in the row.
                    var available = AvailableTextWidth(element);
                    if (needed.x > available + 0.6f)
                    {
                        clipped.Add(
                            $"{Describe(element)} needs {needed.x:0.#}px, has {available:0.#}px " +
                            $"(box {element.contentRect.width:0.#}px)");
                    }
                }

                Assert.IsEmpty(clipped, $"Text is clipped at width {width}.");
            });
        }

        /// <summary>
        /// How much horizontal room a text element really has. Text is not clipped unless
        /// the element hides its overflow, so a fixed-width label may use the empty space
        /// up to its next laid-out sibling before anything is lost.
        /// </summary>
        private static float AvailableTextWidth(TextElement element)
        {
            var box = element.contentRect.width;
            var parent = element.parent;
            if (parent == null)
            {
                return box;
            }

            var style = element.resolvedStyle;
            var left = element.worldBound.xMin + style.borderLeftWidth + style.paddingLeft;

            // The furthest the glyphs may run before they collide with something real:
            // the parent's content edge, or whichever sibling comes next in the row.
            var limit = parent.worldBound.xMax
                        - parent.resolvedStyle.borderRightWidth
                        - parent.resolvedStyle.paddingRight;

            foreach (var sibling in parent.Children())
            {
                if (sibling == element || sibling.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                var siblingLeft = sibling.worldBound.xMin;
                if (siblingLeft >= left + box - 0.6f && siblingLeft < limit)
                {
                    limit = siblingLeft;
                }
            }

            return Mathf.Max(box, limit - left);
        }

        [UnityTest]
        public IEnumerator DesignTokens_SurviveTheUssRoundTrip()
        {
            // Every number below is border-box geometry, as UI Toolkit lays it out.
            var sizes = new (string ClassName, float Width, float Height)[]
            {
                ("unity-base-field__label", 96f, -1f),
                ("alps-numberbox", 70f, 18f),
                ("alps-rangeflag__box", 24f, 18f),
                ("alps-switch__track", 36f, 20f),
                ("alps-switch__knob", 12f, 12f),
                ("alps-slider__thumb", 10f, 10f),
                ("alps-slider__rail", -1f, 2f),
                ("alps-slider__tick", 1f, 4f),
                ("alps-swatch", 28f, 28f),
                ("alps-add__item", 137.33f, -1f),
                ("alps-add__thumb", -1f, 26f),
            };

            yield return Measure(476f, view =>
            {
                Assert.AreEqual(
                    0,
                    ((Color32)view.resolvedStyle.backgroundColor).a,
                    "The root must stay transparent so the native inspector background shows through.");

                Assert.AreEqual(12f, view.resolvedStyle.fontSize, 0.01f);

                // Layout snaps edges to device pixels, so a fractional token such as the
                // add item's 137.33px may move by a whole point on a 1x display.
                var tolerance = Mathf.Max(0.6f, 1f / EditorGUIUtility.pixelsPerPoint);

                foreach (var (className, width, height) in sizes)
                {
                    var matches = Visible(view)
                        .Where(e => e.ClassListContains(className))
                        .Where(e => e.layout.width > 0f && e.layout.height > 0f)
                        .ToList();

                    Assert.IsNotEmpty(matches, $"No visible .{className} to measure.");

                    foreach (var element in matches)
                    {
                        if (width > 0f)
                        {
                            Assert.AreEqual(
                                width, element.layout.width, tolerance,
                                $".{className} width drifted: {Describe(element)}");
                        }

                        if (height > 0f)
                        {
                            Assert.AreEqual(
                                height, element.layout.height, tolerance,
                                $".{className} height drifted: {Describe(element)}");
                        }
                    }
                }

                // The easing tiles come in two sizes. The compact one lives in an own-phase panel.
                foreach (var tile in Visible(view).Where(e => e.ClassListContains("alps-easegrid__tile")))
                {
                    var compact = tile.GetFirstAncestorOfType<AlpsPhaseSettingsView>()
                        ?.ClassListContains("alps-nested") == true;
                    Assert.AreEqual(compact ? 32f : 36f, tile.layout.width, 0.6f, "Easing tile width drifted.");
                    Assert.AreEqual(compact ? 26f : 30f, tile.layout.height, 0.6f, "Easing tile height drifted.");
                }

                foreach (var graph in Visible(view).Where(e => e.ClassListContains("alps-graph")))
                {
                    var compact = graph.ClassListContains("alps-graph--compact");
                    Assert.AreEqual(compact ? 32f : 46f, graph.layout.height, 0.6f, "Graph height drifted.");
                }

                // The card body is a 452px panel, indented 12px inside the root.
                foreach (var body in Visible(view).Where(e => e.ClassListContains("alps-card__body")))
                {
                    Assert.AreEqual(452f, body.layout.width, 0.6f, "Card body width drifted.");
                    Assert.AreEqual(
                        new Color32(0x40, 0x40, 0x40, 0xFF),
                        (Color32)body.resolvedStyle.backgroundColor,
                        "Card body background token drifted.");
                }

                foreach (var header in Visible(view).Where(e => e.ClassListContains("alps-card__header")))
                {
                    Assert.AreEqual(
                        new Color32(0x4A, 0x4A, 0x4A, 0xFF),
                        (Color32)header.resolvedStyle.backgroundColor,
                        "Card header background token drifted.");
                }
            });
        }

        [Test]
        public void GoboThumbnails_LoadFromTheVRSLPackage()
        {
            // The palette reads its thumbnails from the VR Stage Lighting package. A broken path
            // would only show as empty tiles.
            for (var index = 1; index <= AlpsGoboLibrary.GoboCount; index++)
            {
                Assert.NotNull(AlpsGoboLibrary.Load(index), $"Gobo {index} did not load from the VRSL package.");
            }
        }

        [UnityTest]
        public IEnumerator ValueBoxesKeepTheirGutters([ValueSource(nameof(Widths))] float width)
        {
            // AlpsNumberBox is a BaseField, so an unscoped margin rule loses to the
            // `.alps-root .unity-base-field` reset and the box ends up flush against the
            // track, which then draws the min thumb inside the box.
            yield return Measure(width, view =>
            {
                var wrong = new List<string>();

                foreach (var box in Visible(view).Where(e => e.ClassListContains("alps-numberbox--left")))
                {
                    var next = NextSibling(box);
                    if (next != null)
                    {
                        var gap = next.worldBound.xMin - box.worldBound.xMax;
                        if (Mathf.Abs(gap - 9f) > 0.6f)
                        {
                            wrong.Add($"{Describe(box)} leaves {gap:0.#}px before {Describe(next)}");
                        }
                    }
                }

                foreach (var box in Visible(view).Where(e => e.ClassListContains("alps-numberbox--right")))
                {
                    var previous = PreviousSibling(box);
                    if (previous != null)
                    {
                        var gap = box.worldBound.xMin - previous.worldBound.xMax;
                        if (Mathf.Abs(gap - 9f) > 0.6f)
                        {
                            wrong.Add($"{Describe(box)} leaves {gap:0.#}px after {Describe(previous)}");
                        }
                    }
                }

                Assert.IsEmpty(wrong, $"Value box gutters drifted at width {width}.");
            });
        }

        [UnityTest]
        public IEnumerator CardBodiesEndWithTheSameSpace([ValueSource(nameof(Widths))] float width)
        {
            // A body ends 12px below its last control: 3px of padding plus the 9px its last
            // row carries. A group around that row (a mode pane, the phase settings, a
            // parameter with its range frame) must not add its own margin on top.
            yield return Measure(width, view =>
            {
                var wrong = new List<string>();

                foreach (var body in Visible(view).Where(e => e.ClassListContains("alps-card__body")))
                {
                    var space = body.resolvedStyle.paddingBottom;
                    var chain = new List<string>();
                    var current = body;

                    while (true)
                    {
                        var last = current.Children()
                            .Where(c => c.resolvedStyle.display != DisplayStyle.None)
                            .Where(c => c.resolvedStyle.position != Position.Absolute)
                            .LastOrDefault();
                        if (last == null)
                        {
                            break;
                        }

                        var inner = current.layout.height - current.resolvedStyle.paddingBottom -
                                    current.resolvedStyle.borderBottomWidth;
                        space += inner - last.layout.yMax;
                        chain.Add(Describe(last));
                        current = last;

                        // Stop at the first thing that draws: a control or a framed panel.
                        var style = last.resolvedStyle;
                        if (last is IBindable || style.backgroundColor.a > 0f || style.borderBottomWidth > 0f)
                        {
                            break;
                        }
                    }

                    if (Mathf.Abs(space - 12f) > 0.6f)
                    {
                        var title = body.parent.Q<Label>(className: "alps-card__title")?.text;
                        wrong.Add($"{title} ends {space:0.#}px below {string.Join(" > ", chain)}");
                    }
                }

                Assert.IsEmpty(wrong, $"A card body ends with extra space at width {width}.");
            });
        }

        [UnityTest]
        public IEnumerator SliderThumbsStayClearOfTheirValueBoxes([ValueSource(nameof(Widths))] float width)
        {
            // A thumb is centred on its value, so at the ends it hangs 5px past the rail.
            // The row leaves it that room. It must never land on the value box.
            yield return Measure(width, view =>
            {
                var boxes = Visible(view)
                    .Where(e => e.ClassListContains("alps-numberbox"))
                    .Where(e => e.layout.width > 1f)
                    .ToList();

                var collisions = new List<string>();

                foreach (var thumb in Visible(view).Where(e => e.ClassListContains("alps-slider__thumb")))
                {
                    foreach (var box in boxes)
                    {
                        if (thumb.worldBound.Overlaps(box.worldBound))
                        {
                            collisions.Add($"{Describe(thumb)} lands on {Describe(box)}");
                        }
                    }
                }

                Assert.IsEmpty(collisions, $"A slider thumb is drawn over a value box at width {width}.");
            });
        }

        private static VisualElement NextSibling(VisualElement element)
        {
            var parent = element.parent;
            if (parent == null)
            {
                return null;
            }

            var index = parent.IndexOf(element);
            for (var i = index + 1; i < parent.childCount; i++)
            {
                if (parent[i].resolvedStyle.display != DisplayStyle.None)
                {
                    return parent[i];
                }
            }

            return null;
        }

        private static VisualElement PreviousSibling(VisualElement element)
        {
            var parent = element.parent;
            if (parent == null)
            {
                return null;
            }

            for (var i = parent.IndexOf(element) - 1; i >= 0; i--)
            {
                if (parent[i].resolvedStyle.display != DisplayStyle.None)
                {
                    return parent[i];
                }
            }

            return null;
        }

        private sealed class LayoutProbeWindow : EditorWindow
        {
        }
    }
}
