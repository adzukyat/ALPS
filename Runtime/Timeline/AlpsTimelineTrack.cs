using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// A lighting lane bound to an ALPS Container, which drives every fixture below it, or
    /// to a single fixture. Tracks lower in the timeline act as layers above the ones before
    /// them, and override tracks nest the same way.
    /// </summary>
    [TrackColor(0.82f, 0.92f, 0.33f)]
    [TrackClipType(typeof(AlpsTimelineClip))]
    [TrackBindingType(typeof(AlpsTarget))]
    public class AlpsTimelineTrack : TrackAsset, ILayerable
    {
        public const float DefaultBpm = 120f;

        /// <summary>
        /// The show's tempo, one value for the whole timeline. Every ALPS track keeps a copy
        /// so the value survives deleting any one track. It is edited as 全体BPM in the clip
        /// inspector, so the track inspector does not show it. Read it through <see cref="ShowBpm"/>.
        /// </summary>
        [HideInInspector] [Min(1f)] public float bpm = DefaultBpm;

        /// <summary>The tempo of the whole timeline: the first ALPS track's copy.</summary>
        public static float ShowBpm(TimelineAsset timeline)
        {
            foreach (var track in AlpsShowCompiler.CollectTracks(timeline))
            {
                return track.bpm > 0f ? track.bpm : DefaultBpm;
            }

            return DefaultBpm;
        }

        /// <summary>The top level ALPS track this one belongs to, itself for a top level track.</summary>
        public AlpsTimelineTrack RootTrack
        {
            get
            {
                var root = this;
                while (root.parent is AlpsTimelineTrack parentTrack)
                {
                    root = parentTrack;
                }

                return root;
            }
        }

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return CreateMixer(graph, go, inputCount);
        }

        public Playable CreateLayerMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return CreateMixer(graph, go, inputCount);
        }

        private static Playable CreateMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var director = go != null ? go.GetComponent<PlayableDirector>() : null;

            // A rebuilt graph means clips may have moved, so the compiled show is rebuilt too.
            AlpsPreviewDriver.Invalidate(director);

            var playable = ScriptPlayable<AlpsTimelineMixer>.Create(graph, inputCount);
            playable.GetBehaviour().director = director;
            return playable;
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            var target = director != null ? director.GetGenericBinding(RootTrack) as AlpsTarget : null;
            if (target == null)
            {
                return;
            }

            foreach (var fixture in target.Fixtures())
            {
                fixture.GatherPreviewProperties(driver);
            }
        }

        public IEnumerable<TimelineClip> GetAlpsClips()
        {
            foreach (var clip in GetClips())
            {
                if (clip.asset is AlpsTimelineClip)
                {
                    yield return clip;
                }
            }
        }
    }
}
