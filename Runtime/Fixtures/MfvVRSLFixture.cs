using UnityEngine;
using UnityEngine.Timeline;
using VRSL;

namespace ManeuverForVRC
{
    /// <summary>Drives a VR Stage Lighting DMX Static fixture.</summary>
    [AddComponentMenu("Maneuver For VRC/MFV VRSL Fixture")]
    public class MfvVRSLFixture : MfvFixture
    {
        [Tooltip("The VRSL fixture to drive. Found in children when left empty.")]
        public VRStageLighting_DMX_Static target;

        private static readonly string[] PreviewProperties =
        {
            "enableDMXChannels",
            "enableStrobe",
            "enableAutoSpin",
            "panOffsetBlueGreen",
            "tiltOffsetBlue",
            "globalIntensity",
            "lightColorTint.r",
            "lightColorTint.g",
            "lightColorTint.b",
            "lightColorTint.a",
            "coneWidth",
            "coneLength",
            "selectGOBO",
        };

        private MaterialPropertyBlock _block;
        private bool _hasAuthored;
        private bool _authoredDmx;
        private bool _authoredStrobe;
        private bool _authoredAutoSpin;

        public override int AdapterKind => MfvShowPlayer.AdapterVRSLDmxStatic;

        public override bool IsReady => ResolveTarget() != null;

        public override Transform AimTransform => MfvShowPlayer.GetVRSLAimTransform(ResolveTarget());

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
                MfvShowPlayer.WriteNeutralFrame(frame, offset);
                return;
            }

            MfvShowPlayer.CaptureVRSL(fixture, frame, offset);
            _hasAuthored = true;
            _authoredDmx = fixture.enableDMXChannels;
            _authoredStrobe = fixture.enableStrobe;
            _authoredAutoSpin = fixture.enableAutoSpin;
        }

        public override void ApplyFrame(float[] frame, int offset)
        {
            var fixture = ResolveTarget();
            if (fixture == null)
            {
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            MfvShowPlayer.ApplyVRSL(fixture, frame, offset, _block);
        }

        public override void RestoreAuthored(float[] frame, int offset)
        {
            ApplyFrame(frame, offset);

            var fixture = ResolveTarget();
            if (fixture == null || !_hasAuthored)
            {
                return;
            }

            // The preview switched these off. Updating again also rebuilds the property block,
            // which drops the gobo angle the preview added.
            fixture.enableDMXChannels = _authoredDmx;
            fixture.enableStrobe = _authoredStrobe;
            fixture.enableAutoSpin = _authoredAutoSpin;
            fixture._UpdateInstancedProperties();
        }

        public override void GatherPreviewProperties(IPropertyCollector driver)
        {
            var fixture = ResolveTarget();
            if (fixture == null)
            {
                return;
            }

            foreach (var property in PreviewProperties)
            {
                driver.AddFromName(fixture, property);
            }
        }

        public override void RefreshAfterPreview()
        {
            var fixture = ResolveTarget();
            if (fixture != null)
            {
                // Rebuilds the property block from the reverted fields, which also drops the gobo angle.
                fixture._UpdateInstancedProperties();
            }

            _hasAuthored = false;
        }

        private void Reset()
        {
            ResolveTarget();
        }
    }
}
