// A penlight a show can light. In a world playing an ALPS show it takes its glow from the
// penlight texture the show player hands every shader of the world, the avatars' included
// (_Udon_AlpsPenlight). Anywhere else it glows in its own colour, which an avatar can animate.
//
// The file stands on its own, with no include of ALPS, so an avatar project takes it by
// copying this one file.
//
// The texture holds a row per penlight area: a texel per slot along the area's shape, the
// slot's colour times its brightness, then the first three rows of the area's world to
// area matrix and a texel of the slot count, the shape, the size and 1 for an area that is
// there. The penlight finds where it is along each area, blends the two slots around that
// place and takes the brightest area. It reads from where the object is, not from each
// vertex, so one penlight is one colour.
//
// Hidden/ALPS/Penlight Grid writes this layout, and the sizes mirror AlpsShowPlayer's.
Shader "ALPS/Penlight"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _EmissionMask ("Glow Mask", 2D) = "white" {}
        [HDR] _EmissionColor ("Own Glow Color", Color) = (1, 1, 1, 1)
        [ToggleUI] _FollowShow ("Follow The Show", Float) = 1
        _ShowIntensity ("Show Intensity", Range(0, 8)) = 1
    }

    SubShader
    {
        // Batching moves the vertices into another space, which loses where the penlight is.
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "DisableBatching" = "True" "VRCFallback" = "Unlit" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            #define ALPS_PENLIGHT_AREAS 4
            #define ALPS_PENLIGHT_SLOTS 64
            #define ALPS_PENLIGHT_WIDTH (ALPS_PENLIGHT_SLOTS + 4)
            #define ALPS_PENLIGHT_RADIAL 1
            #define ALPS_PENLIGHT_ANGLE 2

            Texture2D<float4> _Udon_AlpsPenlight;

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _EmissionMask;
            fixed4 _Color;
            half4 _EmissionColor;
            float _FollowShow;
            float _ShowIntensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 light : TEXCOORD1;
                // The show's glow, and in alpha whether a show gives one here at all.
                nointerpolation float4 show : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The glow the show gives a penlight at <world>. Alpha is 0 where no show plays:
            // the global is then unset or another size, or holds no area.
            float4 AlpsPenlightGlow(float3 world)
            {
                uint width;
                uint height;
                _Udon_AlpsPenlight.GetDimensions(width, height);
                if (width != ALPS_PENLIGHT_WIDTH || height != ALPS_PENLIGHT_AREAS)
                {
                    return 0;
                }

                float4 glow = 0;
                for (int area = 0; area < ALPS_PENLIGHT_AREAS; area++)
                {
                    float4 info = _Udon_AlpsPenlight.Load(int3(ALPS_PENLIGHT_SLOTS + 3, area, 0));
                    int slots = (int)round(info.x);
                    if (info.w < 0.5 || slots < 1)
                    {
                        continue;
                    }

                    float4 at = float4(world, 1.0);
                    float3 local = float3(
                        dot(_Udon_AlpsPenlight.Load(int3(ALPS_PENLIGHT_SLOTS, area, 0)), at),
                        dot(_Udon_AlpsPenlight.Load(int3(ALPS_PENLIGHT_SLOTS + 1, area, 0)), at),
                        dot(_Udon_AlpsPenlight.Load(int3(ALPS_PENLIGHT_SLOTS + 2, area, 0)), at));

                    // Where the penlight is along the slots, 0 at the first and 1 at the last.
                    int shape = (int)round(info.y);
                    float size = max(info.z, 0.0001);
                    float place = local.x / size + 0.5;
                    if (shape == ALPS_PENLIGHT_RADIAL)
                    {
                        place = length(local.xz) / size;
                    }
                    else if (shape == ALPS_PENLIGHT_ANGLE)
                    {
                        place = atan2(local.x, local.z) / (2.0 * UNITY_PI) + 0.5;
                    }

                    // Slot s sits at (s + 0.5) / slots. Past the ends the end slot is read,
                    // except round the angle, where the last slot meets the first.
                    float x = place * slots - 0.5;
                    int first = (int)floor(x);
                    int second = first + 1;
                    if (shape == ALPS_PENLIGHT_ANGLE)
                    {
                        first = ((first % slots) + slots) % slots;
                        second = ((second % slots) + slots) % slots;
                    }
                    else
                    {
                        first = clamp(first, 0, slots - 1);
                        second = clamp(second, 0, slots - 1);
                    }

                    float4 slot = lerp(
                        _Udon_AlpsPenlight.Load(int3(first, area, 0)),
                        _Udon_AlpsPenlight.Load(int3(second, area, 0)),
                        x - floor(x));
                    glow.rgb = max(glow.rgb, slot.rgb);
                    glow.a = 1.0;
                }

                return glow;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float3 normal = UnityObjectToWorldNormal(v.normal);
                o.light = ShadeSH9(float4(normal, 1.0)) + _LightColor0.rgb * saturate(dot(normal, _WorldSpaceLightPos0.xyz));
                o.show = AlpsPenlightGlow(unity_ObjectToWorld._m03_m13_m23);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                half3 glow = i.show.a > 0.5 && _FollowShow > 0.5 ? i.show.rgb * _ShowIntensity : _EmissionColor.rgb;
                half3 colour = albedo.rgb * i.light + tex2D(_EmissionMask, i.uv).rgb * glow;
                UNITY_APPLY_FOG(i.fogCoord, colour);
                return fixed4(colour, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
