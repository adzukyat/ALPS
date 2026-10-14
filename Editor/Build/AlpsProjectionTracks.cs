using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Keys the gobo's projection switch (<see cref="AlpsProjection"/>) into Animation Tracks
    /// on the build timeline, since VRSL takes nothing from DMX that hides the projection
    /// alone. Each track turns the projection renderers below one anchor on and off, and the
    /// anchor is the closest object above all of them, one per scene root, which gets an
    /// Animator for the track to drive. <see cref="Write"/> adds the tracks when the build
    /// timeline is generated and <see cref="Bind"/> binds them on the scene copy, both from
    /// the same plan.
    /// </summary>
    public static class AlpsProjectionTracks
    {
        public const string TrackPrefix = "ALPS Projection";

        private sealed class Lane
        {
            public Renderer renderer;
            public bool litAtStart;
            public List<float> switches;
        }

        private sealed class Anchor
        {
            public Transform root;
            public readonly List<Lane> lanes = new List<Lane>();
        }

        public static string TrackName(int anchor)
        {
            return $"{TrackPrefix} {anchor + 1}";
        }

        /// <summary>Adds a track per anchor of <paramref name="show"/> to <paramref name="timeline"/>.</summary>
        public static void Write(TimelineAsset timeline, AlpsCompiledShow show)
        {
            var anchors = Plan(show);
            var duration = (float)timeline.duration;
            for (var i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                var name = TrackName(i);
                var clip = new AnimationClip { name = name };
                foreach (var lane in anchor.lanes)
                {
                    var path = AnimationUtility.CalculateTransformPath(lane.renderer.transform, anchor.root);
                    var binding = EditorCurveBinding.FloatCurve(path, lane.renderer.GetType(), "m_Enabled");
                    AnimationUtility.SetEditorCurve(clip, binding, Curve(lane, duration));
                }

                AssetDatabase.AddObjectToAsset(clip, timeline);
                var track = timeline.CreateTrack<AnimationTrack>(null, name);
                var timelineClip = track.CreateClip(clip);
                timelineClip.start = 0.0;
                timelineClip.duration = Mathf.Max(duration, clip.length);
            }
        }

        /// <summary>
        /// Binds the projection tracks of <paramref name="build"/> to Animators on the anchors
        /// of <paramref name="director"/>'s show, adding the Animators that are missing.
        /// </summary>
        public static void Bind(PlayableDirector director, TimelineAsset build, AlpsCompiledShow show, List<string> errors)
        {
            var anchors = Plan(show);
            var tracks = new Dictionary<string, AnimationTrack>();
            foreach (var track in build.GetOutputTracks())
            {
                if (track is AnimationTrack animationTrack && track.name.StartsWith(TrackPrefix) && !tracks.ContainsKey(track.name))
                {
                    tracks.Add(track.name, animationTrack);
                }
            }

            if (tracks.Count != anchors.Count)
            {
                errors.Add($"The build timeline '{build.name}' switches the gobo projection of other fixtures than director '{director.name}' drives. Directors sharing a timeline have to be bound to the same fixtures.");
                return;
            }

            for (var i = 0; i < anchors.Count; i++)
            {
                var root = anchors[i].root;
                var animator = root.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = root.gameObject.AddComponent<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                director.SetGenericBinding(tracks[TrackName(i)], animator);
            }
        }

        /// <summary>
        /// The fixtures whose projection ever goes off, grouped under their anchors in fixture
        /// order, so the generated timeline and the scene copy agree on it.
        /// </summary>
        private static List<Anchor> Plan(AlpsCompiledShow show)
        {
            var anchors = new List<Anchor>();
            if (!AlpsProjection.AnyOff(show))
            {
                return anchors;
            }

            var bySceneRoot = new Dictionary<Transform, Anchor>();
            var renderers = new List<Renderer>();
            for (var i = 0; i < show.fixtures.Count; i++)
            {
                var fixture = show.fixtures[i];
                if (fixture == null)
                {
                    continue;
                }

                var switches = AlpsProjection.Switches(show, i, out var litAtStart);
                if (switches.Count == 0 && litAtStart)
                {
                    continue;
                }

                renderers.Clear();
                fixture.CollectProjectionRenderers(renderers);
                foreach (var renderer in renderers)
                {
                    var sceneRoot = renderer.transform.root;
                    if (!bySceneRoot.TryGetValue(sceneRoot, out var anchor))
                    {
                        anchor = new Anchor();
                        bySceneRoot.Add(sceneRoot, anchor);
                        anchors.Add(anchor);
                    }

                    anchor.lanes.Add(new Lane { renderer = renderer, litAtStart = litAtStart, switches = switches });
                }
            }

            foreach (var anchor in anchors)
            {
                anchor.root = CommonAncestor(anchor.lanes);
            }

            return anchors;
        }

        private static Transform CommonAncestor(List<Lane> lanes)
        {
            var common = lanes[0].renderer.transform;
            foreach (var lane in lanes)
            {
                while (!lane.renderer.transform.IsChildOf(common))
                {
                    common = common.parent;
                }
            }

            return common;
        }

        /// <summary>A stepped curve, 1 while the projection shows, held to <paramref name="duration"/>.</summary>
        private static AnimationCurve Curve(Lane lane, float duration)
        {
            var keys = new List<Keyframe>();
            var lit = lane.litAtStart;
            keys.Add(Step(0f, lit));
            foreach (var time in lane.switches)
            {
                lit = !lit;

                // A switch right at the start replaces the first key.
                if (time <= keys[keys.Count - 1].time)
                {
                    keys[keys.Count - 1] = Step(keys[keys.Count - 1].time, lit);
                }
                else
                {
                    keys.Add(Step(time, lit));
                }
            }

            if (duration > keys[keys.Count - 1].time)
            {
                keys.Add(Step(duration, lit));
            }

            return new AnimationCurve(keys.ToArray());
        }

        private static Keyframe Step(float time, bool lit)
        {
            return new Keyframe(time, lit ? 1f : 0f, float.PositiveInfinity, float.PositiveInfinity);
        }
    }
}
