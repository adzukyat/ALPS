using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Sets a show up around a container from its inspector: a Timeline asset under
    /// <see cref="ShowsFolder"/>, a parent with a director playing it, and a group of lighting
    /// tracks for the container and for each container nested in it, every track bound to its
    /// container. Each group starts with a move, a colour and a brightness track, the usual
    /// way of keeping parts that move differently apart, and the top group's tracks start with
    /// one clip each, so the lights come on at once.
    ///
    /// Everything it creates is in view afterwards, the asset in the Project window, the
    /// director in the Hierarchy and the tracks in the Timeline window, so the setup can be
    /// learned from what it left.
    /// </summary>
    public static class AlpsTimelineSetup
    {
        public const string ShowsFolder = AlpsShowSetup.GeneratedFolder + "/Shows";

        /// <summary>Length of the starter clips in beats.</summary>
        public const float StarterBeats = 16f;

        /// <summary>
        /// True when the container can be set up: it holds a fixture, no container above it
        /// drives it already, it carries no director of its own and no director plays it.
        /// </summary>
        public static bool CanSetUp(AlpsContainer container)
        {
            return container != null
                   && container.Fixtures().Count > 0
                   && IsTopContainer(container)
                   && container.GetComponent<PlayableDirector>() == null
                   && FindDirector(container) == null;
        }

        /// <summary>True when no container above this one drives its fixtures too.</summary>
        public static bool IsTopContainer(AlpsContainer container)
        {
            var parent = container.transform.parent;
            return parent == null || parent.GetComponentInParent<AlpsContainer>(true) == null;
        }

        /// <summary>A director in the container's scene with a lighting track bound to it, or null.</summary>
        public static PlayableDirector FindDirector(AlpsContainer container)
        {
            if (container == null)
            {
                return null;
            }

            foreach (var director in Object.FindObjectsOfType<PlayableDirector>(true))
            {
                if (director.gameObject.scene != container.gameObject.scene || !(director.playableAsset is TimelineAsset timeline))
                {
                    continue;
                }

                foreach (var track in AlpsShowCompiler.CollectTracks(timeline))
                {
                    if (director.GetGenericBinding(track) as AlpsContainer == container)
                    {
                        return director;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Creates the asset, the director and the tracks for <paramref name="container"/> as
        /// one undo step, apart from the asset, which stays. Returns the director.
        /// </summary>
        public static PlayableDirector SetUp(AlpsContainer container)
        {
            var timeline = CreateTimeline(container.name);

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Set Up Timeline");
            var undoGroup = Undo.GetCurrentGroup();
            var show = new GameObject(container.name + " Show");
            Undo.RegisterCreatedObjectUndo(show, "Set Up Timeline");
            var parent = container.transform.parent;
            var sibling = container.transform.GetSiblingIndex();
            show.transform.SetParent(parent, false);
            show.transform.SetSiblingIndex(sibling);
            Undo.SetTransformParent(container.transform, show.transform, true, "Set Up Timeline");

            var director = show.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = false;
            // Hold keeps the last frame at the end, where None stops the director and a seek
            // without Play would freeze it.
            director.extrapolationMode = DirectorWrapMode.Hold;

            AddGroup(timeline, director, null, container);

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
            AlpsPreviewDriver.MarkDirty();
            return director;
        }

        /// <summary>
        /// Shows <paramref name="director"/> in the Timeline window and selects its first
        /// brightness clip, so the clip inspector is what comes up next.
        /// </summary>
        public static void Open(PlayableDirector director)
        {
            if (director == null)
            {
                return;
            }

            Selection.activeGameObject = director.gameObject;
            var window = TimelineEditor.GetOrCreateWindow();
            window.Show();
            window.SetTimeline(director);
            window.Focus();

            // The window takes the director on its next update, and a clip selected before
            // that would be dropped with the old timeline.
            EditorApplication.delayCall += () =>
            {
                var clip = director != null ? FirstBrightnessClip(director.playableAsset as TimelineAsset) : null;
                if (clip != null)
                {
                    TimelineEditor.selectedClip = clip;
                }
            };
        }

        /// <summary>The first clip with a brightness effect, the starter clip that lights the fixtures.</summary>
        public static TimelineClip FirstBrightnessClip(TimelineAsset timeline)
        {
            if (timeline == null)
            {
                return null;
            }

            foreach (var track in AlpsShowCompiler.CollectTracks(timeline))
            {
                foreach (var clip in track.GetClips())
                {
                    if (clip.asset is AlpsLightingClip lighting && lighting.data != null
                        && lighting.data.IndexOf(AlpsEffectKind.Brightness, AlpsParity.All) >= 0)
                    {
                        return clip;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// For tests: removes <see cref="ShowsFolder"/>, and its parent when that holds nothing
        /// else, unless the shows folder was there before (<paramref name="hadShowsFolder"/>).
        /// </summary>
        public static void DeleteFoldersCreatedSince(bool hadShowsFolder)
        {
            if (hadShowsFolder || !AssetDatabase.IsValidFolder(ShowsFolder)
                || AssetDatabase.FindAssets(string.Empty, new[] { ShowsFolder }).Length > 0)
            {
                return;
            }

            AssetDatabase.DeleteAsset(ShowsFolder);
            if (AssetDatabase.FindAssets(string.Empty, new[] { AlpsShowSetup.GeneratedFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(AlpsShowSetup.GeneratedFolder);
            }
        }

        private static TimelineAsset CreateTimeline(string name)
        {
            AlpsShowSetup.EnsureFolder(ShowsFolder);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{ShowsFolder}/{FileName(name)}.playable");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, path);
            return timeline;
        }

        private static string FileName(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            name = name.Trim();
            return string.IsNullOrEmpty(name) ? "Show" : name;
        }

        /// <summary>
        /// A group for <paramref name="container"/> holding its three tracks, then the groups of
        /// the containers nested in it, which come later and so lie above it as layers.
        /// </summary>
        private static void AddGroup(TimelineAsset timeline, PlayableDirector director, GroupTrack parent, AlpsContainer container)
        {
            var group = timeline.CreateTrack<GroupTrack>(parent, container.name);
            var top = parent == null;

            AddTrack(timeline, director, group, container, AlpsStrings.Tr("effect.move.name"), top ? StarterMove() : null);
            AddTrack(timeline, director, group, container, AlpsStrings.Tr("effect.color.name"), top ? StarterColor() : null);
            AddTrack(timeline, director, group, container, AlpsStrings.Tr("effect.brightness.name"), top ? StarterBrightness() : null);

            foreach (var child in NestedContainers(container))
            {
                AddGroup(timeline, director, group, child);
            }
        }

        private static void AddTrack(TimelineAsset timeline, PlayableDirector director, GroupTrack group, AlpsContainer container, string name, AlpsClipEffectSet starter)
        {
            var track = timeline.CreateTrack<AlpsLightingTrack>(group, name);
            director.SetGenericBinding(track, container);
            if (starter == null)
            {
                return;
            }

            var clip = track.CreateClip<AlpsLightingClip>();
            clip.displayName = name;
            clip.start = 0.0;
            clip.duration = StarterBeats * 60.0 / AlpsLightingTrack.DefaultBpm;
            ((AlpsLightingClip)clip.asset).data = starter;
        }

        /// <summary>The containers directly below <paramref name="container"/>, with no other container between.</summary>
        public static List<AlpsContainer> NestedContainers(AlpsContainer container)
        {
            var nested = new List<AlpsContainer>();
            foreach (var candidate in container.GetComponentsInChildren<AlpsContainer>(true))
            {
                if (candidate != container && candidate.transform.parent.GetComponentInParent<AlpsContainer>(true) == container)
                {
                    nested.Add(candidate);
                }
            }

            return nested;
        }

        // --- Starter clips --------------------------------------------------------------------

        /// <summary>Tilt and pan sweeping on the clip's wave, a quarter cycle apart, so the heads trace loops.</summary>
        private static AlpsClipEffectSet StarterMove()
        {
            var set = new AlpsClipEffectSet();
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.isRange = true;
            move.pan.isRange = true;
            return set;
        }

        private static AlpsClipEffectSet StarterColor()
        {
            var set = AlpsClipEffectSet.CreateNew();
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(new Color(0.25f, 0.6f, 1f)));
            return set;
        }

        private static AlpsClipEffectSet StarterBrightness()
        {
            var set = AlpsClipEffectSet.CreateNew();
            set.Add(AlpsEffectKind.Brightness);
            return set;
        }
    }
}
