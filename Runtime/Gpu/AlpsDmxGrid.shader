// Lays the evaluated frames out as the DMX grid VRSL's shaders read in DMX mode.
// The fixture on DMX row n starts on absolute DMX channel 13n + 1, one row of the 13 column
// grid each, and a multi light bar runs on over four rows, four channels per segment. Rows
// are handed out across every show of a scene, and the show data says which of its fixtures
// reads each channel. Channels it holds no fixture for stay dark.
// The fixtures read in VRSL's nine universe mode, where rows 0, 120 and 240 on are three
// bands laid over the same texels in red, green and blue.
// Pass 0 is the grid VRSL reads its channels from, pass 1 the spin phase grid, which holds
// how far a gobo has turned, which is also how far a mirror ball has turned, and how far a
// laser's beams have gone round its cone.
//
// Which texel VRSL reads a channel from is worked out the way VRSL's getValueAtCoords
// does (VRSL-DMXFunctions.cginc), so each channel lands exactly where it is read.
Shader "Hidden/ALPS/DMX Grid"
{
    Properties
    {
        _MainTex ("Frames", 2D) = "black" {}
        _AlpsData ("Show Data", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "AlpsEvaluator.hlsl"

    Texture2D<float4> _MainTex;

    #define ALPS_GRID_WIDTH 26.0
    #define ALPS_GRID_HEIGHT 240.0

    // AlpsShowPlayer.ModelConeLengthLimit, the model length of the fixture's own mesh.
    #define ALPS_CONE_LENGTH_LIMIT 50.0

    // A lit fixture's dimmer stays where it is, and its colour carries the brightness. VRSL
    // multiplies each part of a fixture by the dimmer another number of times, the light on
    // the floor once, a lit face two or three times and a cone five or six, while the
    // colour scales every part once. So with the level in the colour (AlpsLight) the floor,
    // the cone and the face of every fixture dim by the same curve. These are what the
    // dimmer and a white colour hold, at which the floor and the cone of VRSL's stock
    // movers look at 100% the way its static path showed them, which ALPS drove before it
    // moved to DMX.
    #define ALPS_DIMMER_AT_FULL 1.26
    #define ALPS_COLOUR_AT_FULL 2.02

    // AlpsShowPlayer.InfoLayout, DmxLayoutLaser and the laser's ranges.
    #define ALPS_INFO_LAYOUT 4
    #define ALPS_LAYOUT_LASER 1
    #define ALPS_LASER_ANGLE_RANGE 90.0
    #define ALPS_LASER_MIN_WIDTH (-3.75)
    #define ALPS_LASER_MAX_WIDTH 20.0
    #define ALPS_LASER_MESH_SLOPE 0.511
    #define ALPS_LASER_MIN_STRETCH 0.5
    #define ALPS_LASER_MAX_STRETCH 6.0
    #define ALPS_LASER_MIN_BEAMS 4.0
    #define ALPS_LASER_MAX_BEAMS 68.0
    #define ALPS_LASER_MAX_THICKNESS 0.1
    #define ALPS_LASER_DMX_MIN_THICKNESS 0.0005
    #define ALPS_LASER_DMX_MAX_THICKNESS 0.05

    // A laser caps the middle of its beams, so no pair of values dims all of a beam by the
    // curve of the other fixtures. Up to 100% the intensity is the level and the colour takes
    // this power of it, at which the light of a whole beam of the stock laser comes out
    // near the level to the power of 2.2, the middle of it about to the power of 1.5. That
    // is at the stock laser's thickness: a thinner beam has less glow around its capped
    // middle, so more of it dims the way the middle does.
    #define ALPS_LASER_COLOUR_POWER 1.2

    // AlpsShowPlayer.DmxLayoutBarSegment and GpuChannelFixtureStep.
    #define ALPS_LAYOUT_BAR_SEGMENT 2
    #define ALPS_CHANNEL_FIXTURE_STEP 16

    // AlpsShowPlayer.GpuDmxBands and GpuDmxBandRows.
    #define ALPS_DMX_BANDS 3
    #define ALPS_DMX_BAND_ROWS 120

    // AlpsShowPlayer.DmxBandRows, the rows of a band VRSL reads all of.
    int AlpsDmxBandRows(int band)
    {
        return (1536 - 24 * band - 12) / 13 + 1;
    }

    float AlpsFrameChannel(int fixture, int channel)
    {
        return _MainTex.Load(int3(channel / 4, fixture, 0))[channel % 4];
    }

    // Brightness times flicker as a share of 100%.
    float AlpsLevel(int fixture)
    {
        return max(0.0, AlpsFrameChannel(fixture, FrameBrightness) * AlpsFrameChannel(fixture, FrameBrightnessScale) / 100.0);
    }

    float AlpsDimmer(int fixture)
    {
        return AlpsLevel(fixture) > 0.0 ? ALPS_DIMMER_AT_FULL : 0.0;
    }

    float AlpsColour(int fixture, int channel)
    {
        float level = AlpsLevel(fixture);
        return level > 0.0 ? AlpsLight(AlpsFrameChannel(fixture, channel), level) * ALPS_COLOUR_AT_FULL : 0.0;
    }

    // The texel VRSL samples for absolute channel <dmx> on a 26 x 240 grid, following
    // getValueAtCoords and IndustryRead. IndustryRead takes its row as an int, which drops
    // the fraction of dmx / 13, so a whole row of channels shares one texel row. The
    // thirteenth channel of a row, where dmx / 13 has no fraction, is read from the
    // thirteenth column of that same row.
    int2 AlpsVrslTexel(uint dmx)
    {
        uint x = dmx % 13;
        int row = (int)(dmx / 13) + 1;
        if (x == 0)
        {
            x = 13;
            row -= 1;
        }

        float2 texel = float2(1.0 / ALPS_GRID_WIDTH, 1.0 / ALPS_GRID_HEIGHT);
        float resMultiplier = ALPS_GRID_WIDTH / 13;
        float2 uv;
        uv.x = ((x * resMultiplier) * texel.x) - 0.015;
        uv.y = ((row * resMultiplier) * texel.y) - 0.001915;
        return int2(floor(uv * float2(ALPS_GRID_WIDTH, ALPS_GRID_HEIGHT)));
    }

    // Finds the row <n> of a band and its channel <x> VRSL reads from texel <p>, or returns
    // false. Each band reads the texel of the same row and channel in band 0.
    bool AlpsGridCell(int2 p, out int n, out int x)
    {
        n = 0;
        x = 0;
        if ((p.x & 1) == 0)
        {
            return false;
        }

        // Only odd columns are read, column 2x - 1 for channel x of a row.
        x = (p.x + 1) / 2;
        if (x < 1 || x > 13)
        {
            return false;
        }

        // Row n is read from texel row 2n + 1. Checked against VRSL's own maths.
        if ((p.y & 1) == 0)
        {
            return false;
        }

        n = (p.y - 1) / 2;
        if (n >= ALPS_DMX_BAND_ROWS)
        {
            return false;
        }

        int2 read = AlpsVrslTexel((uint)(13 * n + x));
        return read.x == p.x && read.y == p.y;
    }

    // Finds the show's fixture and the channel offset on channel <x> of row <n> of band
    // <band>, or returns false.
    bool AlpsCellAt(int band, int n, int x, AlpsShow show, out int fixture, out int offset)
    {
        fixture = 0;
        offset = 0;
        if (n >= AlpsDmxBandRows(band))
        {
            return false;
        }

        // The channel map names the fixture and which of its channels this is.
        int row = band * ALPS_DMX_BAND_ROWS + n;
        int cell = AlpsReadInt(show.channelFixtures + 13 * row + x - 1);
        if (cell < 0)
        {
            return false;
        }

        fixture = cell / ALPS_CHANNEL_FIXTURE_STEP;
        offset = cell - fixture * ALPS_CHANNEL_FIXTURE_STEP;
        return fixture < show.fixtureCount;
    }

    // What a segment of VRSL's multi light bar reads in its 48 channel mode
    // (GetDMX12CHRGB in VRSL-StandardSurface-Functions.cginc): a dimmer, then the colour.
    // As on the other fixtures the dimmer only says whether the segment is lit, and the
    // colour carries the level. A white segment at 100% is the most the bar's material shines.
    float AlpsBarSegmentValue(int fixture, int offset)
    {
        float level = AlpsLevel(fixture);
        if (level <= 0.0)
        {
            return 0.0;
        }

        return offset == 0 ? 1.0 : AlpsLight(AlpsFrameChannel(fixture, FrameRed + offset - 1), level);
    }

    // What VRSL's laser (VRSL-BasicLaser-DMX.shader) reads from its row, which is laid out
    // another way than the other fixtures' rows.
    float AlpsLaserValue(int fixture, int offset)
    {
        if (offset <= 1)
        {
            // Tilt leans the cone away from where it rests and pan turns it around that rest
            // axis, each by up to 90 degrees to either side. A pan past that is the pan
            // across from it with the tilt the other way, which points the same way. The
            // laser reads the tilt the other way round than the model.
            float pan = AlpsFrameChannel(fixture, FramePan);
            float tilt = AlpsFrameChannel(fixture, FrameTilt);
            pan -= 360.0 * round(pan / 360.0);
            if (abs(pan) > ALPS_LASER_ANGLE_RANGE)
            {
                pan -= pan > 0.0 ? 180.0 : -180.0;
                tilt = -tilt;
            }

            return ((offset == 0 ? -tilt : pan) + ALPS_LASER_ANGLE_RANGE) / (2.0 * ALPS_LASER_ANGLE_RANGE);
        }

        // The laser stretches its mesh by 1 plus lerp(-0.5, 5, v), and the model length is a
        // share of the mesh at ModelConeLengthLimit.
        float stretch = clamp(AlpsFrameChannel(fixture, FrameConeLength) / ALPS_CONE_LENGTH_LIMIT, ALPS_LASER_MIN_STRETCH, ALPS_LASER_MAX_STRETCH);
        if (offset == 2)
        {
            return (stretch - ALPS_LASER_MIN_STRETCH) / (ALPS_LASER_MAX_STRETCH - ALPS_LASER_MIN_STRETCH);
        }

        if (offset == 3)
        {
            // The width is lerp(-3.75, 20, v), which moves the far end of the cone out by the
            // same amount per unit however long the cone is. The model width is half the
            // opening angle, so a longer cone takes more of it to open as far.
            float opening = tan(radians(clamp(AlpsFrameChannel(fixture, FrameConeWidth), 0.0, 89.0))) * stretch / ALPS_LASER_MESH_SLOPE;
            return opening * -ALPS_LASER_MIN_WIDTH / (ALPS_LASER_MAX_WIDTH - ALPS_LASER_MIN_WIDTH);
        }

        if (offset == 4)
        {
            // The laser turns this into its flatness, which halves the depth of the cone at
            // 1 and leaves none of it at 1.999.
            return saturate(AlpsFrameChannel(fixture, FrameLaserFlatness) / 100.0);
        }

        if (offset == 5)
        {
            // Beams come whole, so a count on its way from one to another steps.
            float beams = clamp(round(AlpsFrameChannel(fixture, FrameLaserBeams)), ALPS_LASER_MIN_BEAMS, ALPS_LASER_MAX_BEAMS);
            return (beams - ALPS_LASER_MIN_BEAMS) / (ALPS_LASER_MAX_BEAMS - ALPS_LASER_MIN_BEAMS);
        }

        if (offset >= 7 && offset <= 9)
        {
            // Past 100% the colour alone carries the brightness, the way it does on the
            // other fixtures.
            float level = AlpsLevel(fixture);
            if (level <= 0.0)
            {
                return 0.0;
            }

            float light = AlpsLight(AlpsFrameChannel(fixture, FrameRed + offset - 7), max(level, 1.0));
            return light * pow(min(level, 1.0), ALPS_LASER_COLOUR_POWER);
        }

        if (offset == 10)
        {
            // The laser multiplies by the intensity twice and caps its beams in between, so
            // the middle of a beam follows the intensity and the glow around it the square.
            // Below 5% it switches itself off.
            return min(AlpsLevel(fixture), 1.0);
        }

        if (offset == 11)
        {
            // The channel reaches half the thickest beam at 1, and the laser reads on past it.
            float thickness = max(0.0, AlpsFrameChannel(fixture, FrameLaserThickness)) / 100.0 * ALPS_LASER_MAX_THICKNESS;
            return max(0.0, (thickness - ALPS_LASER_DMX_MIN_THICKNESS) / (ALPS_LASER_DMX_MAX_THICKNESS - ALPS_LASER_DMX_MIN_THICKNESS));
        }

        // The spin channel only says which way the beams go round, and at 0 it is the way the
        // spin grid counts. How far they have gone is in the spin grid.
        return 0.0;
    }

    // Model units to the 0..1 DMX value VRSL turns back into them. Values past the ends are
    // kept, since the grid holds floats.
    float AlpsDmxValue(int fixture, int offset, AlpsShow show)
    {
        int info = show.fixtureInfo + fixture * ALPS_FIXTURE_INFO_STRIDE;
        int layout = AlpsReadInt(info + ALPS_INFO_LAYOUT);
        if (layout == ALPS_LAYOUT_LASER)
        {
            return AlpsLaserValue(fixture, offset);
        }

        if (layout == ALPS_LAYOUT_BAR_SEGMENT)
        {
            return AlpsBarSegmentValue(fixture, offset);
        }

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
            float stretched = mesh * max(0.0, AlpsFrameChannel(fixture, FrameConeLength)) / ALPS_CONE_LENGTH_LIMIT;
            return (stretched - mesh) / 4.0;
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
            return AlpsDimmer(fixture);
        }

        if (offset == 6)
        {
            // The strobe grid is this same texture, where 1 holds the shutter open.
            return 1.0;
        }

        if (offset >= 7 && offset <= 9)
        {
            return AlpsColour(fixture, FrameRed + offset - 7);
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
                int n;
                int x;
                if (!AlpsGridCell(int2(floor(i.uv * float2(ALPS_GRID_WIDTH, ALPS_GRID_HEIGHT))), n, x))
                {
                    return 0;
                }

                // One loop over the bands, so the value compiles once.
                float4 result = float4(0, 0, 0, 1);
                [loop]
                for (int band = 0; band < ALPS_DMX_BANDS; band++)
                {
                    int fixture;
                    int offset;
                    if (AlpsCellAt(band, n, x, show, fixture, offset))
                    {
                        result.rgb += float3(band == 0, band == 1, band == 2) * AlpsDmxValue(fixture, offset, show);
                    }
                }

                return result;
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
                int n;
                int x;
                if (!AlpsGridCell(int2(floor(i.uv * float2(ALPS_GRID_WIDTH, ALPS_GRID_HEIGHT))), n, x))
                {
                    return 0;
                }

                // VRSL reads this grid from the band's own colour in either mode.
                float4 result = float4(0, 0, 0, 1);
                [loop]
                for (int band = 0; band < ALPS_DMX_BANDS; band++)
                {
                    int fixture;
                    int offset;
                    if (AlpsCellAt(band, n, x, show, fixture, offset) && (offset == 10 || offset == 6))
                    {
                        // VRSL turns a gobo's phase, on channel 10, into an angle of
                        // degrees(phase * 4) and flips it for an inverted pan, so the gobo
                        // angle goes in as that phase. A laser has no gobo. The phase on its
                        // channel 6 is how far its beams have gone round the cone, in turns.
                        int info = show.fixtureInfo + fixture * ALPS_FIXTURE_INFO_STRIDE;
                        bool laser = AlpsReadInt(info + ALPS_INFO_LAYOUT) == ALPS_LAYOUT_LASER;
                        float phase = 0.0;
                        if (laser && offset == 6)
                        {
                            phase = AlpsFrameChannel(fixture, FrameLaserSpin) / 360.0;
                        }
                        else if (!laser && offset == 10)
                        {
                            phase = radians(AlpsFrameChannel(fixture, FrameGoboRotation)) / 4.0 * AlpsRead(info + 2);
                        }

                        result.rgb += float3(band == 0, band == 1, band == 2) * phase;
                    }
                }

                return result;
            }
            ENDCG
        }
    }
}
