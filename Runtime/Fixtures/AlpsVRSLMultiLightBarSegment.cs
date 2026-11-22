using UnityEngine;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// One segment of an <see cref="AlpsVRSLMultiLightBar"/>. The show drives it like any
    /// other fixture, and its colour and brightness go into the bar's channels of the DMX
    /// grid: four of them, <see cref="index"/> times four past the bar's first.
    /// </summary>
    [AddComponentMenu("")]
    public class AlpsVRSLMultiLightBarSegment : AlpsFixture
    {
        /// <summary>Which segment of the bar this is, counted from 0 at the bar's first channel.</summary>
        public int index;

        public override int AdapterKind => AlpsShowPlayer.AdapterVRSLBarSegment;

        /// <summary>The bar this is a segment of, the closest one above it.</summary>
        public AlpsVRSLMultiLightBar Bar => transform.parent != null ? transform.parent.GetComponentInParent<AlpsVRSLMultiLightBar>(true) : null;

        public override Object DmxOwner
        {
            get
            {
                var bar = Bar;
                return bar != null ? (Object)bar : this;
            }
        }

        public override int DmxRowCount => AlpsVRSLMultiLightBar.RowCount;

        public override bool IsReady
        {
            get
            {
                var bar = Bar;
                return bar != null && bar.ResolveTarget() != null;
            }
        }

        public override Transform AimTransform => transform;

        public override void CaptureDefault(float[] frame, int offset)
        {
            var bar = Bar;
            if (bar == null)
            {
                AlpsShowPlayer.WriteNeutralFrame(frame, offset);
                return;
            }

            bar.CaptureDefault(frame, offset);
        }

        public override void RestoreAuthored(float[] frame, int offset)
        {
            var bar = Bar;
            if (bar != null)
            {
                bar.RestoreAuthored();
            }
        }

        public override void GatherPreviewProperties(IPropertyCollector driver)
        {
            var bar = Bar;
            if (bar != null)
            {
                bar.GatherPreviewProperties(driver);
            }
        }

        public override void RefreshAfterPreview()
        {
            var bar = Bar;
            if (bar != null)
            {
                bar.RefreshAfterPreview();
            }
        }

        public override void ConfigureGpu(int row, float[] defaults, int offset, float[] info, int infoOffset)
        {
            AlpsShowPlayer.WriteBarSegmentDmxInfo(info, infoOffset, index);
            var bar = Bar;
            if (bar != null)
            {
                bar.ConfigureDmx(row);
            }
        }
    }
}
