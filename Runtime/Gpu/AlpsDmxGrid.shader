// Lays the evaluated frames out as the DMX grid VRSL's mover shaders read in DMX mode.
// The fixture on DMX row n starts on absolute DMX channel 13n + 1, one row of the 13 column
// grid each. Rows are handed out across every show of a scene, and the show data says which
// of its fixtures sits on which row. Rows it holds no fixture for stay dark.
// Pass 0 is the grid VRSL reads its channels from, pass 1 the gobo spin phase grid.
//
// Which texel VRSL reads a channel from is worked out the way VRSL's getValueAtCoords
// does (VRSL-DMXFunctions.cginc), so each channel lands exactly where it is read.
Shader "Hidden/ALPS/DMX Grid"
{
    Properties
    {
        _MainTex ("Frames", 2D) = "black" {}
        _AlpsData ("Show Data", 2D) = "black" {}
        _AlpsBrightnessGain ("Brightness Gain", Float) = 2
        _AlpsDimmer ("Dimmer", Float) = 1
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "AlpsEvaluator.hlsl"

    Texture2D<float4> _MainTex;
    float _AlpsBrightnessGain;
    float _AlpsDimmer;

    #define ALPS_GRID_WIDTH 26.0
    #define ALPS_GRID_HEIGHT 240.0

    // AlpsShowPlayer.ModelConeLengthLimit, the model length of the fixture's own mesh.
    #define ALPS_CONE_LENGTH_LIMIT 50.0

    float AlpsFrameChannel(int fixture, int channel)
    {
        return _MainTex.Load(int3(channel / 4, fixture, 0))[channel % 4];
    }

    // The texel VRSL samples for absolute channel <dmx> on a 26 x 240 grid, following
    // getValueAtCoords and IndustryRead for channels 1 to 12 of a row. IndustryRead takes
    // its row as an int, which drops the fraction of dmx / 13, so a whole row of channels
    // shares one texel row.
    int2 AlpsVrslTexel(uint dmx)
    {
        uint x = dmx % 13;
        int row = (int)(dmx / 13.0 + 1.0);
        float2 texel = float2(1.0 / ALPS_GRID_WIDTH, 1.0 / ALPS_GRID_HEIGHT);
        float resMultiplier = ALPS_GRID_WIDTH / 13;
        float2 uv;
        uv.x = ((x * resMultiplier) * texel.x) - 0.015;
        uv.y = ((row * resMultiplier) * texel.y) - 0.001915;
        return int2(floor(uv * float2(ALPS_GRID_WIDTH, ALPS_GRID_HEIGHT)));
    }

    // Finds the show's fixture and the channel offset VRSL reads from texel <p>, or returns false.
    bool AlpsCellAt(int2 p, AlpsShow show, out int fixture, out int offset)
    {
        fixture = 0;
        offset = 0;
        if ((p.x & 1) == 0)
        {
            return false;
        }

        // Only odd columns are read, column 2x - 1 for channel x of a row.
        int x = (p.x + 1) / 2;
        if (x < 1 || x > 12)
        {
            return false;
        }

        // Fixture n's row is read from texel row 2n + 1. Checked against VRSL's own maths.
        if ((p.y & 1) == 0)
        {
            return false;
        }

        int n = (p.y - 1) / 2;
        if (n >= ALPS_MAX_FIXTURES)
        {
            return false;
        }

        int2 read = AlpsVrslTexel((uint)(13 * n + x));
        if (read.x != p.x || read.y != p.y)
        {
            return false;
        }

        fixture = AlpsReadInt(show.rowFixtures + n);
        if (fixture < 0 || fixture >= show.fixtureCount)
        {
            return false;
        }

        offset = x - 1;
        return true;
    }

    // Model units to the 0..1 DMX value VRSL turns back into them. Values past the ends are
    // kept, since the grid holds floats.
    float AlpsDmxValue(int fixture, int offset, AlpsShow show)
    {
        int info = show.fixtureInfo + fixture * ALPS_FIXTURE_INFO_STRIDE;
        if (offset == 0)
        {
            // Pan: VRSL adds the DMX angle to a base rotation of 0 and mirrors the model's sign.
            float range = AlpsRead(info);
            return range != 0.0 ? (-AlpsFrameChannel(fixture, FramePan) + range) / (2.0 * range) : 0.5;
        }

        if (offset == 1)
        {
            // The fine pan channel is free, since fine channels are off, and VRSL's volumetric
            // mesh reads it to stretch the cone when cone length via DMX is on, by 4 per unit
            // on top of the fixture's own mesh length. The model length is a share of the
            // mesh at ModelConeLengthLimit.
            float mesh = AlpsFrameChannel(fixture, FrameConeMeshLength);
            return mesh * (max(0.0, AlpsFrameChannel(fixture, FrameConeLength)) / ALPS_CONE_LENGTH_LIMIT - 1.0) / 4.0;
        }

        if (offset == 2)
        {
            // Tilt: added to VRSL's base tilt of 90.
            float range = AlpsRead(info + 1);
            return range != 0.0 ? (AlpsFrameChannel(fixture, FrameTilt) + range) / (2.0 * range) : 0.5;
        }

        if (offset == 4)
        {
            // Cone width: the static path's width is 6 per 90 model units above -0.5, and
            // DMX gives lerp(0, 5.5, v) - 1.5 where the static path gives width - 1.
            return max(0.0, AlpsFrameChannel(fixture, FrameConeWidth)) / 90.0 * 6.0 / 5.5;
        }

        if (offset == 5)
        {
            return _AlpsDimmer;
        }

        if (offset == 6)
        {
            // The strobe grid is this same texture, where 1 holds the shutter open.
            return 1.0;
        }

        if (offset >= 7 && offset <= 9)
        {
            // The dimmer stays put and the colour carries the brightness, which keeps it
            // linear on every mesh and lets it pass 100%.
            float level = max(0.0, AlpsFrameChannel(fixture, FrameBrightness) * AlpsFrameChannel(fixture, FrameBrightnessScale) / 100.0);
            return AlpsFrameChannel(fixture, FrameRed + offset - 7) * level * _AlpsBrightnessGain;
        }

        if (offset == 11)
        {
            // VRSL rounds value * 255 / step to the gobo number.
            return clamp(round(AlpsFrameChannel(fixture, FrameGobo)), 1.0, 8.0) * AlpsRead(info + 3) / 255.0;
        }

        // Fine tilt and the spin direction stay at 0.
        return 0.0;
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 5.0

            float4 frag(v2f_img i) : SV_Target
            {
                AlpsShow show = AlpsLoadShow();
                int fixture;
                int offset;
                if (!AlpsCellAt(int2(floor(i.uv * float2(ALPS_GRID_WIDTH, ALPS_GRID_HEIGHT))), show, fixture, offset))
                {
                    return 0;
                }

                float value = AlpsDmxValue(fixture, offset, show);
                return float4(value, value, value, 1);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 5.0

            float4 frag(v2f_img i) : SV_Target
            {
                AlpsShow show = AlpsLoadShow();
                int fixture;
                int offset;
                if (!AlpsCellAt(int2(floor(i.uv * float2(ALPS_GRID_WIDTH, ALPS_GRID_HEIGHT))), show, fixture, offset) || offset != 10)
                {
                    return 0;
                }

                // VRSL turns the phase into an angle of degrees(phase * 4) and flips it for
                // an inverted pan, so the gobo angle goes in as that phase.
                float sign = AlpsRead(show.fixtureInfo + fixture * ALPS_FIXTURE_INFO_STRIDE + 2);
                float phase = radians(AlpsFrameChannel(fixture, FrameGoboRotation)) / 4.0 * sign;
                return float4(phase, 0, 0, 1);
            }
            ENDCG
        }
    }
}
