using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Hosts <see cref="AlpsClipInspectorView"/> on a timeline clip.
    ///
    /// The timeline clip inspector draws the clip asset with IMGUI, so CreateInspectorGUI is
    /// never called there. OnInspectorGUI draws nothing and instead hangs the same view right
    /// after the IMGUI container the clip inspector runs in. Selecting the asset on its own
    /// still goes through CreateInspectorGUI.
    ///
    /// The clip inspector puts the asset editor under its own foldout title bar and stops
    /// calling OnInspectorGUI while that is collapsed. The view lives outside IMGUI, so the
    /// container handler is wrapped to notice a repaint without a draw and hide the view.
    ///
    /// The view mutates the model object directly rather than going through
    /// SerializedProperty bindings, so undo has to be handled explicitly: snapshot before
    /// an interaction starts, and rebuild afterwards. Deserializing an undo step replaces
    /// the managed <see cref="AlpsClipEffectSet"/> instance, so a stale view would keep
    /// editing an object the asset no longer references.
    /// </summary>
    [CustomEditor(typeof(AlpsTimelineClip))]
    public class AlpsTimelineClipInspector : UnityEditor.Editor
    {
        private VisualElement _injected;
        private IMGUIContainer _watched;
        private System.Action _watchedHandler;
        private System.Action _wrapper;
        private bool _drawnThisPass;

        public override VisualElement CreateInspectorGUI()
        {
            return BuildHost();
        }

        public override void OnInspectorGUI()
        {
            MarkDrawn();

            if (targets.Length > 1)
            {
                Detach();
                EditorGUILayout.HelpBox("複数のクリップは同時に編集できません。", MessageType.Info);
                return;
            }

            if (!AttachTo(AlpsImguiHost.Current()))
            {
                EditorGUILayout.HelpBox("このクリップのエディタを表示できませんでした。", MessageType.Warning);
                DrawDefaultInspector();
                return;
            }

            // The clip inspector disables its body while the timeline is locked.
            if (_injected != null)
            {
                _injected.SetEnabled(GUI.enabled);
            }
        }

        /// <summary>
        /// Puts the view right after <paramref name="container"/>, so it reads as the rest of
        /// that IMGUI block. False when there is nothing to hang it on.
        /// </summary>
        internal bool AttachTo(IMGUIContainer container)
        {
            return AttachTo(container, CanEditHierarchy());
        }

        internal bool AttachTo(IMGUIContainer container, bool canEditHierarchy)
        {
            if (container == null || container.parent == null)
            {
                Detach();
                return false;
            }

            if (_injected == null)
            {
                _injected = BuildHost();
            }

            var parent = container.parent;
            if (_injected.parent != parent && canEditHierarchy)
            {
                _injected.RemoveFromHierarchy();
                var index = parent.IndexOf(container);
                parent.Insert(index < 0 ? parent.childCount : index + 1, _injected);
            }

            Watch(container);
            return true;
        }

        /// <summary>Records that the clip inspector drew this editor in the current pass.</summary>
        internal void MarkDrawn()
        {
            _drawnThisPass = true;
        }

        /// <summary>
        /// Runs the watched container's own handler. On a repaint the view follows whether that
        /// handler reached OnInspectorGUI, which it does not while the foldout is collapsed.
        /// </summary>
        internal void RunContainerPass(bool repaint)
        {
            if (repaint)
            {
                _drawnThisPass = false;
            }

            _watchedHandler?.Invoke();

            if (repaint && _injected != null)
            {
                _injected.style.display = _drawnThisPass ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void Watch(IMGUIContainer container)
        {
            if (_watched == container && _wrapper != null && container.onGUIHandler == _wrapper)
            {
                return;
            }

            Unwatch();
            if (_wrapper == null)
            {
                _wrapper = () =>
                {
                    var current = Event.current;
                    RunContainerPass(current != null && current.type == EventType.Repaint);
                };
            }

            _watched = container;
            _watchedHandler = container.onGUIHandler;
            container.onGUIHandler = _wrapper;
        }

        private void Unwatch()
        {
            if (_watched != null && _watched.onGUIHandler == _wrapper)
            {
                _watched.onGUIHandler = _watchedHandler;
            }

            _watched = null;
            _watchedHandler = null;
        }

        /// <summary>
        /// The container also runs its handler while the panel measures itself, and the
        /// hierarchy is locked during that pass. Repainting happens after layout, so that is
        /// when the view can be moved. Outside IMGUI, as in tests, there is no such pass.
        /// </summary>
        private static bool CanEditHierarchy()
        {
            var current = Event.current;
            return current == null || current.type == EventType.Repaint;
        }

        private void OnDisable()
        {
            Detach();
        }

        private void Detach()
        {
            Unwatch();
            var injected = _injected;
            _injected = null;
            if (injected == null)
            {
                return;
            }

            if (CanEditHierarchy())
            {
                injected.RemoveFromHierarchy();
                return;
            }

            EditorApplication.delayCall += () => injected.RemoveFromHierarchy();
        }

        private VisualElement BuildHost()
        {
            var clip = (AlpsTimelineClip)target;
            var host = new VisualElement();

            void Rebuild()
            {
                host.Clear();

                if (clip == null)
                {
                    return;
                }

                clip.data ??= new AlpsClipEffectSet();
                var synced = clip.syncProfile && clip.profile != null;
                var edited = synced ? (Object)clip.profile : clip;
                var set = clip.EffectiveData;

                var view = new AlpsClipInspectorView(set);
                view.RegisterCallback<PointerDownEvent>(
                    _ => Undo.RegisterCompleteObjectUndo(edited, "Edit Clip Effects"),
                    TrickleDown.TrickleDown);
                view.Changed += () =>
                {
                    EditorUtility.SetDirty(edited);
                    NotifyPreview();
                };

                view.ProfileBody.Add(BuildProfile(clip, Rebuild));
                host.Add(view);
            }

            void OnUndoRedo()
            {
                NotifyPreview();
                Rebuild();
            }

            host.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
            host.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);

            Rebuild();
            return host;
        }

        private static VisualElement BuildProfile(AlpsTimelineClip clip, System.Action rebuild)
        {
            var root = new VisualElement();

            var field = new ObjectField("プロファイル")
            {
                objectType = typeof(AlpsClipProfile),
                allowSceneObjects = false,
                value = clip.profile,
            };
            field.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(clip, "Change Clip Profile");
                clip.profile = evt.newValue as AlpsClipProfile;
                EditorUtility.SetDirty(clip);
                NotifyPreview();
                rebuild();
            });
            root.Add(field);

            var sync = new Toggle("プロファイルに追従") { value = clip.syncProfile };
            sync.tooltip = "オンの間はプロファイルの効果を再生し、編集もプロファイルに書き込みます。";
            sync.SetEnabled(clip.profile != null);
            sync.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(clip, "Sync Clip Profile");
                clip.syncProfile = evt.newValue;
                EditorUtility.SetDirty(clip);
                NotifyPreview();
                rebuild();
            });
            root.Add(sync);

            var buttons = new VisualElement();
            buttons.AddToClassList("alps-profile__buttons");

            var load = new Button(() =>
            {
                Undo.RecordObject(clip, "Load Clip Profile");
                clip.data = new AlpsClipEffectSet(clip.profile.data) { profileExpanded = true };
                EditorUtility.SetDirty(clip);
                NotifyPreview();
                rebuild();
            })
            {
                text = "読込",
                tooltip = "プロファイルの内容をこのクリップに複製します。",
            };
            load.AddToClassList("alps-profile__button");
            load.SetEnabled(clip.profile != null && !clip.syncProfile);
            buttons.Add(load);

            var save = new Button(() =>
            {
                var profile = clip.profile;
                if (profile == null)
                {
                    var path = EditorUtility.SaveFilePanelInProject(
                        "プロファイルを保存",
                        "AlpsClipProfile",
                        "asset",
                        "保存先を選んでください。");
                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }

                    profile = CreateInstance<AlpsClipProfile>();
                    AssetDatabase.CreateAsset(profile, path);
                    Undo.RecordObject(clip, "Save Clip Profile");
                    clip.profile = profile;
                    EditorUtility.SetDirty(clip);
                }
                else
                {
                    Undo.RecordObject(profile, "Save Clip Profile");
                }

                profile.data = new AlpsClipEffectSet(clip.data);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                NotifyPreview();
                rebuild();
            })
            {
                text = "保存",
                tooltip = "このクリップの内容をプロファイルに書き込みます。プロファイルが未設定なら新しく作ります。",
            };
            save.AddToClassList("alps-profile__button");
            save.AddToClassList("alps-profile__button--last");
            save.SetEnabled(!clip.syncProfile);
            buttons.Add(save);

            root.Add(buttons);
            return root;
        }

        private static void NotifyPreview()
        {
            AlpsPreviewDriver.MarkDirty();
            TimelineEditor.Refresh(RefreshReason.SceneNeedsUpdate);
        }
    }
}
