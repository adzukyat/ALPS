using UnityEngine;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// One step along an <see cref="AlpsPenlightArea"/>. The show drives it like any other
    /// fixture, so the order modes and spreads count it, but it has no light of its own: its
    /// colour goes into the penlight texture, where the penlights near it read it.
    /// </summary>
    [AddComponentMenu("")]
    public class AlpsPenlightSlot : AlpsFixture
    {
        public override int AdapterKind => AlpsShowPlayer.AdapterPenlight;

        public override bool UsesDmxRow => false;

        public override bool IsReady => true;

        public override Transform AimTransform => transform;

        /// <summary>The area this slot is a step of, the closest one above it.</summary>
        public AlpsPenlightArea Area => transform.parent != null ? transform.parent.GetComponentInParent<AlpsPenlightArea>(true) : null;

        public override void CaptureDefault(float[] frame, int offset)
        {
            AlpsShowPlayer.WriteNeutralFrame(frame, offset);
        }

        public override void GatherPreviewProperties(IPropertyCollector driver)
        {
        }
    }
}
