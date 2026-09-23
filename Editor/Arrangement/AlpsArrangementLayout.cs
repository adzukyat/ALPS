using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Lays an <see cref="AlpsContainer"/>'s children out, and turns the fixture list of the
    /// fixture group a container used to be into the order of its children.
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

        /// <summary>
        /// Everything a layout of <paramref name="container"/> can write: the component and its
        /// children's transforms. An edit snapshots these first.
        /// </summary>
        public static Object[] UndoTargets(AlpsContainer container)
        {
            var targets = new List<Object> { container };
            var root = container.transform;
            for (var i = 0; i < root.childCount; i++)
            {
                targets.Add(root.GetChild(i));
            }

            return targets.ToArray();
        }

        /// <summary>
        /// Brings a container up to date: an old fixture list turned into the children's
        /// order, then, with a shape on, the first layout on its first run and the children's
        /// poses.
        /// </summary>
        public static void Refresh(AlpsContainer container, bool recordUndo)
        {
            if (container == null)
            {
                return;
            }

            MigrateFixtureOrder(container, recordUndo);
            if (!container.Arranges)
            {
                return;
            }

            if (!container.Initialized)
            {
                Initialize(container, recordUndo);
            }

            Apply(container, recordUndo);
        }

        /// <summary>
        /// Writes an edit made straight into the settings: marks the component changed and
        /// lays the children out, starting from the children as they are the first time a
        /// shape is picked. The caller has already snapshotted <see cref="UndoTargets"/>, so
        /// nothing more is recorded.
        /// </summary>
        public static void CommitEdit(AlpsContainer container)
        {
            MarkChanged(container);
            if (!container.Arranges)
            {
                return;
            }

            if (!container.Initialized)
            {
                Initialize(container, recordUndo: false);
            }

            Apply(container, recordUndo: false);
        }

        /// <summary>
        /// Puts every child where the settings place it. Returns how many moved. A container
        /// whose shape is off moves nothing.
        /// </summary>
        public static int Apply(AlpsContainer container, bool recordUndo)
        {
            if (container == null || !container.Arranges)
            {
                return 0;
            }

            var settings = container.settings;
            settings.EnsureLimits();

            var root = container.transform;
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
        /// The first layout of children that are already placed: the line starts on the first
        /// and last child, so a hand placed row stays where it is, and a rotation every child
        /// shares is kept too.
        /// </summary>
        public static void Initialize(AlpsContainer container, bool recordUndo)
        {
            if (recordUndo)
            {
                Undo.RecordObject(container, UndoName);
            }

            if (container.settings == null)
            {
                container.settings = new AlpsArrangementSettings();
            }

            container.settings.EnsureLimits();
            SeedFromChildren(container);
            container.Initialized = true;
            MarkChanged(container);
        }

        /// <summary>
        /// Turns the fixture list a container kept from when it was a fixture group into the
        /// order of its children, so the show keeps its fixture order, and clears the list.
        /// Returns whether there was a list.
        ///
        /// At every level below the container, the children holding listed fixtures move to
        /// the front in the order the list first names them, and the rest follow in the order
        /// they had. Children of a prefab instance cannot be moved, and listed fixtures outside
        /// the container are no longer driven, so both are reported in the console.
        /// </summary>
        public static bool MigrateFixtureOrder(AlpsContainer container, bool recordUndo)
        {
            var legacy = container.LegacyFixtures;
            if (legacy == null || legacy.Count == 0)
            {
                return false;
            }

            if (recordUndo)
            {
                Undo.RecordObject(container, UndoName);
            }

            var root = container.transform;
            var rank = new Dictionary<Transform, int>();
            var outside = new List<string>();
            for (var i = 0; i < legacy.Count; i++)
            {
                var fixture = legacy[i];
                if (fixture == null)
                {
                    continue;
                }

                if (!fixture.transform.IsChildOf(root))
                {
                    outside.Add(fixture.name);
                    continue;
                }

                for (var current = fixture.transform; current != root; current = current.parent)
                {
                    if (!rank.ContainsKey(current))
                    {
                        rank.Add(current, i);
                    }
                }
            }

            var locked = new List<string>();
            OrderChildren(root, rank, recordUndo, locked);

            legacy.Clear();
            MarkChanged(container);

            if (locked.Count > 0)
            {
                Debug.LogWarning(
                    $"[ALPS] '{container.name}' now takes its fixture order from the hierarchy, but the children of {string.Join(", ", locked)} belong to a prefab and could not be reordered. Check the order of the fixtures below them.",
                    container);
            }

            if (outside.Count > 0)
            {
                Debug.LogWarning(
                    $"[ALPS] '{container.name}' drives the fixtures below it. {string.Join(", ", outside)} were listed in its fixture group but are not below it. Move them under it to keep driving them.",
                    container);
            }

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
        /// Orders <paramref name="parent"/>'s children by their rank, the first list position
        /// of a fixture inside them, then does the same one level down. Unranked children keep
        /// their order after the ranked ones.
        /// </summary>
        private static void OrderChildren(Transform parent, Dictionary<Transform, int> rank, bool recordUndo, List<string> locked)
        {
            var children = new List<Transform>();
            for (var i = 0; i < parent.childCount; i++)
            {
                children.Add(parent.GetChild(i));
            }

            var ranked = children.Where(rank.ContainsKey).OrderBy(child => rank[child]).ToList();
            if (ranked.Count == 0)
            {
                return;
            }

            var ordered = ranked.Concat(children.Where(child => !rank.ContainsKey(child))).ToList();
            if (!ordered.SequenceEqual(children))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(parent.gameObject) &&
                    ordered.Any(PrefabUtility.IsPartOfPrefabInstance))
                {
                    locked.Add(parent.name);
                }
                else
                {
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
            }

            foreach (var child in ranked)
            {
                OrderChildren(child, rank, recordUndo, locked);
            }
        }

        /// <summary>
        /// Starts the line on the first child and ends it on the last, and keeps a rotation
        /// every child shares. A lone child, or children all on one spot, get the default
        /// line moved onto them.
        /// </summary>
        private static void SeedFromChildren(AlpsContainer container)
        {
            var settings = container.settings;
            var root = container.transform;
            var count = root.childCount;
            if (count == 0)
            {
                return;
            }

            var first = root.GetChild(0).localPosition;
            var last = root.GetChild(count - 1).localPosition;
            if ((last - first).sqrMagnitude < PositionTolerance)
            {
                settings.lineStart = first + settings.lineStart;
                settings.lineEnd = first + settings.lineEnd;
            }
            else
            {
                settings.lineStart = first;
                settings.lineEnd = last;
            }

            var rotation = root.GetChild(0).localRotation;
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

        [MenuItem("CONTEXT/AlpsContainer/Rearrange Children")]
        private static void Rearrange(MenuCommand command)
        {
            if (command.context is AlpsContainer container)
            {
                Undo.RegisterCompleteObjectUndo(UndoTargets(container), UndoName);
                Refresh(container, recordUndo: false);
            }
        }

        [MenuItem("CONTEXT/AlpsContainer/Rearrange Children", true)]
        private static bool CanRearrange(MenuCommand command)
        {
            return command.context is AlpsContainer container && container.Arranges;
        }
    }
}
