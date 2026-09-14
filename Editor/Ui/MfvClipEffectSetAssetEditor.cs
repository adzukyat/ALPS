using UnityEditor;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Ui.Editor
{
    /// <summary>
    /// Hosts <see cref="MfvClipInspectorView"/> on the authoring asset.
    ///
    /// The view mutates the model object directly rather than going through
    /// SerializedProperty bindings, so undo has to be handled explicitly: snapshot before
    /// an interaction starts, and rebuild afterwards. Deserializing an undo step replaces
    /// the managed <see cref="MfvClipEffectSet"/> instance, so a stale view would keep
    /// editing an object the asset no longer references.
    /// </summary>
    [CustomEditor(typeof(MfvClipEffectSetAsset))]
    public class MfvClipEffectSetAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var asset = (MfvClipEffectSetAsset)target;
            var host = new VisualElement();

            void Rebuild()
            {
                host.Clear();

                if (asset == null)
                {
                    return;
                }

                asset.data ??= new MfvClipEffectSet();

                var view = new MfvClipInspectorView(asset.data);
                view.RegisterCallback<PointerDownEvent>(
                    _ => Undo.RegisterCompleteObjectUndo(asset, "Edit Clip Effects"),
                    TrickleDown.TrickleDown);
                view.Changed += () => EditorUtility.SetDirty(asset);

                host.Add(view);
            }

            void OnUndoRedo()
            {
                Rebuild();
            }

            host.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
            host.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);

            Rebuild();
            return host;
        }
    }
}
