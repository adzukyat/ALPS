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
    /// Plays a compiled show in VRChat. It follows <see cref="PlayableDirector.time"/> and
    /// has the GPU evaluate the show (<c>Runtime/Gpu</c>) into the DMX grid VRSL's shaders read,
    /// so Udon only sets the time and blits three times a frame. The arrays are filled at
    /// build time by the scene processor.
    ///
    /// Every show of a scene shares the grid, one DMX row per fixture across all of them.
    /// A player draws only when its time moves, or every frame while the show tracks a user,
    /// so the show that plays is the one on the grid.
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

        /// <summary>Model cone width at which VRSL reaches the widest cone its inspector offers.</summary>
        public const float ModelConeWidthLimit = 90f;
        /// <summary>
        /// Model cone length at which the cone is its fixture's own mesh length. The mesh
        /// scales in proportion, so 25 is half the mesh and 100 twice it.
        /// </summary>
        public const float ModelConeLengthLimit = 50f;
        /// <summary>
        /// VRSL cone width at a model width of 0. VRSL's inspector stops at 0, but its shader
        /// reaches -0.5 when driven by DMX.
        /// </summary>
        public const float VrslMinConeWidth = -0.5f;
        public const float VrslMaxConeWidth = 5.5f;
        public const float VrslMinConeLength = 0.5f;
        public const float VrslMaxConeLength = 10f;
        public const float VrslDefaultTiltOffset = 90f;

        /// <summary>The material property that turns on VRSL's cone length via DMX.</summary>
        public const string ConeLengthViaDmxProperty = "_EnableExtraChannels";

        /// <summary>Width of the show data texture the GPU evaluator reads.</summary>
        public const int GpuDataWidth = 1024;

        /// <summary>Floats before the first section of the show data, see AlpsEvaluator.hlsl.</summary>
        public const int GpuHeaderSize = 32;

        /// <summary>Floats per fixture in the fixture info section.</summary>
        public const int GpuFixtureInfoStride = 4;

        /// <summary>Floats per fixture in the aim section, the first three rows of its world to aim matrix.</summary>
        public const int GpuAimStride = 12;

        /// <summary>Most fixtures VRSL's 26 x 240 grid holds, one 13 channel row each within three universes.</summary>
        public const int GpuMaxFixtures = 118;

        /// <summary>RGBA texels per fixture row of the frames target, room for the 13 channels and the aim flag.</summary>
        public const int GpuFrameTexels = 4;

        /// <summary>Size of VRSL's horizontal DMX grid, which its shaders' texel maths expects.</summary>
        public const int GpuGridWidth = 26;
        public const int GpuGridHeight = 240;

        /// <summary>Most users one show can track, one shader vector each.</summary>
        public const int GpuMaxTrackedUsers = 8;

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

        /// <summary>The DMX grid row of every fixture, handed out across every show of the scene.</summary>
        public int[] dmxRows = new int[0];

        public Material framesMaterial;
        public Material gridMaterial;

        /// <summary>Two frames targets taken in turn, so a tracking head can follow on from the last frame.</summary>
        public RenderTexture frames;
        public RenderTexture framesPrevious;
        public RenderTexture grid;
        public RenderTexture spin;

        private Texture2D _data;
        private bool _initialized;
        private bool _flip;
        private float _lastTime = -1f;
        private float _lastRenderTime = -1f;
        private bool _tracks;
        private bool _playersDirty = true;
        private VRCPlayerApi[] _trackedPlayers;
        private VRCPlayerApi[] _players;
        private int[] _idTargets;
        private int _idData;
        private int _idPrevFrames;
        private int _idTime;
        private int _idDeltaTime;
        private int _idFrameRows;

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

        /// <summary>
        /// Captures every fixture's default state, puts the fixtures in DMX mode on their rows,
        /// makes the show data texture and hands VRSL the grid textures.
        /// </summary>
        public void Initialize()
        {
            var count = fixtureAdapter.Length;
            var defaults = new float[count * AlpsShowLayout.FrameStride];
            var info = new float[count * GpuFixtureInfoStride];
            var aim = new float[count * GpuAimStride];
            for (var i = 0; i < count; i++)
            {
                var offset = i * AlpsShowLayout.FrameStride;
                var row = i < dmxRows.Length ? dmxRows[i] : i;
                if (IsVRSL(i))
                {
                    CaptureVRSL(vrslFixtures[i], defaults, offset);
                    CaptureVRSLDmxInfo(vrslFixtures[i], info, i * GpuFixtureInfoStride);
                    ConfigureVRSLDmx(vrslFixtures[i], row, defaults, offset);
                }
                else
                {
                    WriteNeutralFrame(defaults, offset);
                    WriteNeutralDmxInfo(info, i * GpuFixtureInfoStride);
                }

                if (i < aimTransforms.Length && aimTransforms[i] != null)
                {
                    WriteAim(aimTransforms[i].worldToLocalMatrix, aim, i * GpuAimStride);
                }
            }

            var data = PackShowData(
                clips, effects, parameters, colors, gobos, positions,
                bucketStart, bucketClips, bucketSeconds, groupCount, groupIndex,
                count, defaults, info, dmxRows, aim);
            _data = CreateShowTexture(data);

            _idData = VRCShader.PropertyToID("_AlpsData");
            _idPrevFrames = VRCShader.PropertyToID("_AlpsPrevFrames");
            _idTime = VRCShader.PropertyToID("_AlpsTime");
            _idDeltaTime = VRCShader.PropertyToID("_AlpsDeltaTime");
            _idFrameRows = VRCShader.PropertyToID("_AlpsFrameRows");
            _idTargets = new int[GpuMaxTrackedUsers];
            for (var u = 0; u < GpuMaxTrackedUsers; u++)
            {
                _idTargets[u] = VRCShader.PropertyToID("_AlpsTrackTarget" + u);
            }

            _tracks = userNames.Length > 0;
            _trackedPlayers = new VRCPlayerApi[userNames.Length];
            _players = new VRCPlayerApi[82];
            BindGrid(grid, spin);
            _initialized = true;
        }

        public void EvaluateAt(float time)
        {
            if (!_initialized)
            {
                Initialize();
            }

            // A paused timeline draws the same frame again, unless a light is following a user.
            if (time == _lastTime && !_tracks)
            {
                return;
            }

            _lastTime = time;
            var now = Time.time;
            var deltaTime = _lastRenderTime >= 0f ? Mathf.Max(0f, now - _lastRenderTime) : 0f;
            _lastRenderTime = now;
            if (_tracks)
            {
                UpdateTargets();
            }

            var target = _flip ? framesPrevious : frames;
            var previous = _flip ? frames : framesPrevious;
            _flip = !_flip;
            framesMaterial.SetTexture(_idData, _data);
            framesMaterial.SetTexture(_idPrevFrames, previous);
            framesMaterial.SetFloat(_idTime, time);
            framesMaterial.SetFloat(_idDeltaTime, deltaTime);
            framesMaterial.SetFloat(_idFrameRows, target.height);
            gridMaterial.SetTexture(_idData, _data);
            RenderPasses(_data, framesMaterial, gridMaterial, target, grid, spin);
        }

        /// <summary>The frames target the last evaluation drew, for tests and debugging.</summary>
        public RenderTexture GetCurrentFrames()
        {
            return _flip ? frames : framesPrevious;
        }

        /// <summary>Evaluates every fixture into the frames, then lays them out as the grids.</summary>
        public static void RenderPasses(Texture2D data, Material framesMaterial, Material gridMaterial, RenderTexture frames, RenderTexture grid, RenderTexture spin)
        {
            VRCGraphics.Blit(data, frames, framesMaterial, 0);
            VRCGraphics.Blit(frames, grid, gridMaterial, 0);
            VRCGraphics.Blit(frames, spin, gridMaterial, 1);
        }

        /// <summary>Points VRSL's DMX textures at the grids.</summary>
        public static void BindGrid(RenderTexture grid, RenderTexture spin)
        {
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_DMXGridRenderTexture"), grid);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_DMXGridRenderTextureMovement"), grid);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_DMXGridStrobeOutput"), grid);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_DMXGridSpinTimer"), spin);
        }

        // ==================================================================================
        // Tracking
        // ==================================================================================

#if UDONSHARP
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            _playersDirty = true;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            _playersDirty = true;
        }
#endif

        /// <summary>
        /// Hands the frames pass the hips of every tracked user present. An avatar without a
        /// hips bone, which reads as zero, gives its head instead.
        /// </summary>
        private void UpdateTargets()
        {
            if (_playersDirty)
            {
                _playersDirty = false;
                for (var u = 0; u < userNames.Length; u++)
                {
                    _trackedPlayers[u] = FindPlayer(userNames[u]);
                }
            }

            var users = Mathf.Min(userNames.Length, GpuMaxTrackedUsers);
            for (var u = 0; u < users; u++)
            {
                var player = _trackedPlayers[u];
                var target = Vector4.zero;
                if (Utilities.IsValid(player))
                {
                    var position = player.GetBonePosition(HumanBodyBones.Hips);
                    if (position == Vector3.zero)
                    {
                        position = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                    }

                    target = new Vector4(position.x, position.y, position.z, 1f);
                }

                framesMaterial.SetVector(_idTargets[u], target);
            }
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
        /// The first three rows of a world to aim space matrix, which the frames pass turns a
        /// user's hips into the space VRSL rotates the fixture head in with.
        /// </summary>
        public static void WriteAim(Matrix4x4 worldToAim, float[] aim, int offset)
        {
            aim[offset] = worldToAim.m00;
            aim[offset + 1] = worldToAim.m01;
            aim[offset + 2] = worldToAim.m02;
            aim[offset + 3] = worldToAim.m03;
            aim[offset + 4] = worldToAim.m10;
            aim[offset + 5] = worldToAim.m11;
            aim[offset + 6] = worldToAim.m12;
            aim[offset + 7] = worldToAim.m13;
            aim[offset + 8] = worldToAim.m20;
            aim[offset + 9] = worldToAim.m21;
            aim[offset + 10] = worldToAim.m22;
            aim[offset + 11] = worldToAim.m23;
        }

        // ==================================================================================
        // Show data
        // ==================================================================================

        /// <summary>
        /// Lays the compiled show out as the floats of the GPU evaluator's data texture: a
        /// header of section offsets, then every array in turn, then the fixture defaults,
        /// the per fixture DMX info, which fixture sits on each DMX row, and the aim spaces.
        /// The length is a whole number of texture rows.
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
            float[] fixtureInfo,
            int[] dmxRows,
            float[] aim)
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
            var rowFixturesAt = fixtureInfoAt + fixtureInfo.Length;
            var aimAt = rowFixturesAt + GpuMaxFixtures;
            var size = aimAt + aim.Length;
            var rows = (size + GpuDataWidth - 1) / GpuDataWidth;
            var data = new float[rows * GpuDataWidth];

            data[0] = 1f;
            data[1] = clipsAt;
            data[2] = clips.Length / AlpsShowLayout.ClipStride;
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
            data[19] = rowFixturesAt;
            data[20] = aimAt;

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
            CopyFloats(aim, data, aimAt);

            // Which of the show's fixtures sits on each DMX row, -1 for rows it does not drive.
            for (var row = 0; row < GpuMaxFixtures; row++)
            {
                data[rowFixturesAt + row] = -1f;
            }

            for (var i = 0; i < fixtureCount; i++)
            {
                var row = i < dmxRows.Length ? dmxRows[i] : i;
                if (row >= 0 && row < GpuMaxFixtures)
                {
                    data[rowFixturesAt + row] = i;
                }
            }

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

        private bool IsVRSL(int fixture)
        {
            return fixtureAdapter[fixture] == AdapterVRSLDmxStatic && fixture < vrslFixtures.Length && vrslFixtures[fixture] != null;
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
            for (var ch = 0; ch < AlpsShowLayout.FrameStride; ch++)
            {
                frame[offset + ch] = 0f;
            }

            frame[offset + AlpsShowLayout.FrameBrightness] = 100f;
            frame[offset + AlpsShowLayout.FrameRed] = 1f;
            frame[offset + AlpsShowLayout.FrameGreen] = 1f;
            frame[offset + AlpsShowLayout.FrameBlue] = 1f;
            frame[offset + AlpsShowLayout.FrameConeLength] = ModelConeLengthLimit;
            frame[offset + AlpsShowLayout.FrameGobo] = AlpsShowLayout.GoboOff;
            frame[offset + AlpsShowLayout.FrameBrightnessScale] = 1f;
            frame[offset + AlpsShowLayout.FrameConeMeshLength] = 1f;
        }

        /// <summary>Reads a VRSL fixture's fields into model units.</summary>
        public static void CaptureVRSL(VRStageLighting_DMX_Static fixture, float[] frame, int offset)
        {
            WriteNeutralFrame(frame, offset);
            frame[offset + AlpsShowLayout.FramePan] = -fixture.panOffsetBlueGreen;
            frame[offset + AlpsShowLayout.FrameTilt] = fixture.tiltOffsetBlue - VrslDefaultTiltOffset;
            // An HDR tint such as VRSL's default white of 2 becomes brightness above 100%
            // over an SDR color.
            var color = fixture.lightColorTint;
            var peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            var excess = peak > 1f ? peak : 1f;
            frame[offset + AlpsShowLayout.FrameBrightness] = fixture.globalIntensity * 100f * excess;
            frame[offset + AlpsShowLayout.FrameRed] = color.r / excess;
            frame[offset + AlpsShowLayout.FrameGreen] = color.g / excess;
            frame[offset + AlpsShowLayout.FrameBlue] = color.b / excess;
            frame[offset + AlpsShowLayout.FrameConeWidth] =
                Mathf.Max(0f, (fixture.coneWidth - VrslMinConeWidth) / (VrslMaxConeWidth - VrslMinConeWidth)) * ModelConeWidthLimit;
            frame[offset + AlpsShowLayout.FrameConeLength] =
                Mathf.InverseLerp(VrslMinConeLength, VrslMaxConeLength, fixture.coneLength) * ModelConeLengthLimit;
            frame[offset + AlpsShowLayout.FrameConeMeshLength] = fixture.maxConeLength;
            frame[offset + AlpsShowLayout.FrameGobo] = fixture.selectGOBO;
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
        /// Switches a VRSL fixture to DMX mode on grid row <paramref name="row"/>. Pan and tilt
        /// come from DMX on top of a base of 0 and 90, colour and brightness from the DMX
        /// colour over a white tint. The cone reaches as far as the mesh stretch that VRSL's
        /// cone length via DMX adds to the fixture's own mesh length, with the fade along the
        /// cone left fully open. That option is a material property, which the build turns on
        /// in a copy of the volumetric material and the preview in each renderer's block.
        /// </summary>
        public static void ConfigureVRSLDmx(VRStageLighting_DMX_Static fixture, int row, float[] defaults, int offset)
        {
            var channel = 13 * row + 1;
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
            fixture.coneLength = VrslMaxConeLength;
            fixture.maxConeLength = defaults[offset + AlpsShowLayout.FrameConeMeshLength];
            fixture._UpdateInstancedProperties();
        }
    }
}
