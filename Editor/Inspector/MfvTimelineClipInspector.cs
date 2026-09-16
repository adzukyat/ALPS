using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// Hosts <see cref="MfvClipInspectorView"/> on a timeline clip.
    ///
    /// The timeline clip inspector draws the clip asset with IMGUI, so CreateInspectorGUI is
    /// never called there. OnInspectorGUI draws nothing and instead hangs the same view right
    /// after the IMGUI container the clip inspector runs in. Selecting the asset on its own
    /// still goes through CreateInspectorGUI.
    ///
    /// The view mutates the model object directly rather than going through
    /// SerializedProperty bindings, so undo has to be handled explicitly: snapshot before
    /// an interaction starts, and rebuild afterwards. Deserializing an undo step replaces
    /// the managed <see cref="MfvClipEffectSet"/> instance, so a stale view would keep
    /// editing an object the asset no longer references.
    /// </summary>
    [CustomEditor(typeof(MfvTimelineClip))]
    public class MfvTimelineClipInspector : UnityEditor.Editor
    {
        private VisualElement _injected;

        public override VisualElement CreateInspectorGUI()
        {
            return BuildHost();
        }

        public override void OnInspectorGUI()
        {
            if (targets.Length > 1)
            {
                Detach();
                EditorGUILayout.HelpBox("複数のクリップは同時に編集できません。", MessageType.Info);
                return;
            }

            if (!AttachTo(MfvImguiHost.Current()))
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

            return true;
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
            var clip = (MfvTimelineClip)target;
            var host = new VisualElement();

            void Rebuild()
            {
                host.Clear();

                if (clip == null)
                {
                    return;
                }

                clip.data ??= new MfvClipEffectSet();
                var synced = clip.syncProfile && clip.profile != null;
                var edited = synced ? (Object)clip.profile : clip;
                var set = clip.EffectiveData;

                var view = new MfvClipInspectorView(set);
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

        private static VisualElement BuildProfile(MfvTimelineClip clip, System.Action rebuild)
        {
            var root = new VisualElement();

            var field = new ObjectField("プロファイル")
            {
                objectType = typeof(MfvClipProfile),
                allowSceneObjects = false,
                value = clip.profile,
            };
            field.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(clip, "Change Clip Profile");
                clip.profile = evt.newValue as MfvClipProfile;
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
            buttons.AddToClassList("mfv-profile__buttons");

            var load = new Button(() =>
            {
                Undo.RecordObject(clip, "Load Clip Profile");
                clip.data = new MfvClipEffectSet(clip.profile.data) { profileExpanded = true };
                EditorUtility.SetDirty(clip);
                NotifyPreview();
                rebuild();
            })
            {
                text = "読込",
                tooltip = "プロファイルの内容をこのクリップに複製します。",
            };
            load.AddToClassList("mfv-profile__button");
            load.SetEnabled(clip.profile != null && !clip.syncProfile);
            buttons.Add(load);

            var save = new Button(() =>
            {
                var profile = clip.profile;
                if (profile == null)
                {
                    var path = EditorUtility.SaveFilePanelInProject(
                        "プロファイルを保存",
                        "MfvClipProfile",
                        "asset",
                        "保存先を選んでください。");
                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }

                    profile = CreateInstance<MfvClipProfile>();
                    AssetDatabase.CreateAsset(profile, path);
                    Undo.RecordObject(clip, "Save Clip Profile");
                    clip.profile = profile;
                    EditorUtility.SetDirty(clip);
                }
                else
                {
                    Undo.RecordObject(profile, "Save Clip Profile");
                }

                profile.data = new MfvClipEffectSet(clip.data);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                NotifyPreview();
                rebuild();
            })
            {
                text = "保存",
                tooltip = "このクリップの内容をプロファイルに書き込みます。プロファイルが未設定なら新しく作ります。",
            };
            save.AddToClassList("mfv-profile__button");
            save.AddToClassList("mfv-profile__button--last");
            save.SetEnabled(!clip.syncProfile);
            buttons.Add(save);

            root.Add(buttons);
            return root;
        }

        private static void NotifyPreview()
        {
            MfvPreviewDriver.MarkDirty();
            TimelineEditor.Refresh(RefreshReason.SceneNeedsUpdate);
        }
    }
}
