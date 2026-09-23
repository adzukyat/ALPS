using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Hosts <see cref="AlpsArrangementView"/> on an arrangement and draws its handles.
    ///
    /// The view edits the settings in place, so undo works like the clip inspector's: the
    /// component, its children's transforms and the fixture group are snapshotted when an
    /// interaction starts, and every edit then lays the children out without recording
    /// more. Undo replaces the settings instance, so the view is rebuilt after it.
    /// </summary>
    [CustomEditor(typeof(AlpsArrangement))]
    public class AlpsArrangementEditor : UnityEditor.Editor
    {
        private AlpsArrangementView _view;

        public override VisualElement CreateInspectorGUI()
        {
            var arrangement = (AlpsArrangement)target;
            var host = new VisualElement();

            // An arrangement added by a script without undo has not been laid out yet.
            if (!arrangement.Initialized && AlpsArrangementLayout.CanRun)
            {
                AlpsArrangementLayout.Refresh(arrangement, recordUndo: true);
            }

            void Rebuild()
            {
                host.Clear();
                if (arrangement == null)
                {
                    return;
                }

                if (arrangement.settings == null)
                {
                    arrangement.settings = new AlpsArrangementSettings();
                }

                _view = new AlpsArrangementView(arrangement.settings);
                _view.RegisterCallback<PointerDownEvent>(_ => Snapshot(arrangement), TrickleDown.TrickleDown);
                _view.RegisterCallback<FocusInEvent>(_ => Snapshot(arrangement), TrickleDown.TrickleDown);
                _view.Changed += () => AlpsArrangementLayout.CommitEdit(arrangement);
                _view.SyncRequested += () =>
                {
                    Snapshot(arrangement);
                    arrangement.SyncFixtureGroup = true;
                    AlpsArrangementLayout.CommitEdit(arrangement);
                    RefreshNotice(arrangement);
                };
                RefreshNotice(arrangement);
                host.Add(_view);
            }

            host.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += Rebuild);
            host.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= Rebuild);
            Rebuild();
            return host;
        }

        private void OnSceneGUI()
        {
            var arrangement = (AlpsArrangement)target;
            if (arrangement == null || !AlpsArrangementLayout.CanRun)
            {
                return;
            }

            if (AlpsArrangementHandles.OnSceneGUI(arrangement))
            {
                _view?.Reload();
            }
        }

        private void RefreshNotice(AlpsArrangement arrangement)
        {
            if (_view != null)
            {
                _view.SyncNoticeVisible = !arrangement.SyncFixtureGroup &&
                                          arrangement.GetComponent<AlpsFixtureGroup>() != null;
            }
        }

        private static void Snapshot(AlpsArrangement arrangement)
        {
            Undo.RegisterCompleteObjectUndo(AlpsArrangementLayout.UndoTargets(arrangement), "Edit Arrangement");
        }
    }
}
