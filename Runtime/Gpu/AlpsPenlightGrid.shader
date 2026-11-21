// Lays the evaluated frames of the show's penlight slots out as the texture the penlights
// read, a row per area. A row holds a texel per slot, the slot's colour times its brightness
// with the brightness in alpha, then the first three rows of the area's world to area matrix
// and a texel of the slot count, the shape, the size and 1 for an area that is there.
// Rows the show holds no area for stay empty, so a penlight finds no area in them.
//
// The penlight shader (Runtime/Penlight/AlpsPenlight.shader) reads this layout on its own,
// since an avatar brings it without the rest of ALPS. A change here changes it there too.
Shader "Hidden/ALPS/Penlight Grid"
{
    Properties
    {
        _MainTex ("Frames", 2D) = "black" {}
        _AlpsData ("Show Data", 2D) = "black" {}
    }

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
            #include "UnityCG.cginc"
            #include "AlpsEvaluator.hlsl"

            Texture2D<float4> _MainTex;

            float AlpsFrameChannel(int fixture, int channel)
            {
                return _MainTex.Load(int3(channel / 4, fixture, 0))[channel % 4];
            }

            float4 frag(v2f_img i) : SV_Target
            {
                int2 p = int2(floor(i.uv * float2(ALPS_PENLIGHT_WIDTH, ALPS_PENLIGHT_AREAS)));
                if (p.y >= AlpsReadInt(ALPS_HEADER_PENLIGHT_AREAS))
                {
                    return 0;
                }

                int area = AlpsReadInt(ALPS_HEADER_PENLIGHT) + p.y * ALPS_PENLIGHT_STRIDE;
                int slots = AlpsReadInt(area + ALPS_PENLIGHT_SLOT_COUNT);
                int column = p.x - ALPS_PENLIGHT_SLOTS;
                if (column >= 3)
                {
                    return float4(slots, AlpsRead(area + ALPS_PENLIGHT_SHAPE), AlpsRead(area + ALPS_PENLIGHT_SIZE), 1);
                }

                if (column >= 0)
                {
                    int row = area + ALPS_PENLIGHT_MATRIX + column * 4;
                    return float4(AlpsRead(row), AlpsRead(row + 1), AlpsRead(row + 2), AlpsRead(row + 3));
                }

                if (p.x >= slots)
                {
                    return 0;
                }

                int fixture = AlpsReadInt(area + ALPS_PENLIGHT_FIRST_SLOT + p.x);
                if (fixture < 0 || fixture >= AlpsReadInt(ALPS_HEADER_FIXTURE_COUNT))
                {
                    return 0;
                }

                // The colour is authored the way a tint is, which Unity turns from gamma into
                // linear in a linear project, so a penlight shows the colour a fixture would.
                float level = max(0.0, AlpsFrameChannel(fixture, FrameBrightness) * AlpsFrameChannel(fixture, FrameBrightnessScale) / 100.0);
                float3 colour = float3(AlpsFrameChannel(fixture, FrameRed), AlpsFrameChannel(fixture, FrameGreen), AlpsFrameChannel(fixture, FrameBlue));
                #ifndef UNITY_COLORSPACE_GAMMA
                colour = float3(GammaToLinearSpaceExact(colour.r), GammaToLinearSpaceExact(colour.g), GammaToLinearSpaceExact(colour.b));
                #endif
                return float4(colour * level, level);
            }
            ENDCG
        }
    }
}
