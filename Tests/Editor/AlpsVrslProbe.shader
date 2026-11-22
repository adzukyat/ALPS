// Reads DMX channels through VRSL's own getValueAtCoords, so a test can see what a VRSL
// mover would read from the grid ALPS draws. Texel x of the target is channel
// _AlpsProbeFirst + x + 1. It reads in nine universe mode, the way ALPS sets its fixtures up.
Shader "Hidden/ALPS/Tests/VRSL Probe"
{
    Properties
    {
        _AlpsProbeChannels ("Channels", Float) = 1
        _AlpsProbeFirst ("Channels Before The First", Float) = 0
        _NineUniverseMode ("Nine Universe Mode", Int) = 1
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
            #define VRSL_DMX
            #include "UnityCG.cginc"
            #include "Packages/com.acchosen.vr-stage-lighting/Runtime/Shaders/Shared/VRSL-Defines.cginc"
            #include "Packages/com.acchosen.vr-stage-lighting/Runtime/Shaders/Shared/VRSL-DMXFunctions.cginc"

            float _AlpsProbeChannels;
            float _AlpsProbeFirst;

            float4 frag(v2f_img i) : SV_Target
            {
                uint channel = (uint)(_AlpsProbeFirst + floor(i.uv.x * _AlpsProbeChannels)) + 1;
                return float4(getValueAtCoords(channel, _Udon_DMXGridRenderTexture), getValueAtCoordsRaw(channel, _Udon_DMXGridSpinTimer), 0, 1);
            }
            ENDCG
        }
    }
}
