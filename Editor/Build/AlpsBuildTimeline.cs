using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The timeline a built world plays: a copy of the authoring timeline with the ALPS
    /// tracks removed, since the Udon player takes their place. Every other track stays
    /// exactly as authored and keeps its director binding.
    /// </summary>
    public static class AlpsBuildTimeline
    {
        public const string Folder = AlpsShowSetup.GeneratedFolder + "/Generated";

        public static bool HasAlpsTracks(TimelineAsset timeline)
        {
            return AlpsShowCompiler.CollectTracks(timeline).Count > 0;
        }

        public static string PathFor(TimelineAsset source)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            var guid = AssetDatabase.AssetPathToGUID(sourcePath);
            var shortGuid = string.IsNullOrEmpty(guid) ? "unsaved" : guid.Substring(0, 8);
            return $"{Folder}/{Sanitize(source.name)}_{shortGuid}_Build.playable";
        }

        /// <summary>Rewrites the build copy of <paramref name="source"/> from its saved state.</summary>
        public static TimelineAsset Generate(TimelineAsset source, List<string> errors)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                errors.Add($"Timeline '{source.name}' is not saved as an asset, so it cannot be built.");
                return null;
            }

            // The build copy is made from disk, so unsaved timeline edits have to land first.
            AssetDatabase.SaveAssetIfDirty(source);

            AlpsShowSetup.EnsureFolder(Folder);
            var path = PathFor(source);
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            if (!AssetDatabase.CopyAsset(sourcePath, path))
            {
                errors.Add($"Could not copy timeline '{sourcePath}' to '{path}'.");
                return null;
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var copy = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (copy == null)
            {
                errors.Add($"The build timeline at '{path}' could not be loaded.");
                return null;
            }

            foreach (var root in new List<TrackAsset>(copy.GetRootTracks()))
            {
                RemoveAlpsTracks(copy, root);
            }

            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssetIfDirty(copy);
            return copy;
        }

        private static void RemoveAlpsTracks(TimelineAsset timeline, TrackAsset track)
        {
            if (track is AlpsTimelineTrack)
            {
                timeline.DeleteTrack(track);
                return;
            }

            foreach (var child in new List<TrackAsset>(track.GetChildTracks()))
            {
                RemoveAlpsTracks(timeline, child);
            }
        }

        /// <summary>
        /// Points <paramref name="director"/> at <paramref name="build"/> and carries every
        /// binding over. Tracks are matched by their position among non ALPS tracks, which is
        /// stable because the copy only lost ALPS tracks.
        /// </summary>
        public static void SwapAndRebind(PlayableDirector director, TimelineAsset source, TimelineAsset build)
        {
            var sourceTracks = FlattenWithoutAlps(source);
            var bindings = ReadBindings(director, sourceTracks);
            director.playableAsset = build;
            WriteBindings(director, FlattenWithoutAlps(build), bindings);
        }

        /// <summary>
        /// Gives <paramref name="build"/> the bindings <paramref name="director"/> holds for
        /// <paramref name="source"/> without switching its asset. This is for a director that
        /// is handed the timeline by another script while the world runs.
        /// </summary>
        public static void CarryBindings(PlayableDirector director, TimelineAsset source, TimelineAsset build)
        {
            var bindings = ReadBindings(director, FlattenWithoutAlps(source));
            WriteBindings(director, FlattenWithoutAlps(build), bindings);
        }

        private static List<Object> ReadBindings(PlayableDirector director, List<TrackAsset> tracks)
        {
            var bindings = new List<Object>(tracks.Count);
            foreach (var track in tracks)
            {
                bindings.Add(director.GetGenericBinding(track));
            }

            return bindings;
        }

        private static void WriteBindings(PlayableDirector director, List<TrackAsset> buildTracks, List<Object> bindings)
        {
            var count = Mathf.Min(bindings.Count, buildTracks.Count);
            for (var i = 0; i < count; i++)
            {
                if (bindings[i] != null)
                {
                    director.SetGenericBinding(buildTracks[i], bindings[i]);
                }
            }
        }

        private static List<TrackAsset> FlattenWithoutAlps(TimelineAsset timeline)
        {
            var result = new List<TrackAsset>();
            foreach (var root in timeline.GetRootTracks())
            {
                Flatten(root, result);
            }

            return result;
        }

        private static void Flatten(TrackAsset track, List<TrackAsset> result)
        {
            if (track is AlpsTimelineTrack)
            {
                return;
            }

            result.Add(track);
            foreach (var child in track.GetChildTracks())
            {
                Flatten(child, result);
            }
        }

        private static string Sanitize(string value)
        {
            foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }
    }
}
