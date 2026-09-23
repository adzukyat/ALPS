// The clip strip the Timeline window draws on ALPS clips: clip 0 of a show compiled from
// the clip alone, evaluated on its own for one fixture per row across the clip's time.
// A texel holds the color, and in alpha the brightness as a share of 100%, 0 when no
// brightness effect lights it, or -1 where the clip plays no color.
Shader "Hidden/ALPS/Clip Strip"
{
    Properties
    {
        _AlpsData ("Show Data", 2D) = "black" {}
        _AlpsStripStart ("Start", Float) = 0
        _AlpsStripEnd ("End", Float) = 1
        _AlpsStripLanes ("Lanes", Float) = 1
        _AlpsStripFixture0 ("First Lane Fixture", Float) = 0
        _AlpsStripFixture1 ("Second Lane Fixture", Float) = 0
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
            #include "Packages/me.adzuki.live-performance-system/Runtime/Gpu/AlpsEvaluator.hlsl"

            float _AlpsStripStart;
            float _AlpsStripEnd;
            float _AlpsStripLanes;
            float _AlpsStripFixture0;
            float _AlpsStripFixture1;

            float4 frag(v2f_img i) : SV_Target
            {
                AlpsShow show = AlpsLoadShow();
                int lane = (int)floor(i.uv.y * _AlpsStripLanes);
                int fixture = (int)round(lane == 0 ? _AlpsStripFixture0 : _AlpsStripFixture1);
                float time = lerp(_AlpsStripStart, _AlpsStripEnd, i.uv.x);

                AlpsFrame frame;
                [unroll]
                for (int f = 0; f < FrameStride; f++)
                {
                    frame.value[f] = 0.0;
                    frame.written[f] = false;
                }

                AlpsEvaluateClip(0, fixture, time, frame, show);
                if (!frame.written[FrameRed])
                {
                    return float4(0, 0, 0, -1);
                }

                float brightness = frame.written[FrameBrightness] ? frame.value[FrameBrightness] / 100.0 : 0.0;
                return float4(frame.value[FrameRed], frame.value[FrameGreen], frame.value[FrameBlue], brightness);
            }
            ENDCG
        }
    }
}
