using UnityEngine;
using UnityEngine.Playables;
using VRC.SDKBase;
using VRSL;

#if UDONSHARP
using UdonSharp;
#endif

namespace ManeuverForVRC
{
    /// <summary>
    /// Plays a compiled show in VRChat. It follows <see cref="PlayableDirector.time"/>,
    /// evaluates every fixture with <see cref="MfvShowEvaluator"/> and writes the result to
    /// the fixtures. The arrays are filled at build time by the scene processor.
    /// </summary>
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Maneuver For VRC/MFV Show Player")]
    public class MfvShowPlayer : UdonSharpBehaviour
#else
    [AddComponentMenu("Maneuver For VRC/MFV Show Player")]
    public class MfvShowPlayer : MonoBehaviour
#endif
    {
        public const int AdapterNone = 0;
        public const int AdapterVRSLDmxStatic = 1;

        /// <summary>Model cone width at which VRSL reaches its widest cone.</summary>
        public const float ModelConeWidthLimit = 90f;
        /// <summary>Model cone length at which VRSL reaches its longest cone.</summary>
        public const float ModelConeLengthLimit = 50f;
        public const float VrslMaxConeWidth = 5.5f;
        public const float VrslMinConeLength = 0.5f;
        public const float VrslMaxConeLength = 10f;
        public const float VrslDefaultTiltOffset = 90f;

        public PlayableDirector director;

        public float[] clips = new float[0];
        public float[] effects = new float[0];
        public float[] parameters = new float[0];
        public float[] colors = new float[0];
        public float[] gobos = new float[0];
        public string[] userNames = new string[0];

        public int[] groupCount = new int[0];
        public int[] groupIndex = new int[0];

        public int[] fixtureAdapter = new int[0];
        public VRStageLighting_DMX_Static[] vrslFixtures = new VRStageLighting_DMX_Static[0];
        public Transform[] aimTransforms = new Transform[0];

        private float[] _defaults;
        private float[] _frame;
        private float[] _clipFrame;
        private float[] _clipWritten;
        private float[] _sum;
        private float[] _weightSum;
        private float[] _scratch;
        private float[] _aimPan;
        private float[] _aimTilt;
        private bool[] _aiming;
        private VRCPlayerApi[] _players;
        private MaterialPropertyBlock _block;
        private bool _initialized;
        private float _lastTime = -1f;
        private bool _anyAiming;

        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            if (director != null)
            {
                EvaluateAt((float)director.time);
            }
        }

        /// <summary>Allocates buffers and captures every fixture's default state.</summary>
        public void Initialize()
        {
            var count = fixtureAdapter.Length;
            _defaults = new float[count * MfvShowEvaluator.FrameStride];
            _frame = new float[MfvShowEvaluator.FrameStride];
            _clipFrame = new float[MfvShowEvaluator.FrameStride];
            _clipWritten = new float[MfvShowEvaluator.FrameStride];
            _sum = new float[MfvShowEvaluator.FrameStride];
            _weightSum = new float[MfvShowEvaluator.FrameStride];
            _scratch = new float[1];
            _aimPan = new float[count];
            _aimTilt = new float[count];
            _aiming = new bool[count];
            _players = new VRCPlayerApi[82];
            _block = new MaterialPropertyBlock();

            for (var i = 0; i < count; i++)
            {
                var offset = i * MfvShowEvaluator.FrameStride;
                if (fixtureAdapter[i] == AdapterVRSLDmxStatic && i < vrslFixtures.Length && vrslFixtures[i] != null)
                {
                    CaptureVRSL(vrslFixtures[i], _defaults, offset);
                }
                else
                {
                    WriteNeutralFrame(_defaults, offset);
                }
            }

            _initialized = true;
        }

        public void EvaluateAt(float time)
        {
            if (!_initialized)
            {
                Initialize();
            }

            // A paused timeline gives the same frame again, unless a light is following a user.
            if (time == _lastTime && !_anyAiming)
            {
                return;
            }

            var deltaTime = _lastTime >= 0f ? Mathf.Abs(time - _lastTime) : 0f;
            _lastTime = time;
            _anyAiming = false;

            var clipCount = clips.Length / MfvShowEvaluator.ClipStride;
            for (var fixture = 0; fixture < fixtureAdapter.Length; fixture++)
            {
                MfvShowEvaluator.EvaluateFixture(
                    clips, effects, parameters, colors, gobos, clipCount,
                    groupCount, groupIndex,
                    fixture, time, _defaults, _frame,
                    _clipFrame, _clipWritten, _sum, _weightSum, _scratch);

                ResolveTracking(fixture, deltaTime);
                if (_aiming[fixture])
                {
                    _anyAiming = true;
                }

                if (fixtureAdapter[fixture] == AdapterVRSLDmxStatic && fixture < vrslFixtures.Length && vrslFixtures[fixture] != null)
                {
                    ApplyVRSL(vrslFixtures[fixture], _frame, 0, _block);
                }
            }
        }

        /// <summary>The last evaluated frame channel, for tests and debugging.</summary>
        public float GetFrameValue(int channel)
        {
            return _frame != null && channel >= 0 && channel < _frame.Length ? _frame[channel] : 0f;
        }

        // ==================================================================================
        // Tracking
        // ==================================================================================

        private void ResolveTracking(int fixture, float deltaTime)
        {
            var track = MfvShowEvaluator.ToInt(_frame[MfvShowEvaluator.FrameTrackEffect]);
            if (track <= 0)
            {
                _aiming[fixture] = false;
                return;
            }

            var row = (track - 1) * MfvShowEvaluator.EffectStride;
            var nameIndex = MfvShowEvaluator.ToInt(effects[row + MfvShowEvaluator.EffectScalarD]);
            var aim = fixture < aimTransforms.Length ? aimTransforms[fixture] : null;
            if (aim == null || nameIndex < 0 || nameIndex >= userNames.Length)
            {
                return;
            }

            var player = FindPlayer(userNames[nameIndex]);
            if (player == null)
            {
                return;
            }

            var head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            var local = aim.InverseTransformPoint(head);
            var pan = AimPan(local);
            var tilt = AimTilt(local);

            if (!_aiming[fixture])
            {
                _aimPan[fixture] = pan;
                _aimTilt[fixture] = tilt;
                _aiming[fixture] = true;
            }
            else
            {
                var speed = effects[row + MfvShowEvaluator.EffectScalarC];
                var follow = 1f - Mathf.Exp(-Mathf.Max(0f, speed) * deltaTime);
                _aimPan[fixture] = Mathf.LerpAngle(_aimPan[fixture], pan, follow);
                _aimTilt[fixture] = Mathf.Lerp(_aimTilt[fixture], tilt, follow);
            }

            _frame[MfvShowEvaluator.FramePan] = _aimPan[fixture];
            _frame[MfvShowEvaluator.FrameTilt] = _aimTilt[fixture];
        }

        private VRCPlayerApi FindPlayer(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return null;
            }

            var count = VRCPlayerApi.GetPlayerCount();
            if (count <= 0)
            {
                return null;
            }

            if (_players == null || _players.Length < count)
            {
                _players = new VRCPlayerApi[count];
            }

            VRCPlayerApi.GetPlayers(_players);
            for (var i = 0; i < count; i++)
            {
                var candidate = _players[i];
                if (Utilities.IsValid(candidate) && candidate.displayName == displayName)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Pan in model degrees that points the head at a point in the aim transform's space.
        /// VRSL pans around the fixture mesh's local Z axis.
        /// </summary>
        public static float AimPan(Vector3 local)
        {
            return Mathf.Atan2(local.x, -local.y) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Tilt in model degrees that points the head at a point in the aim transform's space.
        /// VRSL tilts around the fixture mesh's local X axis, and model tilt 0 aims along the
        /// mesh's local -Z axis.
        /// </summary>
        public static float AimTilt(Vector3 local)
        {
            var horizontal = Mathf.Sqrt(local.x * local.x + local.y * local.y);
            return Mathf.Atan2(horizontal, -local.z) * Mathf.Rad2Deg;
        }

        // ==================================================================================
        // VRSL DMX Static adapter
        // ==================================================================================

        /// <summary>The Transform whose space VRSL's shaders rotate the head in.</summary>
        public static Transform GetVRSLAimTransform(VRStageLighting_DMX_Static fixture)
        {
            if (fixture == null)
            {
                return null;
            }

            var renderers = fixture.objRenderers;
            if (renderers != null && renderers.Length > 0 && renderers[0] != null)
            {
                return renderers[0].transform;
            }

            return fixture.transform;
        }

        /// <summary>A frame for a fixture nothing is known about.</summary>
        public static void WriteNeutralFrame(float[] frame, int offset)
        {
            for (var ch = 0; ch < MfvShowEvaluator.FrameStride; ch++)
            {
                frame[offset + ch] = 0f;
            }

            frame[offset + MfvShowEvaluator.FrameBrightness] = 100f;
            frame[offset + MfvShowEvaluator.FrameRed] = 1f;
            frame[offset + MfvShowEvaluator.FrameGreen] = 1f;
            frame[offset + MfvShowEvaluator.FrameBlue] = 1f;
            frame[offset + MfvShowEvaluator.FrameGobo] = MfvShowEvaluator.GoboOff;
            frame[offset + MfvShowEvaluator.FrameBrightnessScale] = 1f;
        }

        /// <summary>Reads a VRSL fixture's fields into model units.</summary>
        public static void CaptureVRSL(VRStageLighting_DMX_Static fixture, float[] frame, int offset)
        {
            WriteNeutralFrame(frame, offset);
            frame[offset + MfvShowEvaluator.FramePan] = -fixture.panOffsetBlueGreen;
            frame[offset + MfvShowEvaluator.FrameTilt] = fixture.tiltOffsetBlue - VrslDefaultTiltOffset;
            // An HDR tint such as VRSL's default white of 2 becomes brightness above 100%
            // over an SDR color, the inverse of ApplyVRSL.
            var color = fixture.lightColorTint;
            var peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            var excess = peak > 1f ? peak : 1f;
            frame[offset + MfvShowEvaluator.FrameBrightness] = fixture.globalIntensity * 100f * excess;
            frame[offset + MfvShowEvaluator.FrameRed] = color.r / excess;
            frame[offset + MfvShowEvaluator.FrameGreen] = color.g / excess;
            frame[offset + MfvShowEvaluator.FrameBlue] = color.b / excess;
            frame[offset + MfvShowEvaluator.FrameConeWidth] =
                Mathf.Clamp01(fixture.coneWidth / VrslMaxConeWidth) * ModelConeWidthLimit;
            frame[offset + MfvShowEvaluator.FrameConeLength] =
                Mathf.InverseLerp(VrslMinConeLength, VrslMaxConeLength, fixture.coneLength) * ModelConeLengthLimit;
            frame[offset + MfvShowEvaluator.FrameGobo] = fixture.selectGOBO;
        }

        /// <summary>Writes a model frame to a VRSL fixture.</summary>
        public static void ApplyVRSL(VRStageLighting_DMX_Static fixture, float[] frame, int offset, MaterialPropertyBlock block)
        {
            var brightness = frame[offset + MfvShowEvaluator.FrameBrightness] * frame[offset + MfvShowEvaluator.FrameBrightnessScale];

            fixture.enableDMXChannels = false;
            fixture.enableStrobe = false;
            fixture.enableAutoSpin = false;
            fixture.panOffsetBlueGreen = -frame[offset + MfvShowEvaluator.FramePan];
            fixture.tiltOffsetBlue = frame[offset + MfvShowEvaluator.FrameTilt] + VrslDefaultTiltOffset;
            // White at 100% is a tint of 1. VRSL caps global intensity at 1, so brightness
            // above 100% scales the tint instead, reaching VRSL's default white of 2 at 200%.
            var intensity = Mathf.Max(0f, brightness / 100f);
            var tintScale = intensity > 1f ? intensity : 1f;
            fixture.globalIntensity = Mathf.Min(intensity, 1f);
            fixture.lightColorTint = new Color(
                frame[offset + MfvShowEvaluator.FrameRed] * tintScale,
                frame[offset + MfvShowEvaluator.FrameGreen] * tintScale,
                frame[offset + MfvShowEvaluator.FrameBlue] * tintScale,
                1f);
            fixture.coneWidth = Mathf.Clamp01(frame[offset + MfvShowEvaluator.FrameConeWidth] / ModelConeWidthLimit) * VrslMaxConeWidth;
            fixture.coneLength = Mathf.Lerp(
                VrslMinConeLength,
                VrslMaxConeLength,
                Mathf.Clamp01(frame[offset + MfvShowEvaluator.FrameConeLength] / ModelConeLengthLimit));
            fixture.selectGOBO = Mathf.Clamp(MfvShowEvaluator.ToInt(frame[offset + MfvShowEvaluator.FrameGobo]), 1, 8);
            fixture._UpdateInstancedProperties();

            // VRSL replaces the whole property block above, so the gobo angle is added after it.
            // Stock VRSL shaders ignore the property, the patched ones rotate the gobo.
            var renderers = fixture.objRenderers;
            if (renderers == null || block == null)
            {
                return;
            }

            var rotation = frame[offset + MfvShowEvaluator.FrameGoboRotation];
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(block);
                block.SetFloat("_GoboRotation", rotation);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
