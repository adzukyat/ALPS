using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ManeuverForVRC
{
    /// <summary>
    /// A lighting lane bound to a fixture group. Tracks lower in the timeline act as
    /// layers above the ones before them, and override tracks nest the same way.
    /// </summary>
    [TrackColor(0.82f, 0.92f, 0.33f)]
    [TrackClipType(typeof(MfvTimelineClip))]
    [TrackBindingType(typeof(MfvFixtureGroup))]
    public class MfvTimelineTrack : TrackAsset, ILayerable
    {
        [Tooltip("Song tempo in beats per minute. Override tracks use their parent's tempo.")]
        [Min(1f)] public float bpm = 120f;

        [Tooltip("Timeline time in seconds where beat 0 falls.")]
        public float beatOrigin;

        /// <summary>The top level MFV track this one belongs to, itself for a top level track.</summary>
        public MfvTimelineTrack RootTrack
        {
            get
            {
                var root = this;
                while (root.parent is MfvTimelineTrack parentTrack)
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
            MfvPreviewDriver.Invalidate(director);

            var playable = ScriptPlayable<MfvTimelineMixer>.Create(graph, inputCount);
            playable.GetBehaviour().director = director;
            return playable;
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            var group = director != null ? director.GetGenericBinding(RootTrack) as MfvFixtureGroup : null;
            if (group == null)
            {
                return;
            }

            foreach (var fixture in group.fixtures)
            {
                if (fixture != null)
                {
                    fixture.GatherPreviewProperties(driver);
                }
            }
        }

        public IEnumerable<TimelineClip> GetMfvClips()
        {
            foreach (var clip in GetClips())
            {
                if (clip.asset is MfvTimelineClip)
                {
                    yield return clip;
                }
            }
        }
    }
}
