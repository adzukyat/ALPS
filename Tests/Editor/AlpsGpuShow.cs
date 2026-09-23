using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// A compiled show on the GPU evaluator, run through the same passes as the player and
    /// the preview, with its frames read back per time. Needs a graphics device, so the tests
    /// run without -nographics.
    /// </summary>
    internal sealed class AlpsGpuShow : IDisposable
    {
        private readonly List<Object> _created = new List<Object>();
        private readonly Dictionary<float, float[]> _frames = new Dictionary<float, float[]>();
        private readonly Texture2D _data;

        public readonly int FixtureCount;
        public readonly Material FramesMaterial;
        public readonly Material GridMaterial;
        public readonly RenderTexture Frames;
        public readonly RenderTexture Grid;
        public readonly RenderTexture Spin;

        public static void RequireGraphics()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Needs a graphics device. Run the tests without -nographics.");
            }
        }

        /// <param name="rows">The DMX row of each fixture, its own index when left out.</param>
        /// <param name="aim">Each fixture's world to aim space, all zero when left out.</param>
        public AlpsGpuShow(AlpsCompiledShow show, int fixtureCount = -1, int[] rows = null, float[] aim = null)
        {
            RequireGraphics();
            FixtureCount = fixtureCount >= 0 ? fixtureCount : Mathf.Max(show.fixtureCount, show.FixtureCount);
            var info = new float[FixtureCount * AlpsShowPlayer.GpuFixtureInfoStride];
            for (var i = 0; i < FixtureCount; i++)
            {
                AlpsShowPlayer.WriteNeutralDmxInfo(info, i * AlpsShowPlayer.GpuFixtureInfoStride);
            }

            if (rows == null)
            {
                rows = new int[FixtureCount];
                for (var i = 0; i < FixtureCount; i++)
                {
                    rows[i] = i;
                }
            }

            _data = Keep(AlpsShowPlayer.CreateShowTexture(AlpsShowPlayer.PackShowData(
                show.clips, show.effects, show.parameters, show.colors, show.gobos, show.positions,
                show.bucketStart, show.bucketClips, show.bucketSeconds, show.groupCount, show.groupIndex,
                FixtureCount, NeutralDefaults(FixtureCount), info, rows, aim ?? new float[FixtureCount * AlpsShowPlayer.GpuAimStride])));
            var framesShader = Shader.Find(AlpsPreviewDriver.GpuFramesShader);
            var gridShader = Shader.Find(AlpsPreviewDriver.GpuGridShader);
            Assert.NotNull(framesShader, "The frames shader did not compile.");
            Assert.NotNull(gridShader, "The DMX grid shader did not compile.");
            FramesMaterial = Keep(new Material(framesShader));
            GridMaterial = Keep(new Material(gridShader));
            Frames = Keep(AlpsPreviewDriver.CreateGpuTarget("Frames", AlpsShowPlayer.GpuFrameTexels, Mathf.Max(1, FixtureCount)));
            Grid = Keep(AlpsPreviewDriver.CreateGpuTarget("Grid", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight));
            Spin = Keep(AlpsPreviewDriver.CreateGpuTarget("Spin", AlpsShowPlayer.GpuGridWidth, AlpsShowPlayer.GpuGridHeight));
            FramesMaterial.SetTexture("_AlpsData", _data);
            FramesMaterial.SetFloat("_AlpsFrameRows", Frames.height);
            GridMaterial.SetTexture("_AlpsData", _data);
        }

        /// <summary>Every fixture's frame at <paramref name="time"/>, channel after channel.</summary>
        public float[] FramesAt(float time)
        {
            if (_frames.TryGetValue(time, out var cached))
            {
                return cached;
            }

            Render(time);
            var pixels = ReadBack(Frames);
            var result = new float[FixtureCount * AlpsShowLayout.FrameStride];
            for (var fixture = 0; fixture < FixtureCount; fixture++)
            {
                for (var ch = 0; ch < AlpsShowLayout.FrameStride; ch++)
                {
                    result[fixture * AlpsShowLayout.FrameStride + ch] = pixels.GetPixel(ch / 4, fixture)[ch % 4];
                }
            }

            _frames.Add(time, result);
            return result;
        }

        /// <summary>One fixture's frame at <paramref name="time"/>.</summary>
        public float[] Evaluate(int fixture, float time)
        {
            var frame = new float[AlpsShowLayout.FrameStride];
            Array.Copy(FramesAt(time), fixture * AlpsShowLayout.FrameStride, frame, 0, frame.Length);
            return frame;
        }

        /// <summary>Runs the passes at <paramref name="time"/>, leaving the frames and grids drawn.</summary>
        public void Render(float time)
        {
            FramesMaterial.SetFloat("_AlpsTime", time);
            AlpsShowPlayer.RenderPasses(_data, FramesMaterial, GridMaterial, Frames, Grid, Spin);
        }

        public Texture2D ReadBack(RenderTexture target)
        {
            return Keep(ReadBackTexture(target));
        }

        /// <summary>A float copy of <paramref name="target"/>, which the caller destroys.</summary>
        public static Texture2D ReadBackTexture(RenderTexture target)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            return texture;
        }

        /// <summary>The frame of every fixture of a frames target, channel after channel.</summary>
        public static float[] ReadFrames(RenderTexture target, int fixtures, int channels = AlpsShowLayout.FrameStride)
        {
            var pixels = ReadBackTexture(target);
            try
            {
                var result = new float[fixtures * channels];
                for (var fixture = 0; fixture < fixtures; fixture++)
                {
                    for (var ch = 0; ch < channels; ch++)
                    {
                        result[fixture * channels + ch] = pixels.GetPixel(ch / 4, fixture)[ch % 4];
                    }
                }

                return result;
            }
            finally
            {
                Object.DestroyImmediate(pixels);
            }
        }

        public static float[] NeutralDefaults(int fixtures)
        {
            var defaults = new float[fixtures * AlpsShowLayout.FrameStride];
            for (var i = 0; i < fixtures; i++)
            {
                AlpsShowPlayer.WriteNeutralFrame(defaults, i * AlpsShowLayout.FrameStride);
            }

            return defaults;
        }

        public T Keep<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        public void Dispose()
        {
            // A blit leaves its target active, and releasing the active target warns.
            if (RenderTexture.active != null && _created.Contains(RenderTexture.active))
            {
                RenderTexture.active = null;
            }

            foreach (var created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
            _frames.Clear();
        }
    }
}
