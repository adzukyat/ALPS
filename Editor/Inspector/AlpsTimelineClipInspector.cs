using System.Linq;
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
    ///
    /// With several clips selected the view shows the first one, and each edit is carried
    /// over to the others by <see cref="AlpsClipEditSync"/>, so only what was changed
    /// lands on them.
    /// </summary>
    [CustomEditor(typeof(AlpsTimelineClip))]
    [CanEditMultipleObjects]
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
            var clips = targets.OfType<AlpsTimelineClip>().ToArray();
            var host = new VisualElement();

            void Rebuild()
            {
                host.Clear();

                if (clips.Length == 0 || clips.Any(clip => clip == null))
                {
                    return;
                }

                foreach (var clip in clips)
                {
                    clip.data ??= new AlpsClipEffectSet();
                }

                // Clips following the same profile share one effect set, so it is edited once.
                var set = clips[0].EffectiveData;
                var others = clips.Select(clip => clip.EffectiveData).Distinct().Where(other => other != set).ToArray();
                var edited = clips.Select(EditedObject).Distinct().ToArray();
                var before = new AlpsClipEffectSet(set);
                var mixed = others.Length > 0 ? new AlpsMixedValues(set, others) : null;

                var view = new AlpsClipInspectorView(set, mixed);
                view.RegisterCallback<PointerDownEvent>(
                    _ => Undo.RegisterCompleteObjectUndo(edited, "Edit Clip Effects"),
                    TrickleDown.TrickleDown);
                view.StructureEdited += edit =>
                {
                    foreach (var other in others)
                    {
                        edit(other);
                    }
                };
                view.Changed += () =>
                {
                    // A mixed control committed to the value the shown clip already had
                    // leaves no difference for the diff, so its field is carried first.
                    mixed?.CarryEdited();
                    foreach (var other in others)
                    {
                        AlpsClipEditSync.Apply(before, set, other);
                    }

                    before = new AlpsClipEffectSet(set);
                    mixed?.Recompute();
                    foreach (var target in edited)
                    {
                        EditorUtility.SetDirty(target);
                    }

                    NotifyPreview();
                };

                if (clips.Length > 1)
                {
                    var notice = new HelpBox(
                        $"{clips.Length}個のクリップを同時に編集しています。値が異なる項目は「{AlpsMixedValues.MixedText}」で表示され、変更した項目だけが全てのクリップに反映されます。",
                        HelpBoxMessageType.Info);
                    notice.AddToClassList("alps-multi-notice");
                    view.Insert(0, notice);
                }

                view.ProfileBody.Add(BuildProfile(clips, Rebuild));
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

        /// <summary>The asset an edit of this clip writes to, which is the profile while synced.</summary>
        private static Object EditedObject(AlpsTimelineClip clip)
        {
            return clip.syncProfile && clip.profile != null ? (Object)clip.profile : clip;
        }

        /// <summary>
        /// Profile field, sync toggle and load apply to every selected clip. Save writes one
        /// clip into one profile, so it only works on a single clip.
        /// </summary>
        private static VisualElement BuildProfile(AlpsTimelineClip[] clips, System.Action rebuild)
        {
            var root = new VisualElement();
            var first = clips[0];
            var single = clips.Length == 1;
            var allHaveProfile = clips.All(clip => clip.profile != null);
            var anySynced = clips.Any(clip => clip.syncProfile);

            void Edit(string undoName, System.Action<AlpsTimelineClip> apply)
            {
                Undo.RecordObjects(clips, undoName);
                foreach (var clip in clips)
                {
                    apply(clip);
                    EditorUtility.SetDirty(clip);
                }

                NotifyPreview();
                rebuild();
            }

            var field = new ObjectField("プロファイル")
            {
                objectType = typeof(AlpsClipProfile),
                allowSceneObjects = false,
                value = first.profile,
                showMixedValue = clips.Any(clip => clip.profile != first.profile),
            };
            field.RegisterValueChangedCallback(evt =>
                Edit("Change Clip Profile", clip => clip.profile = evt.newValue as AlpsClipProfile));
            root.Add(field);

            var sync = new Toggle("プロファイルに追従")
            {
                value = first.syncProfile,
                showMixedValue = clips.Any(clip => clip.syncProfile != first.syncProfile),
            };
            sync.tooltip = "オンの間はプロファイルの効果を再生し、編集もプロファイルに書き込みます。";
            sync.SetEnabled(allHaveProfile);
            sync.RegisterValueChangedCallback(evt =>
                Edit("Sync Clip Profile", clip => clip.syncProfile = evt.newValue));
            root.Add(sync);

            var buttons = new VisualElement();
            buttons.AddToClassList("alps-profile__buttons");

            var load = new Button(() => Edit(
                "Load Clip Profile",
                clip => clip.data = new AlpsClipEffectSet(clip.profile.data) { profileExpanded = true }))
            {
                text = "読込",
                tooltip = single
                    ? "プロファイルの内容をこのクリップに複製します。"
                    : "それぞれのプロファイルの内容を各クリップに複製します。",
            };
            load.AddToClassList("alps-profile__button");
            load.SetEnabled(allHaveProfile && !anySynced);
            buttons.Add(load);

            var save = new Button(() =>
            {
                var clip = first;
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
                tooltip = single
                    ? "このクリップの内容をプロファイルに書き込みます。プロファイルが未設定なら新しく作ります。"
                    : "複数のクリップを選択している間は保存できません。",
            };
            save.AddToClassList("alps-profile__button");
            save.AddToClassList("alps-profile__button--last");
            save.SetEnabled(single && !first.syncProfile);
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
