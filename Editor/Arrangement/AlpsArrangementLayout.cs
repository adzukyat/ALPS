using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Lays an <see cref="AlpsArrangement"/>'s children out and keeps the fixture group on
    /// the same object in their order.
    ///
    /// Only children whose pose actually differs are written. That keeps prefab instances
    /// free of empty overrides, and it is what lets the watcher run after every change
    /// without looping: a layout that is already in place writes nothing, so it records
    /// no undo and publishes no change of its own.
    /// </summary>
    public static class AlpsArrangementLayout
    {
        public const string UndoName = "Arrange Children";

        /// <summary>Squared distance under which a child counts as already in place.</summary>
        private const float PositionTolerance = 1e-10f;

        /// <summary>Degrees under which a child counts as already turned the right way.</summary>
        private const float RotationTolerance = 1e-3f;

        /// <summary>False in play mode and while building, where the scene is not being authored.</summary>
        public static bool CanRun =>
            !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer;

        /// <summary>A disabled arrangement leaves its children and the fixture group alone.</summary>
        public static bool IsActive(AlpsArrangement arrangement)
        {
            return arrangement != null && arrangement.enabled;
        }

        /// <summary>True while the fixture group next to the arrangement follows the children.</summary>
        public static bool SyncsFixtureGroup(AlpsArrangement arrangement)
        {
            return IsActive(arrangement) &&
                   arrangement.SyncFixtureGroup &&
                   arrangement.GetComponent<AlpsFixtureGroup>() != null;
        }

        /// <summary>
        /// Everything a layout of <paramref name="arrangement"/> can write: the component, its
        /// children's transforms and the fixture group. An edit snapshots these first.
        /// </summary>
        public static Object[] UndoTargets(AlpsArrangement arrangement)
        {
            var targets = new List<Object> { arrangement };
            var root = arrangement.transform;
            for (var i = 0; i < root.childCount; i++)
            {
                targets.Add(root.GetChild(i));
            }

            var group = arrangement.GetComponent<AlpsFixtureGroup>();
            if (group != null)
            {
                targets.Add(group);
            }

            return targets.ToArray();
        }

        /// <summary>
        /// Brings an arrangement up to date: the first layout on its first run, then the
        /// children's poses and the fixture group's order.
        /// </summary>
        public static void Refresh(AlpsArrangement arrangement, bool recordUndo)
        {
            if (!IsActive(arrangement))
            {
                return;
            }

            if (!arrangement.Initialized)
            {
                Initialize(arrangement, recordUndo);
            }

            Apply(arrangement, recordUndo);
            SyncFixtureGroup(arrangement, recordUndo);
        }

        /// <summary>
        /// Writes an edit made straight into the settings: marks the component changed and
        /// lays the children out. The caller has already snapshotted
        /// <see cref="UndoTargets"/>, so nothing more is recorded.
        /// </summary>
        public static void CommitEdit(AlpsArrangement arrangement)
        {
            MarkChanged(arrangement);
            Apply(arrangement, recordUndo: false);
            SyncFixtureGroup(arrangement, recordUndo: false);
        }

        /// <summary>Puts every child where the settings place it. Returns how many moved.</summary>
        public static int Apply(AlpsArrangement arrangement, bool recordUndo)
        {
            if (!IsActive(arrangement))
            {
                return 0;
            }

            if (arrangement.settings == null)
            {
                arrangement.settings = new AlpsArrangementSettings();
            }

            var settings = arrangement.settings;
            settings.EnsureLimits();

            var root = arrangement.transform;
            var count = root.childCount;
            var layout = new float[AlpsArrangementEvaluator.LayoutStride];
            var values = new float[AlpsArrangementEvaluator.ValueCount];
            var frame = new Vector3[AlpsArrangementEvaluator.SlotFrameLength];
            settings.WriteLayout(layout);

            var moved = 0;
            for (var i = 0; i < count; i++)
            {
                var child = root.GetChild(i);
                settings.ResolveValues(i, count, values);
                var rotation = AlpsArrangementEvaluator.EvaluateSlot(layout, values, i, count, frame);
                var position = frame[AlpsArrangementEvaluator.SlotPosition];
                if (!Differs(child, position, rotation) || IsAnimated(child))
                {
                    continue;
                }

                if (recordUndo)
                {
                    Undo.RecordObject(child, UndoName);
                }

                child.localPosition = position;
                child.localRotation = rotation;
                MarkChanged(child);
                moved++;
            }

            return moved;
        }

        /// <summary>
        /// The first layout of an arrangement added to children that are already placed. The
        /// fixture group keeps its order, because the children are reordered to match it,
        /// and the line starts on the first and last child, so a hand placed row stays where
        /// it is. A shared rotation of every child is kept too.
        /// </summary>
        public static void Initialize(AlpsArrangement arrangement, bool recordUndo)
        {
            if (recordUndo)
            {
                Undo.RecordObject(arrangement, UndoName);
            }

            if (arrangement.settings == null)
            {
                arrangement.settings = new AlpsArrangementSettings();
            }

            arrangement.settings.EnsureLimits();
            AdoptFixtureGroupOrder(arrangement, recordUndo);
            SeedFromChildren(arrangement);
            arrangement.Initialized = true;
            MarkChanged(arrangement);
        }

        /// <summary>
        /// The fixture list a synced group holds: every fixture under the arrangement in
        /// hierarchy order, which is the slot order, then any fixture the list already had
        /// from outside the arrangement, so nothing it held is dropped.
        /// </summary>
        public static List<AlpsFixture> ExpectedFixtures(AlpsArrangement arrangement, AlpsFixtureGroup group)
        {
            var expected = arrangement.GetComponentsInChildren<AlpsFixture>(true).ToList();
            if (group != null && group.fixtures != null)
            {
                foreach (var fixture in group.fixtures)
                {
                    if (fixture != null &&
                        !fixture.transform.IsChildOf(arrangement.transform) &&
                        !expected.Contains(fixture))
                    {
                        expected.Add(fixture);
                    }
                }
            }

            return expected;
        }

        /// <summary>True when the group already lists its fixtures in the children's order.</summary>
        public static bool FixtureGroupMatches(AlpsArrangement arrangement, AlpsFixtureGroup group)
        {
            return group.fixtures != null && group.fixtures.SequenceEqual(ExpectedFixtures(arrangement, group));
        }

        /// <summary>Writes the children's order into the fixture group. Returns whether it changed.</summary>
        public static bool SyncFixtureGroup(AlpsArrangement arrangement, bool recordUndo)
        {
            if (!SyncsFixtureGroup(arrangement))
            {
                return false;
            }

            var group = arrangement.GetComponent<AlpsFixtureGroup>();
            if (FixtureGroupMatches(arrangement, group))
            {
                return false;
            }

            var expected = ExpectedFixtures(arrangement, group);
            if (recordUndo)
            {
                Undo.RecordObject(group, UndoName);
            }

            group.fixtures = expected;
            MarkChanged(group);
            return true;
        }

        /// <summary>
        /// The direct child of <paramref name="root"/> that <paramref name="descendant"/> sits
        /// in, or null when it is the root itself or outside it.
        /// </summary>
        public static Transform SlotOf(Transform root, Transform descendant)
        {
            for (var current = descendant; current != null; current = current.parent)
            {
                if (current.parent == root)
                {
                    return current;
                }
            }

            return null;
        }

        /// <summary>
        /// Orders the children the way the fixture group lists their fixtures, so syncing the
        /// group afterwards keeps its order. Children with no listed fixture follow in the
        /// order they had. Children of a prefab instance cannot be reordered, so the group
        /// keeps its list unsynced until the user syncs it.
        /// </summary>
        private static void AdoptFixtureGroupOrder(AlpsArrangement arrangement, bool recordUndo)
        {
            var group = arrangement.GetComponent<AlpsFixtureGroup>();
            if (group == null || group.fixtures == null)
            {
                return;
            }

            var root = arrangement.transform;
            var ordered = new List<Transform>();
            foreach (var fixture in group.fixtures)
            {
                var slot = fixture != null ? SlotOf(root, fixture.transform) : null;
                if (slot != null && !ordered.Contains(slot))
                {
                    ordered.Add(slot);
                }
            }

            if (ordered.Count == 0)
            {
                return;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (!ordered.Contains(child))
                {
                    ordered.Add(child);
                }
            }

            var inOrder = true;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].GetSiblingIndex() != i)
                {
                    inOrder = false;
                    break;
                }
            }

            if (inOrder)
            {
                return;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(arrangement.gameObject))
            {
                arrangement.SyncFixtureGroup = false;
                return;
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].GetSiblingIndex() == i)
                {
                    continue;
                }

                if (recordUndo)
                {
                    Undo.SetSiblingIndex(ordered[i], i, UndoName);
                }
                else
                {
                    ordered[i].SetSiblingIndex(i);
                }
            }
        }

        /// <summary>
        /// Starts the line on the first child and ends it on the last, and keeps a rotation
        /// every child shares. A lone child gets a line through it.
        /// </summary>
        private static void SeedFromChildren(AlpsArrangement arrangement)
        {
            var settings = arrangement.settings;
            var root = arrangement.transform;
            var count = root.childCount;
            if (count == 0)
            {
                return;
            }

            var first = root.GetChild(0);
            if (count == 1)
            {
                settings.lineStart = first.localPosition + settings.lineStart;
                settings.lineEnd = first.localPosition + settings.lineEnd;
            }
            else
            {
                settings.lineStart = first.localPosition;
                settings.lineEnd = root.GetChild(count - 1).localPosition;
            }

            var rotation = first.localRotation;
            for (var i = 1; i < count; i++)
            {
                if (Quaternion.Angle(root.GetChild(i).localRotation, rotation) > RotationTolerance)
                {
                    return;
                }
            }

            var euler = rotation.eulerAngles;
            settings.rotationX.value = Mathf.DeltaAngle(0f, euler.x);
            settings.rotationY.value = Mathf.DeltaAngle(0f, euler.y);
            settings.rotationZ.value = Mathf.DeltaAngle(0f, euler.z);
        }

        private static bool Differs(Transform child, Vector3 position, Quaternion rotation)
        {
            return (child.localPosition - position).sqrMagnitude > PositionTolerance ||
                   Quaternion.Angle(child.localRotation, rotation) > RotationTolerance;
        }

        /// <summary>A transform a Timeline preview is animating is left to the preview.</summary>
        private static bool IsAnimated(Transform child)
        {
            return AnimationMode.InAnimationMode() &&
                   (AnimationMode.IsPropertyAnimated(child, "m_LocalPosition.x") ||
                    AnimationMode.IsPropertyAnimated(child, "m_LocalRotation.x"));
        }

        /// <summary>
        /// Marks an object edited outside undo: dirty, recorded as a prefab override when it
        /// sits in an instance, and its scene dirty.
        /// </summary>
        private static void MarkChanged(Object target)
        {
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            if (target is Component component && !EditorApplication.isPlaying)
            {
                var scene = component.gameObject.scene;
                if (scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }
        }

        [MenuItem("CONTEXT/AlpsArrangement/再配置")]
        private static void Rearrange(MenuCommand command)
        {
            if (command.context is AlpsArrangement arrangement)
            {
                Undo.RegisterCompleteObjectUndo(UndoTargets(arrangement), UndoName);
                Refresh(arrangement, recordUndo: false);
            }
        }
    }
}
