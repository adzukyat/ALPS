using UnityEngine;
using UnityEngine.Timeline;
using AdzukiSoft.ALPS.Vrsl;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// What <see cref="AlpsShowPlayer.ConfigureVRSLDmx"/> changes on a VRSL fixture, kept while
    /// the preview has it in DMX mode so it can be put back the way it was authored.
    /// </summary>
    public sealed class AlpsVrslAuthoredState
    {
        /// <summary>Every field <see cref="AlpsShowPlayer.ConfigureVRSLDmx"/> writes.</summary>
        private static readonly string[] Properties =
        {
            "enableDMXChannels",
            "enableStrobe",
            "enableFineChannels",
            "enableAutoSpin",
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

        private bool _dmx;
        private bool _strobe;
        private bool _autoSpin;
        private bool _fineChannels;
        private int _channel;
        private int _universe;
        private bool _nineUniverses;
        private bool _sectors;
        private bool _singleChannel;
        private float _pan;
        private float _tilt;
        private float _intensity;
        private Color _tint;
        private float _coneLength;
        private float _maxConeLength;

        /// <summary>True from <see cref="Capture"/> until <see cref="Clear"/>.</summary>
        public bool Has { get; private set; }

        public void Capture(VRStageLighting_DMX_Static fixture)
        {
            Has = true;
            _dmx = fixture.enableDMXChannels;
            _strobe = fixture.enableStrobe;
            _autoSpin = fixture.enableAutoSpin;
            _fineChannels = fixture.enableFineChannels;
            _channel = fixture.dmxChannel;
            _universe = fixture.dmxUniverse;
            _nineUniverses = fixture.nineUniverseMode;
            _sectors = fixture.useLegacySectorMode;
            _singleChannel = fixture.singleChannelMode;
            _pan = fixture.panOffsetBlueGreen;
            _tilt = fixture.tiltOffsetBlue;
            _intensity = fixture.globalIntensity;
            _tint = fixture.lightColorTint;
            _coneLength = fixture.coneLength;
            _maxConeLength = fixture.maxConeLength;
        }

        /// <summary>Puts the captured fields back. Does nothing before <see cref="Capture"/>.</summary>
        public void Restore(VRStageLighting_DMX_Static fixture)
        {
            if (!Has)
            {
                return;
            }

            fixture.enableDMXChannels = _dmx;
            fixture.enableStrobe = _strobe;
            fixture.enableAutoSpin = _autoSpin;
            fixture.enableFineChannels = _fineChannels;
            fixture.dmxChannel = _channel;
            fixture.dmxUniverse = _universe;
            fixture.nineUniverseMode = _nineUniverses;
            fixture.useLegacySectorMode = _sectors;
            fixture.singleChannelMode = _singleChannel;
            fixture.panOffsetBlueGreen = _pan;
            fixture.tiltOffsetBlue = _tilt;
            fixture.globalIntensity = _intensity;
            fixture.lightColorTint = _tint;
            fixture.coneLength = _coneLength;
            fixture.maxConeLength = _maxConeLength;
            fixture._UpdateInstancedProperties();
        }

        public void Clear()
        {
            Has = false;
        }

        /// <summary>
        /// Registers the fields with the Timeline window, which reverts them when it stops
        /// previewing, so the scene is not dirtied.
        /// </summary>
        public static void Gather(IPropertyCollector driver, VRStageLighting_DMX_Static fixture)
        {
            foreach (var property in Properties)
            {
                driver.AddFromName(fixture, property);
            }
        }
    }
}
