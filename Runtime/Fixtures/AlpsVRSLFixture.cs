using UnityEngine;
using UnityEngine.Timeline;
using VRSL;

namespace AdzukiSoft.ALPS
{
    /// <summary>Drives a VR Stage Lighting DMX Static fixture.</summary>
    [AddComponentMenu("Adzuki Live Performance System/ALPS VRSL Fixture")]
    public class AlpsVRSLFixture : AlpsFixture
    {
        [Tooltip("The VRSL fixture to drive. Found in children when left empty.")]
        public VRStageLighting_DMX_Static target;

        /// <summary>Every field <see cref="AlpsShowPlayer.ConfigureVRSLDmx"/> writes.</summary>
        private static readonly string[] PreviewProperties =
        {
            "enableDMXChannels",
            "enableStrobe",
            "enableAutoSpin",
            "enableFineChannels",
            "dmxChannel",
            "dmxUniverse",
            "nineUniverseMode",
            "useLegacySectorMode",
            "singleChannelMode",
            "panOffsetBlueGreen",
            "tiltOffsetBlue",
            "globalIntensity",
            "lightColorTint.r",
            "lightColorTint.g",
            "lightColorTint.b",
            "lightColorTint.a",
            "coneLength",
            "maxConeLength",
        };

        private MaterialPropertyBlock _block;
        private bool _hasAuthored;
        private bool _authoredDmx;
        private bool _authoredStrobe;
        private bool _authoredAutoSpin;
        private bool _authoredFineChannels;
        private int _authoredChannel;
        private int _authoredUniverse;
        private bool _authoredNineUniverses;
        private bool _authoredSectors;
        private bool _authoredSingleChannel;
        private float _authoredPan;
        private float _authoredTilt;
        private float _authoredIntensity;
        private Color _authoredTint;
        private float _authoredConeLength;
        private float _authoredMaxConeLength;

        public override int AdapterKind => AlpsShowPlayer.AdapterVRSLDmxStatic;

        public override bool IsReady => ResolveTarget() != null;

        public override Transform AimTransform => AlpsShowPlayer.GetVRSLAimTransform(ResolveTarget());

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
            _hasAuthored = true;
            _authoredDmx = fixture.enableDMXChannels;
            _authoredStrobe = fixture.enableStrobe;
            _authoredAutoSpin = fixture.enableAutoSpin;
            _authoredFineChannels = fixture.enableFineChannels;
            _authoredChannel = fixture.dmxChannel;
            _authoredUniverse = fixture.dmxUniverse;
            _authoredNineUniverses = fixture.nineUniverseMode;
            _authoredSectors = fixture.useLegacySectorMode;
            _authoredSingleChannel = fixture.singleChannelMode;
            _authoredPan = fixture.panOffsetBlueGreen;
            _authoredTilt = fixture.tiltOffsetBlue;
            _authoredIntensity = fixture.globalIntensity;
            _authoredTint = fixture.lightColorTint;
            _authoredConeLength = fixture.coneLength;
            _authoredMaxConeLength = fixture.maxConeLength;
        }

        public override void RestoreAuthored(float[] frame, int offset)
        {
            var fixture = ResolveTarget();
            if (fixture == null || !_hasAuthored)
            {
                return;
            }

            // The preview put the fixture in DMX mode. Updating again also rebuilds the
            // property block, which drops the cone length via DMX the preview turned on.
            fixture.enableDMXChannels = _authoredDmx;
            fixture.enableStrobe = _authoredStrobe;
            fixture.enableAutoSpin = _authoredAutoSpin;
            fixture.enableFineChannels = _authoredFineChannels;
            fixture.dmxChannel = _authoredChannel;
            fixture.dmxUniverse = _authoredUniverse;
            fixture.nineUniverseMode = _authoredNineUniverses;
            fixture.useLegacySectorMode = _authoredSectors;
            fixture.singleChannelMode = _authoredSingleChannel;
            fixture.panOffsetBlueGreen = _authoredPan;
            fixture.tiltOffsetBlue = _authoredTilt;
            fixture.globalIntensity = _authoredIntensity;
            fixture.lightColorTint = _authoredTint;
            fixture.coneLength = _authoredConeLength;
            fixture.maxConeLength = _authoredMaxConeLength;
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
                // Rebuilds the property block from the reverted fields.
                fixture._UpdateInstancedProperties();
            }

            _hasAuthored = false;
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
            AlpsShowPlayer.ConfigureVRSLDmx(fixture, row, defaults, offset);
            EnableConeLengthViaDmx(fixture);
        }

        /// <summary>
        /// Turns VRSL's cone length via DMX on in each renderer's property block, after VRSL
        /// rebuilt the blocks. A build turns it on in the materials instead, since a block
        /// with a property outside VRSL's instanced ones could keep the renderers from being
        /// drawn together.
        /// </summary>
        private void EnableConeLengthViaDmx(VRStageLighting_DMX_Static fixture)
        {
            if (fixture.objRenderers == null)
            {
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            foreach (var renderer in fixture.objRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_block);
                _block.SetFloat(AlpsShowPlayer.ConeLengthViaDmxProperty, 1f);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void Reset()
        {
            ResolveTarget();
        }
    }
}
