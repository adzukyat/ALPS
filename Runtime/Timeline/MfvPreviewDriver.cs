using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace ManeuverForVRC
{
    /// <summary>
    /// Plays a director's MFV tracks on the scene fixtures while the timeline is evaluated
    /// outside VRChat, which is editor preview and play mode without an applied show.
    ///
    /// The whole director compiles into one <see cref="MfvCompiledShow"/> and runs through
    /// the same <see cref="MfvShowEvaluator.EvaluateFixture"/> the Udon player uses. Fixture
    /// state from before the preview is captured once and restored when the graph goes away.
    /// </summary>
    public static class MfvPreviewDriver
    {
        private sealed class State
        {
            public int users;
            public int revision = -1;
            public bool stale = true;
            public ulong lastFrameId = ulong.MaxValue;
            public float lastTime = float.NaN;
            public MfvCompiledShow show;
            public float[] defaults = new float[0];
            public readonly Dictionary<MfvFixture, float[]> captured = new Dictionary<MfvFixture, float[]>();
            public readonly float[] frame = new float[MfvShowEvaluator.FrameStride];
            public readonly float[] clipFrame = new float[MfvShowEvaluator.FrameStride];
            public readonly float[] clipWritten = new float[MfvShowEvaluator.FrameStride];
            public readonly float[] sum = new float[MfvShowEvaluator.FrameStride];
            public readonly float[] weightSum = new float[MfvShowEvaluator.FrameStride];
            public readonly float[] scratch = new float[1];
        }

        private static readonly Dictionary<PlayableDirector, State> States = new Dictionary<PlayableDirector, State>();

        /// <summary>Bumped by every clip or profile edit, so compiled shows know to rebuild.</summary>
        public static int Revision { get; private set; }

        public static void MarkDirty()
        {
            Revision++;
        }

        public static void Invalidate(PlayableDirector director)
        {
            if (director != null && States.TryGetValue(director, out var state))
            {
                state.stale = true;
            }
        }

        public static void Retain(PlayableDirector director)
        {
            if (director == null)
            {
                return;
            }

            PurgeDestroyedDirectors();
            if (!States.TryGetValue(director, out var state))
            {
                state = new State();
                States.Add(director, state);
            }

            state.users++;
        }

        public static void Release(PlayableDirector director)
        {
            // A closing scene destroys the director before its graph, so a destroyed key still counts.
            if (ReferenceEquals(director, null) || !States.TryGetValue(director, out var state))
            {
                return;
            }

            state.users--;
            if (state.users > 0)
            {
                return;
            }

            // Timeline rebuilds the graph on most edits, and the old graph may go away before or
            // after the new one starts. The captured defaults stay, so a rebuilt graph never
            // mistakes preview output for the authored state. Only the end of a preview session
            // forgets them.
            state.users = 0;
            state.stale = true;
            foreach (var pair in state.captured)
            {
                if (pair.Key != null)
                {
                    pair.Key.RestoreAuthored(pair.Value, 0);
                }
            }
        }

        /// <summary>
        /// The Timeline window stopped previewing and reverted the fixture properties it
        /// gathered. Visuals are refreshed and defaults are captured again next time.
        /// </summary>
        public static void EndPreviewSession()
        {
            PurgeDestroyedDirectors();
            foreach (var state in States.Values)
            {
                foreach (var fixture in state.captured.Keys)
                {
                    if (fixture != null)
                    {
                        fixture.RefreshAfterPreview();
                    }
                }

                state.captured.Clear();
                state.stale = true;
                state.lastFrameId = ulong.MaxValue;
            }
        }

        /// <summary>The compiled show a director is previewing, compiling it if needed.</summary>
        public static MfvCompiledShow GetShow(PlayableDirector director)
        {
            return director != null && States.TryGetValue(director, out var state) ? EnsureCompiled(director, state) : null;
        }

        public static void Evaluate(PlayableDirector director, float time, ulong frameId)
        {
            if (director == null || !States.TryGetValue(director, out var state))
            {
                return;
            }

            // Every MFV track calls in, but one evaluation per frame covers them all.
            if (state.lastFrameId == frameId && Mathf.Approximately(state.lastTime, time) && !state.stale && state.revision == Revision)
            {
                return;
            }

            state.lastFrameId = frameId;
            state.lastTime = time;

            var show = EnsureCompiled(director, state);
            var clipCount = show.ClipCount;
            for (var fixture = 0; fixture < show.fixtures.Count; fixture++)
            {
                var target = show.fixtures[fixture];
                if (target == null)
                {
                    continue;
                }

                MfvShowEvaluator.EvaluateFixture(
                    show.clips, show.effects, show.parameters, show.colors, show.gobos, clipCount,
                    show.groupCount, show.groupIndex,
                    fixture, time, state.defaults, state.frame,
                    state.clipFrame, state.clipWritten, state.sum, state.weightSum, state.scratch);

                target.ApplyFrame(state.frame, 0);
            }
        }

        private static void PurgeDestroyedDirectors()
        {
            List<PlayableDirector> destroyed = null;
            foreach (var director in States.Keys)
            {
                if (director == null)
                {
                    if (destroyed == null)
                    {
                        destroyed = new List<PlayableDirector>();
                    }

                    destroyed.Add(director);
                }
            }

            if (destroyed != null)
            {
                foreach (var director in destroyed)
                {
                    States.Remove(director);
                }
            }
        }

        private static MfvCompiledShow EnsureCompiled(PlayableDirector director, State state)
        {
            if (state.show != null && !state.stale && state.revision == Revision)
            {
                return state.show;
            }

            state.show = MfvShowCompiler.Compile(director);
            state.stale = false;
            state.revision = Revision;

            var stride = MfvShowEvaluator.FrameStride;
            state.defaults = new float[state.show.fixtures.Count * stride];
            for (var i = 0; i < state.show.fixtures.Count; i++)
            {
                var fixture = state.show.fixtures[i];
                if (fixture == null)
                {
                    MfvShowPlayer.WriteNeutralFrame(state.defaults, i * stride);
                    continue;
                }

                // Capture only the first time a fixture is seen, before the preview has written to it.
                if (!state.captured.TryGetValue(fixture, out var captured))
                {
                    captured = new float[stride];
                    fixture.CaptureDefault(captured, 0);
                    state.captured.Add(fixture, captured);
                }

                System.Array.Copy(captured, 0, state.defaults, i * stride, stride);
            }

            return state.show;
        }
    }
}
