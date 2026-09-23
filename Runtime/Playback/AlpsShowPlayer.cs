using UnityEngine;
using UnityEngine.Playables;
using VRC.SDKBase;
using VRSL;

#if UDONSHARP
using UdonSharp;
#endif

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Plays a compiled show in VRChat. It follows <see cref="PlayableDirector.time"/>,
    /// evaluates every fixture with <see cref="AlpsShowEvaluator"/> and writes the result to
    /// the fixtures. The arrays are filled at build time by the scene processor.
    ///
    /// Writing to VRSL costs far more than evaluating, so a fixture is only written when its
    /// frame changed since the last write, and only its gobo angle when nothing else did.
    /// One fixture per frame is written in full regardless, in turn, which repairs anything
    /// that rebuilt a fixture's property block behind the player's back.
    /// </summary>
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Adzuki Live Performance System/ALPS Show Player")]
    public class AlpsShowPlayer : UdonSharpBehaviour
#else
    [AddComponentMenu("Adzuki Live Performance System/ALPS Show Player")]
    public class AlpsShowPlayer : MonoBehaviour
#endif
    {
        public const int AdapterNone = 0;
        public const int AdapterVRSLDmxStatic = 1;

        public const int ChangeNone = 0;
        public const int ChangeGoboRotation = 1;
        public const int ChangeAll = 2;

        /// <summary>Model cone width at which VRSL reaches the widest cone its inspector offers.</summary>
        public const float ModelConeWidthLimit = 90f;
        /// <summary>
        /// Model cone length at which VRSL's cone fills its whole mesh. Longer cones stretch the
        /// mesh in proportion, so 100 is twice the fixture's own mesh length.
        /// </summary>
        public const float ModelConeLengthLimit = 50f;
        /// <summary>
        /// VRSL cone width at a model width of 0. VRSL's inspector stops at 0, but its shader
        /// reaches -0.5 when driven by DMX, and the static path narrows the same way below 0.
        /// </summary>
        public const float VrslMinConeWidth = -0.5f;
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
        public int[] positions = new int[0];
        public int[] bucketStart = new int[0];
        public int[] bucketClips = new int[0];
        public float bucketSeconds = 1f;

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
        private int[] _active;
        private float[] _activeWeight;
        private float[] _applied;
        private bool[] _hasApplied;
        private int _refreshFixture;
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
            _defaults = new float[count * AlpsShowEvaluator.FrameStride];
            _frame = new float[AlpsShowEvaluator.FrameStride];
            _clipFrame = new float[AlpsShowEvaluator.FrameStride];
            _clipWritten = new float[AlpsShowEvaluator.FrameStride];
            _sum = new float[AlpsShowEvaluator.FrameStride];
            _weightSum = new float[AlpsShowEvaluator.FrameStride];
            _scratch = new float[1];
            var clipCount = clips.Length / AlpsShowEvaluator.ClipStride;
            _active = new int[clipCount];
            _activeWeight = new float[clipCount];
            _applied = new float[count * AlpsShowEvaluator.FrameStride];
            _hasApplied = new bool[count];
            _refreshFixture = 0;
            _aimPan = new float[count];
            _aimTilt = new float[count];
            _aiming = new bool[count];
            _players = new VRCPlayerApi[82];
            _block = new MaterialPropertyBlock();

            for (var i = 0; i < count; i++)
            {
                var offset = i * AlpsShowEvaluator.FrameStride;
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

            var count = fixtureAdapter.Length;
            var activeCount = AlpsShowEvaluator.ActiveClips(clips, bucketStart, bucketClips, bucketSeconds, time, _active, _activeWeight);
            var refresh = _refreshFixture;
            _refreshFixture = count > 0 ? (refresh + 1) % count : 0;

            for (var fixture = 0; fixture < count; fixture++)
            {
                AlpsShowEvaluator.EvaluateFixture(
                    clips, effects, parameters, colors, gobos, positions,
                    _active, _activeWeight, activeCount,
                    groupCount, groupIndex,
                    fixture, time, _defaults, _frame,
                    _clipFrame, _clipWritten, _sum, _weightSum, _scratch);

                ResolveTracking(fixture, deltaTime);
                if (_aiming[fixture])
                {
                    _anyAiming = true;
                }

                if (fixtureAdapter[fixture] != AdapterVRSLDmxStatic || fixture >= vrslFixtures.Length || vrslFixtures[fixture] == null)
                {
                    continue;
                }

                var change = FrameChange(_frame, 0, _applied, fixture * AlpsShowEvaluator.FrameStride, !_hasApplied[fixture] || fixture == refresh);
                _hasApplied[fixture] = true;
                if (change == ChangeAll)
                {
                    ApplyVRSL(vrslFixtures[fixture], _frame, 0, _block);
                }
                else if (change == ChangeGoboRotation)
                {
                    ApplyVRSLGoboRotation(vrslFixtures[fixture], _frame[AlpsShowEvaluator.FrameGoboRotation], _block);
                }
            }
        }

        /// <summary>
        /// Compares a frame with the one last written to a fixture, keeps it as the new last
        /// written one, and says what has to be written: nothing, only the gobo angle, or all
        /// of it. <paramref name="force"/> asks for all of it whatever changed.
        /// </summary>
        public static int FrameChange(float[] frame, int offset, float[] applied, int appliedOffset, bool force)
        {
            var change = force ? ChangeAll : ChangeNone;
            for (var ch = 0; ch < AlpsShowEvaluator.FrameStride; ch++)
            {
                var value = frame[offset + ch];
                if (applied[appliedOffset + ch] == value)
                {
                    continue;
                }

                applied[appliedOffset + ch] = value;
                if (ch == AlpsShowEvaluator.FrameGoboRotation)
                {
                    if (change == ChangeNone)
                    {
                        change = ChangeGoboRotation;
                    }
                }
                else
                {
                    change = ChangeAll;
                }
            }

            return change;
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
            var track = AlpsShowEvaluator.ToInt(_frame[AlpsShowEvaluator.FrameTrackEffect]);
            if (track <= 0)
            {
                _aiming[fixture] = false;
                return;
            }

            var row = (track - 1) * AlpsShowEvaluator.EffectStride;
            var nameIndex = AlpsShowEvaluator.ToInt(effects[row + AlpsShowEvaluator.EffectScalarD]);
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
                var speed = effects[row + AlpsShowEvaluator.EffectScalarC];
                var follow = 1f - Mathf.Exp(-Mathf.Max(0f, speed) * deltaTime);
                _aimPan[fixture] = Mathf.LerpAngle(_aimPan[fixture], pan, follow);
                _aimTilt[fixture] = Mathf.Lerp(_aimTilt[fixture], tilt, follow);
            }

            _frame[AlpsShowEvaluator.FramePan] = _aimPan[fixture];
            _frame[AlpsShowEvaluator.FrameTilt] = _aimTilt[fixture];
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
            for (var ch = 0; ch < AlpsShowEvaluator.FrameStride; ch++)
            {
                frame[offset + ch] = 0f;
            }

            frame[offset + AlpsShowEvaluator.FrameBrightness] = 100f;
            frame[offset + AlpsShowEvaluator.FrameRed] = 1f;
            frame[offset + AlpsShowEvaluator.FrameGreen] = 1f;
            frame[offset + AlpsShowEvaluator.FrameBlue] = 1f;
            frame[offset + AlpsShowEvaluator.FrameGobo] = AlpsShowEvaluator.GoboOff;
            frame[offset + AlpsShowEvaluator.FrameBrightnessScale] = 1f;
            frame[offset + AlpsShowEvaluator.FrameConeMeshLength] = 1f;
        }

        /// <summary>Reads a VRSL fixture's fields into model units.</summary>
        public static void CaptureVRSL(VRStageLighting_DMX_Static fixture, float[] frame, int offset)
        {
            WriteNeutralFrame(frame, offset);
            frame[offset + AlpsShowEvaluator.FramePan] = -fixture.panOffsetBlueGreen;
            frame[offset + AlpsShowEvaluator.FrameTilt] = fixture.tiltOffsetBlue - VrslDefaultTiltOffset;
            // An HDR tint such as VRSL's default white of 2 becomes brightness above 100%
            // over an SDR color, the inverse of ApplyVRSL.
            var color = fixture.lightColorTint;
            var peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            var excess = peak > 1f ? peak : 1f;
            frame[offset + AlpsShowEvaluator.FrameBrightness] = fixture.globalIntensity * 100f * excess;
            frame[offset + AlpsShowEvaluator.FrameRed] = color.r / excess;
            frame[offset + AlpsShowEvaluator.FrameGreen] = color.g / excess;
            frame[offset + AlpsShowEvaluator.FrameBlue] = color.b / excess;
            frame[offset + AlpsShowEvaluator.FrameConeWidth] =
                Mathf.Max(0f, (fixture.coneWidth - VrslMinConeWidth) / (VrslMaxConeWidth - VrslMinConeWidth)) * ModelConeWidthLimit;
            frame[offset + AlpsShowEvaluator.FrameConeLength] =
                Mathf.InverseLerp(VrslMinConeLength, VrslMaxConeLength, fixture.coneLength) * ModelConeLengthLimit;
            frame[offset + AlpsShowEvaluator.FrameConeMeshLength] = fixture.maxConeLength;
            frame[offset + AlpsShowEvaluator.FrameGobo] = fixture.selectGOBO;
        }

        /// <summary>Writes a model frame to a VRSL fixture.</summary>
        public static void ApplyVRSL(VRStageLighting_DMX_Static fixture, float[] frame, int offset, MaterialPropertyBlock block)
        {
            ApplyVRSLFields(fixture, frame, offset);
            ApplyVRSLGoboRotation(fixture, frame[offset + AlpsShowEvaluator.FrameGoboRotation], block);
        }

        /// <summary>
        /// Writes everything but the gobo angle to a VRSL fixture and lets VRSL rebuild its
        /// property block, which drops the gobo angle, so <see cref="ApplyVRSLGoboRotation"/>
        /// has to follow.
        /// </summary>
        public static void ApplyVRSLFields(VRStageLighting_DMX_Static fixture, float[] frame, int offset)
        {
            var brightness = frame[offset + AlpsShowEvaluator.FrameBrightness] * frame[offset + AlpsShowEvaluator.FrameBrightnessScale];

            fixture.enableDMXChannels = false;
            fixture.enableStrobe = false;
            fixture.enableAutoSpin = false;
            fixture.panOffsetBlueGreen = -frame[offset + AlpsShowEvaluator.FramePan];
            fixture.tiltOffsetBlue = frame[offset + AlpsShowEvaluator.FrameTilt] + VrslDefaultTiltOffset;
            // White at 100% is a tint of 1. VRSL caps global intensity at 1, so brightness
            // above 100% scales the tint instead, reaching VRSL's default white of 2 at 200%.
            var intensity = Mathf.Max(0f, brightness / 100f);
            var tintScale = intensity > 1f ? intensity : 1f;
            fixture.globalIntensity = Mathf.Min(intensity, 1f);
            fixture.lightColorTint = new Color(
                frame[offset + AlpsShowEvaluator.FrameRed] * tintScale,
                frame[offset + AlpsShowEvaluator.FrameGreen] * tintScale,
                frame[offset + AlpsShowEvaluator.FrameBlue] * tintScale,
                1f);
            // A width typed past the limit keeps opening at the same rate.
            var coneWidth = Mathf.Max(0f, frame[offset + AlpsShowEvaluator.FrameConeWidth]) / ModelConeWidthLimit;
            fixture.coneWidth = VrslMinConeWidth + coneWidth * (VrslMaxConeWidth - VrslMinConeWidth);
            // Up to the limit the cone fades in along the fixture's own mesh. Past it the mesh
            // itself is stretched, which VRSL scales linearly from the fixture.
            var coneLength = Mathf.Max(0f, frame[offset + AlpsShowEvaluator.FrameConeLength]) / ModelConeLengthLimit;
            fixture.coneLength = Mathf.Lerp(VrslMinConeLength, VrslMaxConeLength, Mathf.Clamp01(coneLength));
            fixture.maxConeLength = frame[offset + AlpsShowEvaluator.FrameConeMeshLength] * Mathf.Max(1f, coneLength);
            fixture.selectGOBO = Mathf.Clamp(AlpsShowEvaluator.ToInt(frame[offset + AlpsShowEvaluator.FrameGobo]), 1, 8);
            fixture._UpdateInstancedProperties();
        }

        /// <summary>
        /// Adds the gobo angle to each renderer's property block. VRSL replaces the whole block
        /// whenever it updates, so this runs after it. Stock VRSL shaders ignore the property,
        /// the patched ones rotate the gobo.
        /// </summary>
        public static void ApplyVRSLGoboRotation(VRStageLighting_DMX_Static fixture, float rotation, MaterialPropertyBlock block)
        {
            var renderers = fixture.objRenderers;
            if (renderers == null || block == null)
            {
                return;
            }

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
