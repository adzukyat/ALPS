// Evaluates the show on the GPU. Each row of the target is one fixture, four RGBA texels
// wide, holding the 13 frame channels of AlpsShowEvaluator in order.
Shader "Hidden/ALPS/Frames"
{
    Properties
    {
        _AlpsData ("Show Data", 2D) = "black" {}
        _AlpsTime ("Time", Float) = 0
        _AlpsFrameRows ("Frame Rows", Float) = 1
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

            float _AlpsFrameRows;

            float4 frag(v2f_img i) : SV_Target
            {
                AlpsShow show = AlpsLoadShow();
                int fixture = (int)floor(i.uv.y * _AlpsFrameRows);
                if (fixture >= show.fixtureCount)
                {
                    return 0;
                }

                float frame[FrameStride];
                AlpsEvaluateFixture(fixture, _AlpsTime, frame, show);

                int first = (int)floor(i.uv.x * 4.0) * 4;
                float4 result = 0;
                [unroll]
                for (int c = 0; c < 4; c++)
                {
                    int ch = first + c;
                    result[c] = ch < FrameStride ? frame[min(ch, FrameStride - 1)] : 0.0;
                }

                return result;
            }
            ENDCG
        }
    }
}
