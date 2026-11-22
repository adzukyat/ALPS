using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;
using AdzukiSoft.ALPS.Vrsl;

namespace AdzukiSoft.ALPS
{
    /// <summary>Drives a VR Stage Lighting DMX Static fixture.</summary>
    [AddComponentMenu("ALPS/ALPS VRSL Fixture")]
    public class AlpsVRSLFixture : AlpsFixture
    {
        [Tooltip("The VRSL fixture to drive. Found in children when left empty.")]
        public VRStageLighting_DMX_Static target;

        private readonly AlpsVrslAuthoredState _authored = new AlpsVrslAuthoredState();
        private readonly List<Renderer> _projection = new List<Renderer>();
        private readonly List<bool> _authoredProjection = new List<bool>();

        public override int AdapterKind => AlpsShowPlayer.AdapterVRSLDmxStatic;

        public override bool IsReady => ResolveTarget() != null;

        public override bool IsLaser => AlpsShowPlayer.GetVRSLLaserMaterial(ResolveTarget()) != null;

        public override Transform AimTransform =>AlpsShowPlayer.GetVRSLAimTransform(ResolveTarget());

        /// <summary>
        /// A multi light bar reads a dimmer and a colour per segment over four rows of the
        /// grid, which this one fixture on one row does not write.
        /// </summary>
        public override string SetupWarning => AlpsVRSLMultiLightBar.IsMultiLightBar(ResolveTarget())
            ? $"'{name}' is a multi light bar, which a show drives segment by segment. Replace its ALPS VRSL Fixture with an ALPS VRSL Multi Light Bar."
            : null;

        public VRStageLighting_DMX_Static ResolveTarget()
        {
            if (target == null)
            {
                target = GetComponentInChildren<VRStageLighting_DMX_Static>(true);
            }

            return target;
        }

        public override void CaptureDefault(float[] frame, int offset)
        {
            var fixture = ResolveTarget();
            if (fixture == null)
            {
                AlpsShowPlayer.WriteNeutralFrame(frame, offset);
                return;
            }

            AlpsShowPlayer.CaptureVRSL(fixture, frame, offset);
            _authored.Capture(fixture);

            _projection.Clear();
            _authoredProjection.Clear();
            CollectProjectionRenderers(_projection);
            foreach (var renderer in _projection)
            {
                _authoredProjection.Add(renderer.enabled);
            }
        }

        public override void RestoreAuthored(float[] frame, int offset)
        {
            var fixture = ResolveTarget();
            if (fixture == null || !_authored.Has)
            {
                return;
            }

            // The preview put the fixture in DMX mode.
            _authored.Restore(fixture);
            SetPreviewProjection(true);
        }

        public override void GatherPreviewProperties(IPropertyCollector driver)
        {
            var fixture = ResolveTarget();
            if (fixture == null)
            {
                return;
            }

            AlpsVrslAuthoredState.Gather(driver, fixture);

            var projection = new List<Renderer>();
            CollectProjectionRenderers(projection);
            foreach (var renderer in projection)
            {
                driver.AddFromName(renderer, "m_Enabled");
            }
        }

        /// <summary>
        /// The fixture's renderers drawn with one of VRSL's projection shaders, which the gobo's
        /// projection switch hides. A mirror ball's light is not one of them: the ball has no
        /// gobo, and its light is all it throws, which its brightness dims.
        /// </summary>
        public override void CollectProjectionRenderers(List<Renderer> into)
        {
            var fixture = ResolveTarget();
            if (fixture == null || fixture.objRenderers == null)
            {
                return;
            }

            foreach (var renderer in fixture.objRenderers)
            {
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material != null && material.shader != null && material.shader.name.EndsWith("/Projection") && !into.Contains(renderer))
                {
                    into.Add(renderer);
                }
            }
        }

        public override void SetPreviewProjection(bool visible)
        {
            if (!_authored.Has)
            {
                return;
            }

            for (var i = 0; i < _projection.Count; i++)
            {
                var renderer = _projection[i];
                var enabled = visible && _authoredProjection[i];
                if (renderer != null && renderer.enabled != enabled)
                {
                    renderer.enabled = enabled;
                }
            }
        }

        public override void RefreshAfterPreview()
        {
            var fixture = ResolveTarget();
            if (fixture != null)
            {
                // Rebuilds the property block from the reverted fields.
                fixture._UpdateInstancedProperties();
            }

            _authored.Clear();
        }

        public override void ConfigureGpu(int row, float[] defaults, int offset, float[] info, int infoOffset)
        {
            var fixture = ResolveTarget();
            if (fixture == null)
            {
                base.ConfigureGpu(row, defaults, offset, info, infoOffset);
                return;
            }

            AlpsShowPlayer.CaptureVRSLDmxInfo(fixture, info, infoOffset);
            AlpsShowPlayer.ConfigureVRSLDmx(fixture, row, defaults[offset + AlpsShowLayout.FrameConeMeshLength]);
        }

        private void Reset()
        {
            ResolveTarget();
        }
    }
}
