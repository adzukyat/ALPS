using System.Collections.Generic;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// A container in a scene: which transforms a layout writes, undo, the first layout over
    /// children that are already placed, the fixtures it holds, the fixture list of an old
    /// fixture group turned into the children's order, and the watcher that picks the
    /// containers a change touches.
    /// </summary>
    public class AlpsArrangementLayoutTests
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

        // ------------------------------------------------------------------ apply

        [Test]
        public void Apply_WritesTheDirectChildrenOnly()
        {
            var arrangement = Rig(3);
            var children = Children(arrangement);
            children[1].gameObject.SetActive(false);
            var grandchild = new GameObject("Grandchild").transform;
            grandchild.SetParent(children[0], false);
            grandchild.localPosition = new Vector3(5f, 5f, 5f);

            Assert.That(AlpsArrangementLayout.Apply(arrangement, recordUndo: false), Is.EqualTo(3));

            AssertPosition(new Vector3(-2f, 0f, 0f), children[0]);
            AssertPosition(new Vector3(0f, 0f, 0f), children[1]);
            AssertPosition(new Vector3(2f, 0f, 0f), children[2]);
            AssertPosition(new Vector3(5f, 5f, 5f), grandchild);
        }

        [Test]
        public void Apply_WritesNothingOnceInPlace()
        {
            var arrangement = Rig(3);
            AlpsArrangementLayout.Apply(arrangement, recordUndo: false);

            Assert.That(AlpsArrangementLayout.Apply(arrangement, recordUndo: false), Is.EqualTo(0));
        }

        [Test]
        public void Apply_OffLeavesTheChildrenAlone()
        {
            var arrangement = Rig(2, initialized: false, shape: AlpsArrangementShape.Off);

            Assert.That(AlpsArrangementLayout.Apply(arrangement, recordUndo: false), Is.EqualTo(0));
            AlpsArrangementLayout.Refresh(arrangement, recordUndo: false);
            AssertPosition(new Vector3(0f, 0f, 7f), Children(arrangement)[0]);
            Assert.That(arrangement.Initialized, Is.False, "The first layout waits for a shape.");
        }

        [Test]
        public void Container_StartsWithTheShapeOff()
        {
            var container = new GameObject("Container").AddComponent<AlpsContainer>();

            Assert.That(container.settings.shape, Is.EqualTo(AlpsArrangementShape.Off));
            Assert.That(container.Arranges, Is.False);
        }

        [Test]
        public void Apply_UndoPutsTheChildrenBack()
        {
            var arrangement = Rig(2);
            var children = Children(arrangement);

            Undo.IncrementCurrentGroup();
            AlpsArrangementLayout.Apply(arrangement, recordUndo: true);
            AssertPosition(new Vector3(-2f, 0f, 0f), children[0]);

            Undo.PerformUndo();
            AssertPosition(new Vector3(0f, 0f, 7f), children[0]);
            AssertPosition(new Vector3(1f, 0f, 7f), children[1]);
        }

        [Test]
        public void Apply_FollowsTheChildrenOrder()
        {
            var arrangement = Rig(3);
            var children = Children(arrangement);
            children[2].SetSiblingIndex(0);

            AlpsArrangementLayout.Apply(arrangement, recordUndo: false);

            AssertPosition(new Vector3(-2f, 0f, 0f), children[2]);
            AssertPosition(new Vector3(0f, 0f, 0f), children[0]);
            AssertPosition(new Vector3(2f, 0f, 0f), children[1]);
        }

        // ------------------------------------------------------------- initialize

        [Test]
        public void Initialize_StartsTheLineOnTheFirstAndLastChildAndKeepsASharedRotation()
        {
            var arrangement = Rig(3, initialized: false);
            var children = Children(arrangement);
            var turned = Quaternion.Euler(0f, 90f, 180f);
            children[0].localPosition = new Vector3(0f, 1f, 0f);
            children[1].localPosition = new Vector3(3f, 1f, 1f);
            children[2].localPosition = new Vector3(4f, 1f, 2f);
            foreach (var child in children)
            {
                child.localRotation = turned;
            }

            AlpsArrangementLayout.Refresh(arrangement, recordUndo: false);

            Assert.That(arrangement.Initialized, Is.True);
            AssertPosition(new Vector3(0f, 1f, 0f), children[0]);
            AssertPosition(new Vector3(2f, 1f, 1f), children[1]);
            AssertPosition(new Vector3(4f, 1f, 2f), children[2]);
            Assert.That(Quaternion.Angle(turned, children[1].localRotation), Is.LessThan(0.01f));
        }

        [Test]
        public void Initialize_MovesTheDefaultLineOntoChildrenThatShareASpot()
        {
            var arrangement = Rig(3, initialized: false);
            var children = Children(arrangement);
            foreach (var child in children)
            {
                child.localPosition = new Vector3(1f, 2f, 3f);
            }

            AlpsArrangementLayout.Refresh(arrangement, recordUndo: false);

            AssertPosition(new Vector3(-1f, 2f, 3f), children[0]);
            AssertPosition(new Vector3(1f, 2f, 3f), children[1]);
            AssertPosition(new Vector3(3f, 2f, 3f), children[2]);
        }

        [Test]
        public void CommitEdit_StartsFromTheChildrenWhenAShapeIsFirstPicked()
        {
            var container = Rig(2, initialized: false, shape: AlpsArrangementShape.Off);
            var children = Children(container);

            AlpsArrangementLayout.CommitEdit(container);
            Assert.That(container.Initialized, Is.False);

            container.settings.shape = AlpsArrangementShape.Line;
            AlpsArrangementLayout.CommitEdit(container);

            Assert.That(container.Initialized, Is.True);
            AssertPosition(new Vector3(0f, 0f, 7f), children[0]);
            AssertPosition(new Vector3(1f, 0f, 7f), children[1]);
        }

        // --------------------------------------------------------------- fixtures

        [Test]
        public void Fixtures_FollowTheHierarchyOrderBelowTheContainer()
        {
            var container = Rig(2, shape: AlpsArrangementShape.Off);
            var children = Children(container);
            var first = children[0].gameObject.AddComponent<AlpsVRSLFixture>();
            var nested = new GameObject("Head");
            nested.transform.SetParent(children[1], false);
            nested.SetActive(false);
            var second = nested.AddComponent<AlpsVRSLFixture>();
            new GameObject("Outside").AddComponent<AlpsVRSLFixture>();

            CollectionAssert.AreEqual(new AlpsFixture[] { first, second }, container.Fixtures());

            children[1].SetSiblingIndex(0);
            CollectionAssert.AreEqual(new AlpsFixture[] { second, first }, container.Fixtures());
            CollectionAssert.AreEqual(new AlpsFixture[] { second }, second.Fixtures(), "A fixture drives itself.");
        }

        [Test]
        public void Migrate_OrdersTheChildrenLikeTheOldFixtureList()
        {
            var container = Rig(3, shape: AlpsArrangementShape.Off);
            var children = Children(container);
            var fixtures = children.Select(child => child.gameObject.AddComponent<AlpsVRSLFixture>()).ToArray();
            var holder = new GameObject("Holder").transform;
            holder.SetParent(container.transform, false);
            var inner = Enumerable.Range(0, 2).Select(i =>
            {
                var head = new GameObject($"Head {i}");
                head.transform.SetParent(holder, false);
                return head.AddComponent<AlpsVRSLFixture>();
            }).ToArray();
            var outside = new GameObject("Outside").AddComponent<AlpsVRSLFixture>();
            container.LegacyFixtures.AddRange(new AlpsFixture[] { fixtures[2], inner[1], outside, null, fixtures[0], inner[0] });

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Outside"));
            Assert.That(AlpsArrangementLayout.MigrateFixtureOrder(container, recordUndo: false), Is.True);

            CollectionAssert.AreEqual(new[] { fixtures[2], inner[1], inner[0], fixtures[0], fixtures[1] }, container.Fixtures());
            Assert.That(container.LegacyFixtures, Is.Empty);
            Assert.That(AlpsArrangementLayout.MigrateFixtureOrder(container, recordUndo: false), Is.False);
        }

        [Test]
        public void Migrate_RunsOverAnOpenedScene()
        {
            var container = Rig(2, shape: AlpsArrangementShape.Off);
            var fixtures = Children(container).Select(child => child.gameObject.AddComponent<AlpsVRSLFixture>()).ToArray();
            container.LegacyFixtures.AddRange(new AlpsFixture[] { fixtures[1], fixtures[0] });

            Assert.That(AlpsArrangementWatcher.MigrateScene(container.gameObject.scene), Is.EqualTo(1));
            CollectionAssert.AreEqual(new AlpsFixture[] { fixtures[1], fixtures[0] }, container.Fixtures());
        }

        // ---------------------------------------------------------------- watcher

        [Test]
        public void Watcher_CollectsTheArrangementsAChangeTouches()
        {
            var arrangement = Rig(2);
            var child = Children(arrangement)[0];
            var grandchild = new GameObject("Grandchild").transform;
            grandchild.SetParent(child, false);
            var elsewhere = new GameObject("Elsewhere");

            AssertCollects(arrangement, ObjectChangeKind.CreateGameObjectHierarchy, child.gameObject.GetInstanceID());
            AssertCollects(arrangement, ObjectChangeKind.DestroyGameObjectHierarchy, 0, arrangement.gameObject.GetInstanceID());
            AssertCollects(arrangement, ObjectChangeKind.ChangeChildrenOrder, arrangement.gameObject.GetInstanceID());
            AssertCollects(arrangement, ObjectChangeKind.ChangeGameObjectParent, elsewhere.GetInstanceID(),
                arrangement.gameObject.GetInstanceID(), 0);
            AssertCollects(arrangement, ObjectChangeKind.ChangeGameObjectOrComponentProperties, child.GetInstanceID());
            AssertCollects(arrangement, ObjectChangeKind.ChangeGameObjectOrComponentProperties, arrangement.GetInstanceID());
            AssertCollects(arrangement, ObjectChangeKind.ChangeGameObjectStructure, grandchild.gameObject.GetInstanceID());

            var nested = new GameObject("Holder");
            arrangement.transform.SetParent(nested.transform, false);
            AssertCollects(arrangement, ObjectChangeKind.ChangeGameObjectStructureHierarchy, nested.GetInstanceID());

            var set = new HashSet<AlpsContainer>();
            AlpsArrangementWatcher.CollectFor(ObjectChangeKind.ChangeGameObjectOrComponentProperties, grandchild.GetInstanceID(), 0, 0, set);
            AlpsArrangementWatcher.CollectFor(ObjectChangeKind.CreateGameObjectHierarchy, elsewhere.GetInstanceID(), 0, 0, set);
            Assert.That(set, Is.Empty, "A move below a slot or outside the container changes no layout.");
        }

        [Test]
        public void Watcher_ReadsTheEventsOfAPublish()
        {
            var arrangement = Rig(2);
            var child = Children(arrangement)[0];
            var scene = arrangement.gameObject.scene;

            var builder = new ObjectChangeEventStream.Builder(Allocator.Temp);
            try
            {
                var created = new CreateGameObjectHierarchyEventArgs(child.gameObject.GetInstanceID(), scene);
                builder.PushCreateGameObjectHierarchyEvent(ref created);
                var stream = builder.ToStream(Allocator.Temp);
                try
                {
                    var set = new HashSet<AlpsContainer>();
                    AlpsArrangementWatcher.Collect(ref stream, set);
                    Assert.That(set, Is.EquivalentTo(new[] { arrangement }));
                }
                finally
                {
                    stream.Dispose();
                }
            }
            finally
            {
                builder.Dispose();
            }
        }

        [Test]
        public void Watcher_SkipsThePublishThatFollowsAnUndo()
        {
            var arrangement = Rig(2);
            var child = Children(arrangement)[0];
            var scene = arrangement.gameObject.scene;

            var builder = new ObjectChangeEventStream.Builder(Allocator.Temp);
            try
            {
                var structure = new ChangeGameObjectStructureEventArgs(arrangement.gameObject.GetInstanceID(), scene);
                builder.PushChangeGameObjectStructureEvent(ref structure);
                var stream = builder.ToStream(Allocator.Temp);
                try
                {
                    AlpsArrangementWatcher.NoteUndoRedo();
                    AlpsArrangementWatcher.Handle(ref stream);
                    AssertPosition(new Vector3(0f, 0f, 7f), child);
                    Assert.That(AlpsArrangementWatcher.SkipsNextPublish, Is.False);

                    AlpsArrangementWatcher.Handle(ref stream);
                    AssertPosition(new Vector3(-2f, 0f, 0f), child);
                }
                finally
                {
                    stream.Dispose();
                }
            }
            finally
            {
                builder.Dispose();
            }
        }

        // ---------------------------------------------------------------- handles

        [Test]
        public void Handles_TurnADraggedPointIntoAValue()
        {
            var radiusLimit = AlpsArrangementSettings.RadiusLimit;
            var sizeLimit = AlpsArrangementSettings.SizeLimit;
            Assert.That(AlpsArrangementHandles.RadiusAt(new Vector3(0f, 0f, 4f), 0f, 0f, radiusLimit), Is.EqualTo(4f).Within(1e-4f));
            Assert.That(AlpsArrangementHandles.RadiusAt(new Vector3(3f, 1f, 0.5f), 90f, 0f, radiusLimit), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(AlpsArrangementHandles.RadiusAt(new Vector3(0f, 2f, 4f), 0f, 1f, radiusLimit), Is.EqualTo(3f).Within(1e-4f),
                "The dot sits on the path, the offset further out than the radius.");
            Assert.That(AlpsArrangementHandles.RadiusAt(new Vector3(0f, 0f, 50f), 0f, 0f, radiusLimit), Is.EqualTo(radiusLimit.y));
            Assert.That(AlpsArrangementHandles.RadiusAt(new Vector3(0f, 0f, -2f), 0f, 0f, radiusLimit), Is.EqualTo(0f));
            Assert.That(AlpsArrangementHandles.ExtentAt(2.5f, 0f, sizeLimit), Is.EqualTo(5f).Within(1e-4f));
            Assert.That(AlpsArrangementHandles.ExtentAt(3f, 0.5f, sizeLimit), Is.EqualTo(5f).Within(1e-4f));
            Assert.That(AlpsArrangementHandles.ExtentAt(-1f, 0f, sizeLimit), Is.EqualTo(0f));
        }

        [TestCase(AlpsArrangementShape.Line, AlpsArrangementSpacing.Ends)]
        [TestCase(AlpsArrangementShape.Circle, AlpsArrangementSpacing.Ends)]
        [TestCase(AlpsArrangementShape.Polygon, AlpsArrangementSpacing.Centered)]
        [TestCase(AlpsArrangementShape.Rectangle, AlpsArrangementSpacing.Centered)]
        public void Handles_OutlineRunsThroughTheChildren(AlpsArrangementShape shape, AlpsArrangementSpacing spacing)
        {
            // Lifted and pushed out, the path goes where the children are, not along the
            // container's plane under them. Corners are left out: a corner child steps out
            // between its two edges, inside the corner of the moved edges.
            var settings = new AlpsArrangementSettings { shape = shape, spacing = spacing, sides = 6 };
            settings.width.value = 4f;
            settings.depth.value = 2f;
            settings.height.value = 1.5f;
            settings.outward.value = 0.5f;

            var points = new List<Vector3>();
            var closed = AlpsArrangementHandles.Outline(settings, points);

            var layout = new float[AlpsArrangementEvaluator.LayoutStride];
            var values = new float[AlpsArrangementEvaluator.ValueCount];
            var frame = new Vector3[AlpsArrangementEvaluator.SlotFrameLength];
            settings.WriteLayout(layout);
            for (var i = 0; i < 6; i++)
            {
                settings.ResolveValues(i, 6, values);
                AlpsArrangementEvaluator.EvaluateSlot(layout, values, i, 6, frame);
                var slot = frame[AlpsArrangementEvaluator.SlotPosition];
                Assert.That(DistanceToPath(slot, points, closed), Is.LessThan(0.01f), $"{shape} slot {i} at {slot:F3}");
            }
        }

        [Test]
        public void Handles_OutlineFollowsTheShape()
        {
            var points = new List<Vector3>();
            var settings = new AlpsArrangementSettings();

            Assert.That(AlpsArrangementHandles.Outline(settings, points), Is.False);
            Assert.That(points, Is.Empty, "Off has no path.");

            settings.shape = AlpsArrangementShape.Line;
            Assert.That(AlpsArrangementHandles.Outline(settings, points), Is.False);
            CollectionAssert.AreEqual(new[] { settings.lineStart, settings.lineEnd }, points);

            settings.shape = AlpsArrangementShape.Rectangle;
            settings.width.value = 4f;
            settings.depth.value = 2f;
            Assert.That(AlpsArrangementHandles.Outline(settings, points), Is.True);
            CollectionAssert.AreEqual(
                new[] { new Vector3(-2f, 0f, 1f), new Vector3(2f, 0f, 1f), new Vector3(2f, 0f, -1f), new Vector3(-2f, 0f, -1f) },
                points);

            settings.shape = AlpsArrangementShape.Circle;
            settings.radius.value = 2f;
            Assert.That(AlpsArrangementHandles.Outline(settings, points), Is.True);
            Assert.That(points.All(point => Mathf.Abs(point.magnitude - 2f) < 1e-4f), Is.True);

            settings.sweep.value = 90f;
            Assert.That(AlpsArrangementHandles.Outline(settings, points), Is.False, "An arc stays open.");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// A container laying out on a line from (-2,0,0) to (2,0,0) with
        /// <paramref name="count"/> children placed off it at (i, 0, 7).
        /// </summary>
        private static AlpsContainer Rig(int count, bool initialized = true, AlpsArrangementShape shape = AlpsArrangementShape.Line)
        {
            var rig = new GameObject("Rig");
            for (var i = 0; i < count; i++)
            {
                var child = new GameObject($"Child {i}").transform;
                child.SetParent(rig.transform, false);
                child.localPosition = new Vector3(i, 0f, 7f);
            }

            var arrangement = rig.AddComponent<AlpsContainer>();
            arrangement.settings.shape = shape;
            arrangement.Initialized = initialized;
            return arrangement;
        }

        private static Transform[] Children(AlpsContainer arrangement)
        {
            return Enumerable.Range(0, arrangement.transform.childCount).Select(arrangement.transform.GetChild).ToArray();
        }

        private static float DistanceToPath(Vector3 point, List<Vector3> path, bool closed)
        {
            var nearest = float.MaxValue;
            var segments = closed ? path.Count : path.Count - 1;
            for (var i = 0; i < segments; i++)
            {
                var a = path[i];
                var b = path[(i + 1) % path.Count];
                var along = b - a;
                var t = along.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(point - a, along) / along.sqrMagnitude) : 0f;
                nearest = Mathf.Min(nearest, Vector3.Distance(point, a + along * t));
            }

            return nearest;
        }

        private static void AssertCollects(AlpsContainer arrangement, ObjectChangeKind kind, int id, int idA = 0, int idB = 0)
        {
            var set = new HashSet<AlpsContainer>();
            AlpsArrangementWatcher.CollectFor(kind, id, idA, idB, set);
            Assert.That(set, Does.Contain(arrangement), kind.ToString());
        }

        private static void AssertPosition(Vector3 expected, Transform transform)
        {
            Assert.That(Vector3.Distance(expected, transform.localPosition), Is.LessThan(1e-4f),
                $"{transform.name} expected {expected:F3} but was {transform.localPosition:F3}");
        }
    }
}
