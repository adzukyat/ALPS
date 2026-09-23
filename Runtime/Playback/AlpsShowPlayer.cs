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
    /// Going through VRSL costs far more than evaluating: every field is a write into another
    /// behaviour, and VRSL then rebuilds its whole property block. So a fixture is written
    /// through VRSL only the first time and once in a while after, one fixture per frame in
    /// turn, which also repairs anything that rebuilt a block behind the player's back. In
    /// between, only the shader properties of the channels that changed are written straight
    /// into the renderers' property blocks, and nothing when no channel changed.
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

        // Channel bits of a change mask from FrameChange, grouped by the shader properties they feed.
        private const int ChangedPan = 1 << AlpsShowEvaluator.FramePan;
        private const int ChangedTilt = 1 << AlpsShowEvaluator.FrameTilt;
        private const int ChangedLight =
            (1 << AlpsShowEvaluator.FrameBrightness) | (1 << AlpsShowEvaluator.FrameBrightnessScale) |
            (1 << AlpsShowEvaluator.FrameRed) | (1 << AlpsShowEvaluator.FrameGreen) | (1 << AlpsShowEvaluator.FrameBlue);
        private const int ChangedConeWidth = 1 << AlpsShowEvaluator.FrameConeWidth;
        private const int ChangedConeLength = (1 << AlpsShowEvaluator.FrameConeLength) | (1 << AlpsShowEvaluator.FrameConeMeshLength);
        private const int ChangedGobo = 1 << AlpsShowEvaluator.FrameGobo;
        private const int ChangedGoboRotation = 1 << AlpsShowEvaluator.FrameGoboRotation;

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

        /// <summary>
        /// Experimental: evaluate the show on the GPU and hand VRSL a DMX grid instead of
        /// writing every fixture from Udon. Tracking a user is not supported there yet.
        /// </summary>
        public bool gpu;
        public Material gpuFramesMaterial;
        public Material gpuGridMaterial;
        public RenderTexture gpuFrames;
        public RenderTexture gpuGrid;
        public RenderTexture gpuSpin;

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
        private MeshRenderer[] _renderers;
        private int[] _rendererStart;
        private int _idPan;
        private int _idTilt;
        private int _idIntensity;
        private int _idEmission;
        private int _idEmissionDmx;
        private int _idConeWidth;
        private int _idConeLength;
        private int _idMaxConeLength;
        private int _idGobo;
        private int _idGoboRotation;
        private float[] _aimPan;
        private float[] _aimTilt;
        private bool[] _aiming;
        private VRCPlayerApi[] _players;
        private MaterialPropertyBlock _block;
        private bool _initialized;
        private float _lastTime = -1f;
        private bool _anyAiming;
        private Texture2D _gpuData;
        private int _idTime;

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

            // Every fixture's renderers in one list, read from VRSL once.
            _rendererStart = new int[count + 1];
            var rendererCount = 0;
            for (var i = 0; i < count; i++)
            {
                var offset = i * AlpsShowEvaluator.FrameStride;
                _rendererStart[i] = rendererCount;
                if (IsVRSL(i))
                {
                    CaptureVRSL(vrslFixtures[i], _defaults, offset);
                    var renderers = vrslFixtures[i].objRenderers;
                    rendererCount += renderers != null ? renderers.Length : 0;
                }
                else
                {
                    WriteNeutralFrame(_defaults, offset);
                }
            }

            _rendererStart[count] = rendererCount;
            _renderers = new MeshRenderer[rendererCount];
            for (var i = 0; i < count; i++)
            {
                if (!IsVRSL(i))
                {
                    continue;
                }

                var renderers = vrslFixtures[i].objRenderers;
                var start = _rendererStart[i];
                for (var r = 0; r < _rendererStart[i + 1] - start; r++)
                {
                    _renderers[start + r] = renderers[r];
                }
            }

            _idPan = VRCShader.PropertyToID("_FixtureBaseRotationY");
            _idTilt = VRCShader.PropertyToID("_FixtureRotationX");
            _idIntensity = VRCShader.PropertyToID("_GlobalIntensity");
            _idEmission = VRCShader.PropertyToID("_Emission");
            _idEmissionDmx = VRCShader.PropertyToID("_EmissionDMX");
            _idConeWidth = VRCShader.PropertyToID("_ConeWidth");
            _idConeLength = VRCShader.PropertyToID("_ConeLength");
            _idMaxConeLength = VRCShader.PropertyToID("_MaxConeLength");
            _idGobo = VRCShader.PropertyToID("_ProjectionSelection");
            _idGoboRotation = VRCShader.PropertyToID(GoboRotationProperty);

            if (gpu)
            {
                InitializeGpu();
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

            if (gpu)
            {
                _lastTime = time;
                RenderGpu(time);
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

                if (!IsVRSL(fixture))
                {
                    continue;
                }

                var changed = FrameChange(_frame, 0, _applied, fixture * AlpsShowEvaluator.FrameStride);
                if (!_hasApplied[fixture] || fixture == refresh)
                {
                    _hasApplied[fixture] = true;
                    ApplyVRSL(vrslFixtures[fixture], _frame, 0, _block);
                }
                else if (changed != 0)
                {
                    WriteChanges(fixture, changed);
                }
            }
        }

        // ==================================================================================
        // GPU playback
        // ==================================================================================

        /// <summary>Width of the show data texture the GPU evaluator reads.</summary>
        public const int GpuDataWidth = 1024;

        /// <summary>Floats before the first section of the show data, see AlpsEvaluator.hlsl.</summary>
        public const int GpuHeaderSize = 32;

        /// <summary>Floats per fixture in the fixture info section.</summary>
        public const int GpuFixtureInfoStride = 4;

        /// <summary>Most fixtures VRSL's 26 x 240 grid holds, one 13 channel row each within three universes.</summary>
        public const int GpuMaxFixtures = 118;

        /// <summary>RGBA texels per fixture row of the frames target, room for the 13 channels.</summary>
        public const int GpuFrameTexels = 4;

        /// <summary>Size of VRSL's horizontal DMX grid, which its shaders' texel maths expects.</summary>
        public const int GpuGridWidth = 26;
        public const int GpuGridHeight = 240;

        /// <summary>
        /// Puts the fixtures in DMX mode on their own rows, makes the show data texture and
        /// hands VRSL the grid textures.
        /// </summary>
        private void InitializeGpu()
        {
            var count = fixtureAdapter.Length;
            var info = new float[count * GpuFixtureInfoStride];
            for (var i = 0; i < count; i++)
            {
                if (IsVRSL(i))
                {
                    CaptureVRSLDmxInfo(vrslFixtures[i], info, i * GpuFixtureInfoStride);
                    ConfigureVRSLDmx(vrslFixtures[i], i, _defaults, i * AlpsShowEvaluator.FrameStride);
                }
                else
                {
                    WriteNeutralDmxInfo(info, i * GpuFixtureInfoStride);
                }
            }

            var data = PackShowData(
                clips, effects, parameters, colors, gobos, positions,
                bucketStart, bucketClips, bucketSeconds, groupCount, groupIndex,
                count, _defaults, info);
            _gpuData = CreateShowTexture(data);
            _idTime = VRCShader.PropertyToID("_AlpsTime");
            BindGpu(_gpuData, gpuFramesMaterial, gpuGridMaterial, gpuFrames, gpuGrid, gpuSpin);
        }

        private void RenderGpu(float time)
        {
            gpuFramesMaterial.SetFloat(_idTime, time);
            RenderGpuPasses(_gpuData, gpuFramesMaterial, gpuGridMaterial, gpuFrames, gpuGrid, gpuSpin);
        }

        /// <summary>Points the materials at the show data and VRSL's DMX textures at the grids.</summary>
        public static void BindGpu(Texture2D data, Material framesMaterial, Material gridMaterial, RenderTexture frames, RenderTexture grid, RenderTexture spin)
        {
            framesMaterial.SetTexture("_AlpsData", data);
            framesMaterial.SetFloat("_AlpsFrameRows", frames.height);
            gridMaterial.SetTexture("_AlpsData", data);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_DMXGridRenderTexture"), grid);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_DMXGridRenderTextureMovement"), grid);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_DMXGridStrobeOutput"), grid);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_DMXGridSpinTimer"), spin);
        }

        /// <summary>Evaluates every fixture into the frames, then lays them out as the grids.</summary>
        public static void RenderGpuPasses(Texture2D data, Material framesMaterial, Material gridMaterial, RenderTexture frames, RenderTexture grid, RenderTexture spin)
        {
            VRCGraphics.Blit(data, frames, framesMaterial, 0);
            VRCGraphics.Blit(frames, grid, gridMaterial, 0);
            VRCGraphics.Blit(frames, spin, gridMaterial, 1);
        }

        /// <summary>
        /// Lays the compiled show out as the floats of the GPU evaluator's data texture: a
        /// header of section offsets, then every array in turn, then the fixture defaults and
        /// the per fixture DMX info. The length is a whole number of texture rows.
        /// </summary>
        public static float[] PackShowData(
            float[] clips,
            float[] effects,
            float[] parameters,
            float[] colors,
            float[] gobos,
            int[] positions,
            int[] bucketStart,
            int[] bucketClips,
            float bucketSeconds,
            int[] groupCount,
            int[] groupIndex,
            int fixtureCount,
            float[] defaults,
            float[] fixtureInfo)
        {
            var clipsAt = GpuHeaderSize;
            var effectsAt = clipsAt + clips.Length;
            var parametersAt = effectsAt + effects.Length;
            var colorsAt = parametersAt + parameters.Length;
            var gobosAt = colorsAt + colors.Length;
            var positionsAt = gobosAt + gobos.Length;
            var bucketStartAt = positionsAt + positions.Length;
            var bucketClipsAt = bucketStartAt + bucketStart.Length;
            var groupCountAt = bucketClipsAt + bucketClips.Length;
            var groupIndexAt = groupCountAt + groupCount.Length;
            var defaultsAt = groupIndexAt + groupIndex.Length;
            var fixtureInfoAt = defaultsAt + defaults.Length;
            var size = fixtureInfoAt + fixtureInfo.Length;
            var rows = (size + GpuDataWidth - 1) / GpuDataWidth;
            var data = new float[rows * GpuDataWidth];

            data[0] = 1f;
            data[1] = clipsAt;
            data[2] = clips.Length / AlpsShowEvaluator.ClipStride;
            data[3] = effectsAt;
            data[4] = parametersAt;
            data[5] = colorsAt;
            data[6] = gobosAt;
            data[7] = positionsAt;
            data[8] = bucketStartAt;
            data[9] = Mathf.Max(0, bucketStart.Length - 1);
            data[10] = bucketClipsAt;
            data[11] = bucketSeconds;
            data[12] = groupCountAt;
            data[13] = groupCount.Length;
            data[14] = groupIndexAt;
            data[15] = fixtureCount;
            data[16] = defaultsAt;
            data[17] = fixtureInfoAt;
            data[18] = groupCount.Length > 0 ? groupIndex.Length / groupCount.Length : 0;

            CopyFloats(clips, data, clipsAt);
            CopyFloats(effects, data, effectsAt);
            CopyFloats(parameters, data, parametersAt);
            CopyFloats(colors, data, colorsAt);
            CopyFloats(gobos, data, gobosAt);
            CopyInts(positions, data, positionsAt);
            CopyInts(bucketStart, data, bucketStartAt);
            CopyInts(bucketClips, data, bucketClipsAt);
            CopyInts(groupCount, data, groupCountAt);
            CopyInts(groupIndex, data, groupIndexAt);
            CopyFloats(defaults, data, defaultsAt);
            CopyFloats(fixtureInfo, data, fixtureInfoAt);
            return data;
        }

        private static void CopyFloats(float[] source, float[] target, int at)
        {
            for (var i = 0; i < source.Length; i++)
            {
                target[at + i] = source[i];
            }
        }

        private static void CopyInts(int[] source, float[] target, int at)
        {
            for (var i = 0; i < source.Length; i++)
            {
                target[at + i] = source[i];
            }
        }

        /// <summary>A linear single float texture holding <paramref name="data"/> row by row.</summary>
        public static Texture2D CreateShowTexture(float[] data)
        {
            var rows = data.Length / GpuDataWidth;
            var texture = new Texture2D(GpuDataWidth, Mathf.Max(1, rows), TextureFormat.RFloat, false, true);
            var pixels = new Color[texture.width * texture.height];
            for (var i = 0; i < data.Length; i++)
            {
                pixels[i] = new Color(data[i], 0f, 0f, 0f);
            }

            texture.SetPixels(pixels);
            texture.Apply(false);
            return texture;
        }

        /// <summary>
        /// What the DMX grid needs to know about a fixture: its pan and tilt ranges, which way
        /// its gobo turns, and the DMX step between gobos.
        /// </summary>
        public static void CaptureVRSLDmxInfo(VRStageLighting_DMX_Static fixture, float[] info, int offset)
        {
            info[offset] = fixture.maxMinPan / 2f;
            info[offset + 1] = fixture.maxMinTilt / 2f;
            info[offset + 2] = fixture.invertPan ? -1f : 1f;
            info[offset + 3] = fixture.legacyGoboRange ? 42.5f : 30f;
        }

        public static void WriteNeutralDmxInfo(float[] info, int offset)
        {
            info[offset] = 90f;
            info[offset + 1] = -90f;
            info[offset + 2] = 1f;
            info[offset + 3] = 30f;
        }

        /// <summary>
        /// Switches a VRSL fixture to DMX mode on the grid row of <paramref name="index"/>.
        /// Pan and tilt come from DMX on top of a base of 0 and 90, and colour and brightness
        /// from the DMX colour over a white tint. The cone length has no DMX channel, so it
        /// keeps the fixture's default.
        /// </summary>
        public static void ConfigureVRSLDmx(VRStageLighting_DMX_Static fixture, int index, float[] defaults, int offset)
        {
            var channel = 13 * index + 1;
            var universe = (channel - 1) / 520 + 1;
            fixture.enableDMXChannels = true;
            fixture.useLegacySectorMode = false;
            fixture.nineUniverseMode = false;
            fixture.singleChannelMode = false;
            fixture.enableFineChannels = false;
            fixture.enableStrobe = false;
            fixture.enableAutoSpin = true;
            fixture.dmxUniverse = universe;
            fixture.dmxChannel = channel - (universe - 1) * 520;
            fixture.panOffsetBlueGreen = 0f;
            fixture.tiltOffsetBlue = VrslDefaultTiltOffset;
            fixture.globalIntensity = 1f;
            fixture.lightColorTint = Color.white;
            var share = VrslConeLengthShare(defaults[offset + AlpsShowEvaluator.FrameConeLength]);
            fixture.coneLength = VrslConeLengthOf(share);
            fixture.maxConeLength = VrslMeshLength(defaults[offset + AlpsShowEvaluator.FrameConeMeshLength], share);
            fixture._UpdateInstancedProperties();
        }

        private bool IsVRSL(int fixture)
        {
            return fixtureAdapter[fixture] == AdapterVRSLDmxStatic && fixture < vrslFixtures.Length && vrslFixtures[fixture] != null;
        }

        /// <summary>
        /// Compares a frame with the one last written to a fixture, keeps it as the new last
        /// written one, and returns a mask with bit <c>1 &lt;&lt; channel</c> set for every
        /// channel that changed.
        /// </summary>
        public static int FrameChange(float[] frame, int offset, float[] applied, int appliedOffset)
        {
            var changed = 0;
            for (var ch = 0; ch < AlpsShowEvaluator.FrameStride; ch++)
            {
                var value = frame[offset + ch];
                if (applied[appliedOffset + ch] != value)
                {
                    applied[appliedOffset + ch] = value;
                    changed |= 1 << ch;
                }
            }

            return changed;
        }

        /// <summary>
        /// Writes the shader properties of the <paramref name="changed"/> channels straight
        /// into each renderer's property block, with the values <see cref="ApplyVRSL"/> would
        /// have VRSL pass on. Everything else VRSL put in the block stays as it was.
        /// </summary>
        private void WriteChanges(int fixture, int changed)
        {
            // Decided once here, since each test is an extern call in Udon and the loop runs per renderer.
            var pan = (changed & ChangedPan) != 0;
            var tilt = (changed & ChangedTilt) != 0;
            var light = (changed & ChangedLight) != 0;
            var coneWidth = (changed & ChangedConeWidth) != 0;
            var coneLengths = (changed & ChangedConeLength) != 0;
            var gobo = (changed & ChangedGobo) != 0;
            var goboRotation = (changed & ChangedGoboRotation) != 0;

            var frame = _frame;
            var panValue = pan ? VrslPan(frame[AlpsShowEvaluator.FramePan]) : 0f;
            var tiltValue = tilt ? VrslTilt(frame[AlpsShowEvaluator.FrameTilt]) : 0f;
            var coneWidthValue = coneWidth ? VrslConeWidth(frame[AlpsShowEvaluator.FrameConeWidth]) : 0f;
            var goboValue = gobo ? VrslGobo(frame[AlpsShowEvaluator.FrameGobo]) : 0;
            var goboRotationValue = frame[AlpsShowEvaluator.FrameGoboRotation];
            var intensity = 0f;
            var tint = Color.black;
            if (light)
            {
                var level = VrslLevel(frame, 0);
                intensity = Mathf.Min(level, 1f);
                tint = VrslTint(frame, 0, level);
            }

            var coneLength = 0f;
            var maxConeLength = 0f;
            if (coneLengths)
            {
                var share = VrslConeLengthShare(frame[AlpsShowEvaluator.FrameConeLength]);
                // VRSL hands its shaders the distance from 10.5 rather than the length itself.
                coneLength = Mathf.Abs(VrslConeLengthOf(share) - 10.5f);
                maxConeLength = VrslMeshLength(frame[AlpsShowEvaluator.FrameConeMeshLength], share);
            }

            var end = _rendererStart[fixture + 1];
            for (var r = _rendererStart[fixture]; r < end; r++)
            {
                var renderer = _renderers[r];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_block);
                if (pan)
                {
                    _block.SetFloat(_idPan, panValue);
                }

                if (tilt)
                {
                    _block.SetFloat(_idTilt, tiltValue);
                }

                if (light)
                {
                    _block.SetFloat(_idIntensity, intensity);
                    _block.SetColor(_idEmission, tint);
                    _block.SetColor(_idEmissionDmx, tint);
                }

                if (coneWidth)
                {
                    _block.SetFloat(_idConeWidth, coneWidthValue);
                }

                if (coneLengths)
                {
                    _block.SetFloat(_idConeLength, coneLength);
                    _block.SetFloat(_idMaxConeLength, maxConeLength);
                }

                if (gobo)
                {
                    _block.SetFloat(_idGobo, goboValue);
                }

                if (goboRotation)
                {
                    _block.SetFloat(_idGoboRotation, goboRotationValue);
                }

                renderer.SetPropertyBlock(_block);
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

        /// <summary>Shader property the patched VRSL shaders turn the gobo by.</summary>
        public const string GoboRotationProperty = "_GoboRotation";

        public static float VrslPan(float pan)
        {
            return -pan;
        }

        public static float VrslTilt(float tilt)
        {
            return tilt + VrslDefaultTiltOffset;
        }

        /// <summary>Brightness times flicker as a share of 100%, not capped.</summary>
        public static float VrslLevel(float[] frame, int offset)
        {
            return Mathf.Max(0f, frame[offset + AlpsShowEvaluator.FrameBrightness] * frame[offset + AlpsShowEvaluator.FrameBrightnessScale] / 100f);
        }

        /// <summary>
        /// White at 100% is a tint of 1. VRSL caps global intensity at 1, so a
        /// <paramref name="level"/> above 1 scales the tint instead, reaching VRSL's default
        /// white of 2 at 200%.
        /// </summary>
        public static Color VrslTint(float[] frame, int offset, float level)
        {
            var scale = level > 1f ? level : 1f;
            return new Color(
                frame[offset + AlpsShowEvaluator.FrameRed] * scale,
                frame[offset + AlpsShowEvaluator.FrameGreen] * scale,
                frame[offset + AlpsShowEvaluator.FrameBlue] * scale,
                1f);
        }

        /// <summary>A width typed past the limit keeps opening at the same rate.</summary>
        public static float VrslConeWidth(float width)
        {
            return VrslMinConeWidth + Mathf.Max(0f, width) / ModelConeWidthLimit * (VrslMaxConeWidth - VrslMinConeWidth);
        }

        /// <summary>A model cone length as a share of <see cref="ModelConeLengthLimit"/>.</summary>
        public static float VrslConeLengthShare(float length)
        {
            return Mathf.Max(0f, length) / ModelConeLengthLimit;
        }

        /// <summary>Up to the limit the cone fades in along the fixture's own mesh.</summary>
        public static float VrslConeLengthOf(float share)
        {
            return Mathf.Lerp(VrslMinConeLength, VrslMaxConeLength, Mathf.Clamp01(share));
        }

        /// <summary>Past the limit the mesh itself is stretched, which VRSL scales linearly from the fixture.</summary>
        public static float VrslMeshLength(float meshLength, float share)
        {
            return meshLength * Mathf.Max(1f, share);
        }

        public static int VrslGobo(float gobo)
        {
            return Mathf.Clamp(AlpsShowEvaluator.ToInt(gobo), 1, 8);
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
            var level = VrslLevel(frame, offset);
            var coneLength = VrslConeLengthShare(frame[offset + AlpsShowEvaluator.FrameConeLength]);

            fixture.enableDMXChannels = false;
            fixture.enableStrobe = false;
            fixture.enableAutoSpin = false;
            fixture.panOffsetBlueGreen = VrslPan(frame[offset + AlpsShowEvaluator.FramePan]);
            fixture.tiltOffsetBlue = VrslTilt(frame[offset + AlpsShowEvaluator.FrameTilt]);
            fixture.globalIntensity = Mathf.Min(level, 1f);
            fixture.lightColorTint = VrslTint(frame, offset, level);
            fixture.coneWidth = VrslConeWidth(frame[offset + AlpsShowEvaluator.FrameConeWidth]);
            fixture.coneLength = VrslConeLengthOf(coneLength);
            fixture.maxConeLength = VrslMeshLength(frame[offset + AlpsShowEvaluator.FrameConeMeshLength], coneLength);
            fixture.selectGOBO = VrslGobo(frame[offset + AlpsShowEvaluator.FrameGobo]);
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
                block.SetFloat(GoboRotationProperty, rotation);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
