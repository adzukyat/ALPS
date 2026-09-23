using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Plays a director's ALPS tracks on the scene fixtures while the timeline is evaluated
    /// outside VRChat, which is editor preview and play mode without an applied show.
    ///
    /// The whole director compiles into one <see cref="AlpsCompiledShow"/> and runs through
    /// the same <see cref="AlpsShowEvaluator.EvaluateFixture"/> the Udon player uses. Fixture
    /// state from before the preview is captured once and restored when the graph goes away.
    /// </summary>
    public static class AlpsPreviewDriver
    {
        private sealed class State
        {
            public int users;
            public int revision = -1;
            public bool stale = true;
            public ulong lastFrameId = ulong.MaxValue;
            public float lastTime = float.NaN;
            public AlpsCompiledShow show;
            public float[] defaults = new float[0];
            public int[] active = new int[0];
            public float[] activeWeight = new float[0];
            public readonly Dictionary<AlpsFixture, float[]> captured = new Dictionary<AlpsFixture, float[]>();
            public readonly float[] frame = new float[AlpsShowEvaluator.FrameStride];
            public readonly float[] clipFrame = new float[AlpsShowEvaluator.FrameStride];
            public readonly float[] clipWritten = new float[AlpsShowEvaluator.FrameStride];
            public readonly float[] sum = new float[AlpsShowEvaluator.FrameStride];
            public readonly float[] weightSum = new float[AlpsShowEvaluator.FrameStride];
            public readonly float[] scratch = new float[1];

            // Experimental GPU playback, set up with the compiled show.
            public Texture2D gpuData;
            public Material gpuFramesMaterial;
            public Material gpuGridMaterial;
            public RenderTexture gpuFrames;
            public RenderTexture gpuGrid;
            public RenderTexture gpuSpin;
        }

        private static readonly Dictionary<PlayableDirector, State> States = new Dictionary<PlayableDirector, State>();

        /// <summary>Bumped by every clip or profile edit, so compiled shows know to rebuild.</summary>
        public static int Revision { get; private set; }

        /// <summary>
        /// Experimental: preview on the GPU through VRSL's DMX mode, the way the player does
        /// with <see cref="AlpsShowPlayer.gpu"/> on. Set by the editor's menu toggle.
        /// </summary>
        public static bool UseGpu;

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
        public static AlpsCompiledShow GetShow(PlayableDirector director)
        {
            return director != null && States.TryGetValue(director, out var state) ? EnsureCompiled(director, state) : null;
        }

        public static void Evaluate(PlayableDirector director, float time, ulong frameId)
        {
            if (director == null || !States.TryGetValue(director, out var state))
            {
                return;
            }

            // Every ALPS track calls in, but one evaluation per frame covers them all.
            if (state.lastFrameId == frameId && Mathf.Approximately(state.lastTime, time) && !state.stale && state.revision == Revision)
            {
                return;
            }

            state.lastFrameId = frameId;
            state.lastTime = time;

            var show = EnsureCompiled(director, state);
            if (state.gpuData != null)
            {
                state.gpuFramesMaterial.SetFloat("_AlpsTime", time);
                AlpsShowPlayer.RenderGpuPasses(state.gpuData, state.gpuFramesMaterial, state.gpuGridMaterial, state.gpuFrames, state.gpuGrid, state.gpuSpin);
                return;
            }

            var activeCount = AlpsShowEvaluator.ActiveClips(
                show.clips, show.bucketStart, show.bucketClips, show.bucketSeconds, time, state.active, state.activeWeight);
            for (var fixture = 0; fixture < show.fixtures.Count; fixture++)
            {
                var target = show.fixtures[fixture];
                if (target == null)
                {
                    continue;
                }

                AlpsShowEvaluator.EvaluateFixture(
                    show.clips, show.effects, show.parameters, show.colors, show.gobos, show.positions,
                    state.active, state.activeWeight, activeCount,
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

        private static AlpsCompiledShow EnsureCompiled(PlayableDirector director, State state)
        {
            if (state.show != null && !state.stale && state.revision == Revision)
            {
                return state.show;
            }

            state.show = AlpsShowCompiler.Compile(director);
            state.stale = false;
            state.revision = Revision;
            state.active = new int[state.show.ClipCount];
            state.activeWeight = new float[state.show.ClipCount];

            var stride = AlpsShowEvaluator.FrameStride;
            state.defaults = new float[state.show.fixtures.Count * stride];
            for (var i = 0; i < state.show.fixtures.Count; i++)
            {
                var fixture = state.show.fixtures[i];
                if (fixture == null)
                {
                    AlpsShowPlayer.WriteNeutralFrame(state.defaults, i * stride);
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

            ReleaseGpu(state);
            if (UseGpu)
            {
                SetUpGpu(state);
            }

            return state.show;
        }

        private static void SetUpGpu(State state)
        {
            var show = state.show;
            var count = show.fixtures.Count;
            if (count == 0)
            {
                return;
            }

            if (count > AlpsShowPlayer.GpuMaxFixtures)
            {
                Debug.LogWarning($"[ALPS] GPU preview holds {AlpsShowPlayer.GpuMaxFixtures} fixtures at most, this show has {count}. Previewing on the CPU.");
                return;
            }

            var framesShader = Shader.Find(GpuFramesShader);
            var gridShader = Shader.Find(GpuGridShader);
            if (framesShader == null || gridShader == null)
            {
                Debug.LogWarning("[ALPS] The GPU preview shaders are missing. Previewing on the CPU.");
                return;
            }

            var info = new float[count * AlpsShowPlayer.GpuFixtureInfoStride];
            for (var i = 0; i < count; i++)
            {
                var fixture = show.fixtures[i];
                if (fixture != null && fixture.SupportsGpu)
                {
                    fixture.ConfigureGpu(i, state.defaults, i * AlpsShowEvaluator.FrameStride, info, i * AlpsShowPlayer.GpuFixtureInfoStride);
                }
                else
                {
                    AlpsShowPlayer.WriteNeutralDmxInfo(info, i * AlpsShowPlayer.GpuFixtureInfoStride);
                }
            }

            var data = AlpsShowPlayer.PackShowData(
                show.clips, show.effects, show.parameters, show.colors, show.gobos, show.positions,
                show.bucketStart, show.bucketClips, show.bucketSeconds, show.groupCount, show.groupIndex,
                count, state.defaults, info);
            state.gpuData = AlpsShowPlayer.CreateShowTexture(data);
            state.gpuData.hideFlags = HideFlags.HideAndDontSave;
            state.gpuFramesMaterial = new Material(framesShader) { hideFlags = HideFlags.HideAndDontSave };
            state.gpuGridMaterial = new Material(gridShader) { hideFlags = HideFlags.HideAndDontSave };
            state.gpuFrames = CreateGpuTarget("ALPS Frames", AlpsShowPlayer.GpuFrameTexels, count);
            state.gpuGrid = CreateGpuTarget("ALPS DMX Grid", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight);
            state.gpuSpin = CreateGpuTarget("ALPS DMX Spin", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight);
            AlpsShowPlayer.BindGpu(state.gpuData, state.gpuFramesMaterial, state.gpuGridMaterial, state.gpuFrames, state.gpuGrid, state.gpuSpin);
        }

        public const string GpuFramesShader = "Hidden/ALPS/Frames";
        public const string GpuGridShader = "Hidden/ALPS/DMX Grid";

        /// <summary>A float target read texel by texel, as the GPU evaluator and VRSL read it.</summary>
        public static RenderTexture CreateGpuTarget(string name, int width, int height)
        {
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            target.Create();
            return target;
        }

        private static void ReleaseGpu(State state)
        {
            DestroyGpuObject(state.gpuData);
            DestroyGpuObject(state.gpuFramesMaterial);
            DestroyGpuObject(state.gpuGridMaterial);
            DestroyGpuObject(state.gpuFrames);
            DestroyGpuObject(state.gpuGrid);
            DestroyGpuObject(state.gpuSpin);
            state.gpuData = null;
            state.gpuFramesMaterial = null;
            state.gpuGridMaterial = null;
            state.gpuFrames = null;
            state.gpuGrid = null;
            state.gpuSpin = null;
        }

        private static void DestroyGpuObject(Object target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
