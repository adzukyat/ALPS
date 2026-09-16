using UnityEngine;
using UnityEngine.Timeline;

namespace ManeuverForVRC
{
    /// <summary>
    /// One light the show can drive. Subclasses adapt the logical frame of
    /// <see cref="MfvShowEvaluator"/> to a concrete fixture system, so the timeline side
    /// never needs to know which lighting package is underneath.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class MfvFixture : MonoBehaviour
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
    }
}
