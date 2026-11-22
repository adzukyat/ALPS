using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;
using AdzukiSoft.ALPS.Vrsl;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Drives a VR Stage Lighting multi light bar, a bar of twelve segments that each take a
    /// colour and a brightness of their own. The show drives every segment like a fixture, so
    /// the order modes, spreads and delays run along the bar. A lighting track bound here
    /// drives the bar's segments, and one bound to a container above it counts them with
    /// the container's other fixtures.
    ///
    /// The segments are hidden children the editor keeps at <see cref="SegmentCount"/>. The
    /// bar reads a dimmer and a colour per segment, 48 channels, which is why it takes
    /// <see cref="RowCount"/> rows of the DMX grid, and why its material has to be in VRSL's
    /// 48 channel mode, as the one ALPS comes with is.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ALPS/ALPS VRSL Multi Light Bar")]
    public class AlpsVRSLMultiLightBar : AlpsTarget
    {
        public const string SegmentName = "ALPS Bar Segment";
        public const int SegmentCount = 12;

        /// <summary>The rows of 13 channels its 48 channels reach over.</summary>
        public const int RowCount = 4;

        /// <summary>What the name of VRSL's multi light bar shader ends in, which tells a bar from the other fixtures.</summary>
        public const string ShaderName = "Multi-Channel Bar";

        [Tooltip("The VRSL multi light bar to drive. Found in children when left empty.")]
        public VRStageLighting_DMX_Static target;

        private readonly AlpsVrslAuthoredState _authored = new AlpsVrslAuthoredState();

        /// <summary>Whether a VRSL fixture is drawn with the multi light bar's shader.</summary>
        public static bool IsMultiLightBar(VRStageLighting_DMX_Static fixture)
        {
            if (fixture == null || fixture.objRenderers == null)
            {
                return false;
            }

            foreach (var renderer in fixture.objRenderers)
            {
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material != null && material.shader != null && material.shader.name.EndsWith(ShaderName))
                {
                    return true;
                }
            }

            return false;
        }

        public VRStageLighting_DMX_Static ResolveTarget()
        {
            if (target == null)
            {
                target = GetComponentInChildren<VRStageLighting_DMX_Static>(true);
            }

            return target;
        }

        /// <summary>The segments below the bar, from its first to its last.</summary>
        public List<AlpsVRSLMultiLightBarSegment> Segments()
        {
            var result = new List<AlpsVRSLMultiLightBarSegment>();
            foreach (var segment in GetComponentsInChildren<AlpsVRSLMultiLightBarSegment>(true))
            {
                if (segment.Bar == this)
                {
                    result.Add(segment);
                }
            }

            result.Sort((a, b) => a.index.CompareTo(b.index));
            return result;
        }

        public override void CollectFixtures(List<AlpsFixture> into)
        {
            foreach (var segment in Segments())
            {
                into.Add(segment);
            }
        }

        /// <summary>What a segment falls back to: the bar's own tint and intensity.</summary>
        public void CaptureDefault(float[] frame, int offset)
        {
            var fixture = ResolveTarget();
            if (fixture == null)
            {
                AlpsShowPlayer.WriteNeutralFrame(frame, offset);
                return;
            }

            AlpsShowPlayer.CaptureVRSL(fixture, frame, offset);
            // Every segment asks in turn, and only the first finds the bar as it was authored.
            if (!_authored.Has)
            {
                _authored.Capture(fixture);
            }
        }

        public void RestoreAuthored()
        {
            var fixture = ResolveTarget();
            if (fixture != null)
            {
                _authored.Restore(fixture);
            }
        }

        public void GatherPreviewProperties(IPropertyCollector driver)
        {
            var fixture = ResolveTarget();
            if (fixture != null)
            {
                AlpsVrslAuthoredState.Gather(driver, fixture);
            }
        }

        public void RefreshAfterPreview()
        {
            var fixture = ResolveTarget();
            if (fixture != null)
            {
                // Rebuilds the property block from the reverted fields.
                fixture._UpdateInstancedProperties();
            }

            _authored.Clear();
        }

        /// <summary>Puts the bar in DMX mode with its first channel on <paramref name="row"/>.</summary>
        public void ConfigureDmx(int row)
        {
            var fixture = ResolveTarget();
            if (fixture != null)
            {
                AlpsShowPlayer.ConfigureVRSLDmx(fixture, row, fixture.maxConeLength);
            }
        }

        private void Reset()
        {
            ResolveTarget();
        }
    }
}
