using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Keeps arrangements laid out while the scene is edited. Every change Unity records
    /// through undo is published once per frame, and the arrangements it touches are laid
    /// out again: a child added, removed, reordered, reparented or moved by hand, a fixture
    /// added below one, the arrangement itself added or reset.
    ///
    /// The layout records into the undo group that is current, so it lands with the change
    /// that caused it. Undo and redo restore the children together with that change, so the
    /// publish that follows them is skipped: laying out again there would record over the
    /// redo and trap Ctrl+Z on the same step.
    ///
    /// While a handle is dragged the layout waits for the release, so a child pulled out of
    /// place does not flicker back every frame.
    /// </summary>
    [InitializeOnLoad]
    public static class AlpsArrangementWatcher
    {
        /// <summary>Editor updates after an undo or redo during which a publish is its own.</summary>
        private const int UndoSkipTicks = 2;

        private static readonly HashSet<AlpsArrangement> Pending = new HashSet<AlpsArrangement>();
        private static int _undoSkip;

        static AlpsArrangementWatcher()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnUpdate;
        }

        /// <summary>True while the changes published next come from an undo or redo.</summary>
        public static bool SkipsNextPublish => _undoSkip > 0;

        /// <summary>
        /// Adds the arrangements that a change of <paramref name="kind"/> can move. The ids
        /// are the event's own: the changed object, then the parent a destroyed object left,
        /// or the previous and the new parent of a reparented one.
        /// </summary>
        public static void CollectFor(ObjectChangeKind kind, int id, int idA, int idB, HashSet<AlpsArrangement> into)
        {
            switch (kind)
            {
                case ObjectChangeKind.CreateGameObjectHierarchy:
                case ObjectChangeKind.ChangeChildrenOrder:
                case ObjectChangeKind.ChangeGameObjectStructure:
                    AddAncestors(GameObjectOf(id), into);
                    break;
                case ObjectChangeKind.DestroyGameObjectHierarchy:
                    AddAncestors(GameObjectOf(idA), into);
                    break;
                case ObjectChangeKind.ChangeGameObjectParent:
                    AddAncestors(GameObjectOf(id), into);
                    AddAncestors(GameObjectOf(idA), into);
                    AddAncestors(GameObjectOf(idB), into);
                    break;
                case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    AddSelfAndParent(GameObjectOf(id), into);
                    break;
                case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                case ObjectChangeKind.UpdatePrefabInstances:
                    var gameObject = GameObjectOf(id);
                    AddAncestors(gameObject, into);
                    AddDescendants(gameObject, into);
                    break;
            }
        }

        /// <summary>Adds the arrangements every event in <paramref name="stream"/> can move.</summary>
        public static void Collect(ref ObjectChangeEventStream stream, HashSet<AlpsArrangement> into)
        {
            for (var i = 0; i < stream.length; i++)
            {
                var kind = stream.GetEventType(i);
                switch (kind)
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        stream.GetCreateGameObjectHierarchyEvent(i, out var created);
                        CollectFor(kind, created.instanceId, 0, 0, into);
                        break;
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        stream.GetDestroyGameObjectHierarchyEvent(i, out var destroyed);
                        CollectFor(kind, destroyed.instanceId, destroyed.parentInstanceId, 0, into);
                        break;
                    case ObjectChangeKind.ChangeChildrenOrder:
                        stream.GetChangeChildrenOrderEvent(i, out var reordered);
                        CollectFor(kind, reordered.instanceId, 0, 0, into);
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out var reparented);
                        CollectFor(kind, reparented.instanceId, reparented.previousParentInstanceId,
                            reparented.newParentInstanceId, into);
                        break;
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var properties);
                        CollectFor(kind, properties.instanceId, 0, 0, into);
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out var structure);
                        CollectFor(kind, structure.instanceId, 0, 0, into);
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var hierarchy);
                        CollectFor(kind, hierarchy.instanceId, 0, 0, into);
                        break;
                    case ObjectChangeKind.UpdatePrefabInstances:
                        stream.GetUpdatePrefabInstancesEvent(i, out var prefabs);
                        var instanceIds = prefabs.instanceIds;
                        for (var j = 0; j < instanceIds.Length; j++)
                        {
                            CollectFor(kind, instanceIds[j], 0, 0, into);
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// Handles one publish: skipped right after an undo or redo, otherwise the touched
        /// arrangements are queued and laid out unless a drag is still going on.
        /// </summary>
        public static void Handle(ref ObjectChangeEventStream stream)
        {
            if (_undoSkip > 0)
            {
                _undoSkip = 0;
                return;
            }

            if (!AlpsArrangementLayout.CanRun)
            {
                return;
            }

            Collect(ref stream, Pending);
            Flush();
        }

        /// <summary>Lays out every queued arrangement, unless a handle is still held.</summary>
        public static void Flush()
        {
            if (Pending.Count == 0)
            {
                return;
            }

            if (!AlpsArrangementLayout.CanRun)
            {
                Pending.Clear();
                return;
            }

            if (GUIUtility.hotControl != 0)
            {
                return;
            }

            var arrangements = Pending.ToArray();
            Pending.Clear();
            foreach (var arrangement in arrangements)
            {
                if (arrangement != null)
                {
                    AlpsArrangementLayout.Refresh(arrangement, recordUndo: true);
                }
            }
        }

        /// <summary>Marks the next publish as the undo's or redo's own. Tests call it directly.</summary>
        public static void NoteUndoRedo()
        {
            _undoSkip = UndoSkipTicks;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            Handle(ref stream);
        }

        private static void OnUndoRedo()
        {
            NoteUndoRedo();
        }

        private static void OnUpdate()
        {
            // An undo that changed nothing publishes nothing, so the skip wears off instead
            // of swallowing the next real change.
            if (_undoSkip > 0)
            {
                _undoSkip--;
            }

            Flush();
        }

        private static GameObject GameObjectOf(int instanceId)
        {
            if (instanceId == 0)
            {
                return null;
            }

            var target = EditorUtility.InstanceIDToObject(instanceId);
            if (target is GameObject gameObject)
            {
                return gameObject;
            }

            return target is Component component ? component.gameObject : null;
        }

        private static void AddAncestors(GameObject gameObject, HashSet<AlpsArrangement> into)
        {
            if (gameObject == null)
            {
                return;
            }

            for (var current = gameObject.transform; current != null; current = current.parent)
            {
                if (current.TryGetComponent<AlpsArrangement>(out var arrangement))
                {
                    into.Add(arrangement);
                }
            }
        }

        private static void AddSelfAndParent(GameObject gameObject, HashSet<AlpsArrangement> into)
        {
            if (gameObject == null)
            {
                return;
            }

            if (gameObject.TryGetComponent<AlpsArrangement>(out var own))
            {
                into.Add(own);
            }

            var parent = gameObject.transform.parent;
            if (parent != null && parent.TryGetComponent<AlpsArrangement>(out var arrangement))
            {
                into.Add(arrangement);
            }
        }

        private static void AddDescendants(GameObject gameObject, HashSet<AlpsArrangement> into)
        {
            if (gameObject == null)
            {
                return;
            }

            foreach (var arrangement in gameObject.GetComponentsInChildren<AlpsArrangement>(true))
            {
                into.Add(arrangement);
            }
        }
    }
}
