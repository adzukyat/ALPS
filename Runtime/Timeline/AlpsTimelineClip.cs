using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// A lighting cue: the order, shared settings and effect stack the clip inspector edits.
    /// Evaluation happens in the track mixer, so the clip playable itself carries nothing.
    /// </summary>
    public class AlpsTimelineClip : PlayableAsset, ITimelineClipAsset
    {
        public AlpsClipEffectSet data = new AlpsClipEffectSet();

        [Tooltip("A saved clip setup this clip can load from, save to, or follow.")]
        public AlpsClipProfile profile;

        [Tooltip("Play the profile's effects instead of the clip's own.")]
        public bool syncProfile;

        public ClipCaps clipCaps => ClipCaps.Blending;

        /// <summary>The effect set that plays, which is the profile's while synced.</summary>
        public AlpsClipEffectSet EffectiveData => syncProfile && profile != null && profile.data != null ? profile.data : data;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return Playable.Create(graph);
        }

        private void OnValidate()
        {
            if (data == null)
            {
                data = new AlpsClipEffectSet();
            }

            AlpsPreviewDriver.MarkDirty();
        }
    }
}
