using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// One light the show can drive. Subclasses adapt the logical frame of
    /// <see cref="AlpsShowEvaluator"/> to a concrete fixture system, so the timeline side
    /// never needs to know which lighting package is underneath.
    ///
    /// A track can be bound to one fixture directly, which then drives it alone.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class AlpsFixture : AlpsTarget
    {
        /// <summary>Which output path the Udon player uses for this fixture.</summary>
        public abstract int AdapterKind { get; }

        /// <summary>False when the fixture has nothing to drive, for example a missing target.</summary>
        public abstract bool IsReady { get; }

        /// <summary>The Transform used to aim at a tracked user.</summary>
        public abstract Transform AimTransform { get; }

        /// <summary>
        /// Reads the fixture's current state into <paramref name="frame"/> at
        /// <paramref name="offset"/>. This is what channels no effect drives fall back to.
        /// </summary>
        public abstract void CaptureDefault(float[] frame, int offset);

        /// <summary>Writes a frame to the fixture for editor preview.</summary>
        public abstract void ApplyFrame(float[] frame, int offset);

        /// <summary>
        /// Puts the fixture back the way it was authored once the preview ends.
        /// <paramref name="frame"/> is what <see cref="CaptureDefault"/> read.
        /// </summary>
        public virtual void RestoreAuthored(float[] frame, int offset)
        {
            ApplyFrame(frame, offset);
        }

        /// <summary>
        /// Registers every serialized property the preview writes, so the Timeline window
        /// reverts them when it stops previewing and the scene is not dirtied.
        /// </summary>
        public abstract void GatherPreviewProperties(IPropertyCollector driver);

        /// <summary>Called after the Timeline window reverted the preview, to bring visuals back in line.</summary>
        public virtual void RefreshAfterPreview()
        {
        }

        /// <summary>Whether the experimental GPU playback can drive this fixture.</summary>
        public virtual bool SupportsGpu => false;

        /// <summary>
        /// Readies the fixture for GPU playback as fixture <paramref name="index"/> of the
        /// show and writes what the GPU needs to know about it into <paramref name="info"/>
        /// at <paramref name="infoOffset"/>. <paramref name="defaults"/> holds its captured
        /// default frame at <paramref name="offset"/>.
        /// </summary>
        public virtual void ConfigureGpu(int index, float[] defaults, int offset, float[] info, int infoOffset)
        {
            AlpsShowPlayer.WriteNeutralDmxInfo(info, infoOffset);
        }

        public override void CollectFixtures(List<AlpsFixture> into)
        {
            into.Add(this);
        }
    }
}
