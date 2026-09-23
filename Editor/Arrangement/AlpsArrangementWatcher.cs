using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Keeps containers up to date while the scene is edited. Every change Unity records
    /// through undo is published once per frame, and the containers it touches are laid
    /// out again: a child added, removed, reordered, reparented or moved by hand, a fixture
    /// added below one, the container itself added or reset. Any of these can change which
    /// fixtures a track drives or their order, so the preview compiles the show again.
    ///
    /// A scene that opens with containers that were fixture groups gets their fixture lists
    /// turned into the children's order.
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

        private static readonly HashSet<AlpsContainer> Pending = new HashSet<AlpsContainer>();
        private static int _undoSkip;

        static AlpsArrangementWatcher()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnUpdate;
            EditorSceneManager.sceneOpened += (scene, _) => MigrateScene(scene);

            // Scenes that were open before this domain reload.
            EditorApplication.delayCall += () =>
            {
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    MigrateScene(SceneManager.GetSceneAt(i));
                }
            };
        }

        /// <summary>
        /// Turns the fixture list of every container in <paramref name="scene"/> that was a
        /// fixture group into the order of its children. Returns how many there were.
        /// </summary>
        public static int MigrateScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !AlpsArrangementLayout.CanRun)
            {
                return 0;
            }

            var migrated = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var container in root.GetComponentsInChildren<AlpsContainer>(true))
                {
                    if (AlpsArrangementLayout.MigrateFixtureOrder(container, recordUndo: false))
                    {
                        migrated++;
                    }
                }
            }

            if (migrated > 0)
            {
                AlpsPreviewDriver.MarkDirty();
            }

            return migrated;
        }

        /// <summary>True while the changes published next come from an undo or redo.</summary>
        public static bool SkipsNextPublish => _undoSkip > 0;

        /// <summary>
        /// Adds the containers that a change of <paramref name="kind"/> can move. The ids
        /// are the event's own: the changed object, then the parent a destroyed object left,
        /// or the previous and the new parent of a reparented one.
        /// </summary>
        public static void CollectFor(ObjectChangeKind kind, int id, int idA, int idB, HashSet<AlpsContainer> into)
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

        /// <summary>Adds the containers every event in <paramref name="stream"/> can move.</summary>
        public static void Collect(ref ObjectChangeEventStream stream, HashSet<AlpsContainer> into)
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
        /// containers are queued and laid out unless a drag is still going on.
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

        /// <summary>Lays out every queued container, unless a handle is still held.</summary>
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

            var containers = Pending.ToArray();
            Pending.Clear();
            foreach (var container in containers)
            {
                if (container != null)
                {
                    AlpsArrangementLayout.Refresh(container, recordUndo: true);
                }
            }

            AlpsPreviewDriver.MarkDirty();
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

        private static void AddAncestors(GameObject gameObject, HashSet<AlpsContainer> into)
        {
            if (gameObject == null)
            {
                return;
            }

            for (var current = gameObject.transform; current != null; current = current.parent)
            {
                if (current.TryGetComponent<AlpsContainer>(out var container))
                {
                    into.Add(container);
                }
            }
        }

        private static void AddSelfAndParent(GameObject gameObject, HashSet<AlpsContainer> into)
        {
            if (gameObject == null)
            {
                return;
            }

            if (gameObject.TryGetComponent<AlpsContainer>(out var own))
            {
                into.Add(own);
            }

            var parent = gameObject.transform.parent;
            if (parent != null && parent.TryGetComponent<AlpsContainer>(out var container))
            {
                into.Add(container);
            }
        }

        private static void AddDescendants(GameObject gameObject, HashSet<AlpsContainer> into)
        {
            if (gameObject == null)
            {
                return;
            }

            foreach (var container in gameObject.GetComponentsInChildren<AlpsContainer>(true))
            {
                into.Add(container);
            }
        }
    }
}
