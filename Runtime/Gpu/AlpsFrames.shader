// Evaluates the show. Each row of the target is one fixture, four RGBA texels wide, holding
// the 13 frame channels of AlpsShowLayout in order, whether the head is aimed at a tracked
// user, and the token of the show that drew it.
//
// Pan and tilt start from the previous frame, so a head nothing moves stops where it was. The
// frames targets are shared by every show of a scene, so only a previous frame the same show
// drew counts, and a show without a token starts from the defaults.
//
// A head tracking a user turns toward the user's hips, found from the fixture's aim space
// in the show data. It follows at the effect's speed from where the previous frames left it,
// and jumps straight there when it was not aimed before.
Shader "Hidden/ALPS/Frames"
{
    Properties
    {
        _AlpsData ("Show Data", 2D) = "black" {}
        _AlpsPrevFrames ("Previous Frames", 2D) = "black" {}
        _AlpsTime ("Time", Float) = 0
        _AlpsDeltaTime ("Delta Time", Float) = 0
        _AlpsFrameRows ("Frame Rows", Float) = 1
        _AlpsShowToken ("Show Token", Float) = 0
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

            Texture2D<float4> _AlpsPrevFrames;
            float _AlpsFrameRows;
            float _AlpsDeltaTime;
            float _AlpsShowToken;

            // Hips positions of the tracked users, in the order of the show's user names.
            // w is 1 while the user is in the instance.
            float4 _AlpsTrackTarget0;
            float4 _AlpsTrackTarget1;
            float4 _AlpsTrackTarget2;
            float4 _AlpsTrackTarget3;
            float4 _AlpsTrackTarget4;
            float4 _AlpsTrackTarget5;
            float4 _AlpsTrackTarget6;
            float4 _AlpsTrackTarget7;

            float4 AlpsTrackTarget(int user)
            {
                if (user == 0) return _AlpsTrackTarget0;
                if (user == 1) return _AlpsTrackTarget1;
                if (user == 2) return _AlpsTrackTarget2;
                if (user == 3) return _AlpsTrackTarget3;
                if (user == 4) return _AlpsTrackTarget4;
                if (user == 5) return _AlpsTrackTarget5;
                if (user == 6) return _AlpsTrackTarget6;
                if (user == 7) return _AlpsTrackTarget7;
                return 0;
            }

            // Points the head at its user, if the frame follows one. Returns 1 when it did.
            float AlpsTrack(int fixture, inout float frame[FrameStride], AlpsShow show)
            {
                int track = (int)round(frame[FrameTrackEffect]);
                if (track <= 0) return 0.0;

                int effectRow = show.effects + (track - 1) * EffectStride;
                float4 target = AlpsTrackTarget(AlpsReadInt(effectRow + EffectScalarD));
                if (target.w < 0.5) return 0.0;

                // The user's hips in the space VRSL turns the head in.
                int aim = show.aim + fixture * ALPS_AIM_STRIDE;
                float3 local;
                local.x = AlpsRead(aim) * target.x + AlpsRead(aim + 1) * target.y + AlpsRead(aim + 2) * target.z + AlpsRead(aim + 3);
                local.y = AlpsRead(aim + 4) * target.x + AlpsRead(aim + 5) * target.y + AlpsRead(aim + 6) * target.z + AlpsRead(aim + 7);
                local.z = AlpsRead(aim + 8) * target.x + AlpsRead(aim + 9) * target.y + AlpsRead(aim + 10) * target.z + AlpsRead(aim + 11);

                // VRSL pans around the mesh's local Z axis and tilt 0 aims along its -Z axis.
                float pan = degrees(atan2(local.x, -local.y));
                float tilt = degrees(atan2(sqrt(local.x * local.x + local.y * local.y), -local.z));

                float4 previous = _AlpsPrevFrames.Load(int3(0, fixture, 0));
                if (_AlpsPrevFrames.Load(int3(FrameAimed / 4, fixture, 0))[FrameAimed % 4] > 0.5)
                {
                    float follow = 1.0 - exp(-max(0.0, AlpsRead(effectRow + EffectScalarC)) * _AlpsDeltaTime);
                    float turn = pan - previous.x;
                    turn -= floor(turn / 360.0) * 360.0;
                    turn = turn > 180.0 ? turn - 360.0 : turn;
                    pan = previous.x + turn * follow;
                    tilt = lerp(previous.y, tilt, follow);
                }

                frame[FramePan] = pan;
                frame[FrameTilt] = tilt;
                return 1.0;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                AlpsShow show = AlpsLoadShow();
                int fixture = (int)floor(i.uv.y * _AlpsFrameRows);
                if (fixture >= show.fixtureCount)
                {
                    return 0;
                }

                float4 previous = _AlpsPrevFrames.Load(int3(0, fixture, 0));
                float token = _AlpsPrevFrames.Load(int3(FrameShowToken / 4, fixture, 0))[FrameShowToken % 4];
                bool hasHeld = _AlpsShowToken > 0.5 && abs(token - _AlpsShowToken) < 0.5;

                float frame[FrameStride];
                AlpsEvaluateFixture(fixture, _AlpsTime, hasHeld, float2(previous[FramePan], previous[FrameTilt]), frame, show);
                float aimed = AlpsTrack(fixture, frame, show);

                int first = (int)floor(i.uv.x * 4.0) * 4;
                float4 result = 0;
                [unroll]
                for (int c = 0; c < 4; c++)
                {
                    int ch = first + c;
                    result[c] = ch < FrameStride ? frame[min(ch, FrameStride - 1)] : (ch == FrameAimed ? aimed : (ch == FrameShowToken ? _AlpsShowToken : 0.0));
                }

                return result;
            }
            ENDCG
        }
    }
}
