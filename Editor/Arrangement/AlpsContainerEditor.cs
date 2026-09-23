using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Hosts <see cref="AlpsArrangementView"/> on a container and draws its handles.
    ///
    /// The view edits the settings in place, so undo works like the clip inspector's: the
    /// component and its children's transforms are snapshotted when an interaction starts,
    /// and every edit then lays the children out without recording more. Undo replaces the
    /// settings instance, so the view is rebuilt after it.
    /// </summary>
    [CustomEditor(typeof(AlpsContainer))]
    public class AlpsContainerEditor : UnityEditor.Editor
    {
        private AlpsArrangementView _view;

        public override VisualElement CreateInspectorGUI()
        {
            var container = (AlpsContainer)target;
            var host = new VisualElement();

            // A container added by a script without undo, or one that was a fixture group.
            if (AlpsArrangementLayout.CanRun)
            {
                AlpsArrangementLayout.Refresh(container, recordUndo: true);
            }

            void Rebuild()
            {
                host.Clear();
                if (container == null)
                {
                    return;
                }

                if (container.settings == null)
                {
                    container.settings = new AlpsArrangementSettings();
                }

                _view = new AlpsArrangementView(container.settings);
                _view.RegisterCallback<PointerDownEvent>(_ => Snapshot(container), TrickleDown.TrickleDown);
                _view.RegisterCallback<FocusInEvent>(_ => Snapshot(container), TrickleDown.TrickleDown);
                _view.Changed += () => AlpsArrangementLayout.CommitEdit(container);
                host.Add(_view);
            }

            host.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += Rebuild);
            host.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= Rebuild);
            Rebuild();
            return host;
        }

        private void OnSceneGUI()
        {
            var container = (AlpsContainer)target;
            if (container == null || !container.Arranges || !AlpsArrangementLayout.CanRun)
            {
                return;
            }

            if (AlpsArrangementHandles.OnSceneGUI(container))
            {
                _view?.Reload();
            }
        }

        private static void Snapshot(AlpsContainer container)
        {
            Undo.RegisterCompleteObjectUndo(AlpsArrangementLayout.UndoTargets(container), "Edit Container");
        }
    }
}
