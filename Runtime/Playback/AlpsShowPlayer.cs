using UnityEngine;
using UnityEngine.Playables;
using VRC.SDKBase;
using AdzukiSoft.ALPS.Vrsl;

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
    [AddComponentMenu("ALPS/ALPS Show Player")]
    public class AlpsShowPlayer : UdonSharpBehaviour
#else
    [AddComponentMenu("ALPS/ALPS Show Player")]
    public class AlpsShowPlayer : MonoBehaviour
#endif
    {
        public const int AdapterNone = 0;
        public const int AdapterVRSLDmxStatic = 1;
        public const int AdapterPenlight = 2;

        /// <summary>A segment of a VRSL multi light bar, which shares the bar's rows with the other segments.</summary>
        public const int AdapterVRSLBarSegment = 3;

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

        /// <summary>
        /// The material property that turns on VRSL's cone length via DMX, which the volumetric
        /// materials of the fixtures ALPS comes with have on.
        /// </summary>
        public const string ConeLengthViaDmxProperty = "_EnableExtraChannels";

        /// <summary>Width of the show data texture the GPU evaluator reads.</summary>
        public const int GpuDataWidth = 1024;

        /// <summary>Floats before the first section of the show data, see AlpsEvaluator.hlsl.</summary>
        public const int GpuHeaderSize = 32;

        /// <summary>
        /// Floats per fixture in the fixture info section: the pan and tilt ranges, which way
        /// the gobo turns and the DMX step between gobos, then which channels the fixture
        /// reads (<see cref="InfoLayout"/>), then which segment of its bar a bar segment is.
        /// </summary>
        public const int GpuFixtureInfoStride = 8;
        public const int InfoPanRange = 0;
        public const int InfoTiltRange = 1;
        public const int InfoPanSign = 2;
        public const int InfoGoboStep = 3;
        public const int InfoLayout = 4;
        public const int InfoBarSegment = 5;

        /// <summary>The 13 channels VRSL's movers and static lights read.</summary>
        public const int DmxLayoutStandard = 0;

        /// <summary>
        /// The 12 channels VRSL's laser reads: tilt, pan, cone length, cone width, flatness,
        /// beam count, which way its beams go round, the colour, the intensity and the beam
        /// thickness. How far the beams have gone round it reads from the spin grid.
        /// </summary>
        public const int DmxLayoutLaser = 1;

        /// <summary>
        /// The 4 channels a segment of VRSL's multi light bar reads in its 48 channel mode:
        /// the dimmer and the colour, <see cref="InfoBarSegment"/> times four past the bar's
        /// first channel.
        /// </summary>
        public const int DmxLayoutBarSegment = 2;
        public const int BarSegmentChannels = 4;

        /// <summary>The channels of a row of the grid, the thirteenth of which only a multi light bar reads.</summary>
        public const int GpuRowChannels = 13;

        /// <summary>
        /// What the channel map multiplies a fixture by, leaving room below it for which of
        /// its channels a cell is.
        /// </summary>
        public const int GpuChannelFixtureStep = 16;

        /// <summary>The material property only VRSL's laser shader has, which tells a laser from the other fixtures.</summary>
        public const string LaserBeamsProperty = "_LaserCount";
        public const string LaserFlatnessProperty = "_ZConeFlatness";
        public const string LaserThicknessProperty = "_LaserThickness";
        public const string LaserTiltProperty = "_XRotation";
        public const string LaserPanProperty = "_YRotation";
        public const string LaserConeWidthProperty = "_VertexConeWidth";
        public const string LaserConeLengthProperty = "_VertexConeLength";

        /// <summary>
        /// The ends of the laser's cone width. At the lower one its beams run in one line, and
        /// each unit above it opens the cone by the same amount again.
        /// </summary>
        public const float LaserMinConeWidth = -3.75f;
        public const float LaserMaxConeWidth = 20f;

        /// <summary>
        /// The radius of the laser's own mesh at its far end over its length, which is how far
        /// the cone opens at a width of 0 and a length of 0. The model width of a laser is half
        /// its opening angle in degrees, whatever its length.
        /// </summary>
        public const float LaserMeshSlope = 0.511f;

        /// <summary>The shortest and the longest the laser's length channel makes its mesh, as shares of it.</summary>
        public const float LaserMinStretch = 0.5f;
        public const float LaserMaxStretch = 6f;

        /// <summary>The angle the laser's pan and tilt channels reach to either side.</summary>
        public const float LaserAngleRange = 90f;

        /// <summary>The laser's flatness at which its cone is a fan, 100% in the model.</summary>
        public const float LaserMaxFlatness = 1.999f;

        /// <summary>The fewest and the most beams the laser draws around its cone.</summary>
        public const float LaserMinBeams = 4f;
        public const float LaserMaxBeams = 68f;

        /// <summary>
        /// The thickest beam a laser's material holds, 100% in the model. It is twice what the
        /// laser's thickness channel reaches at 1 (<see cref="LaserDmxMaxThickness"/>), and the
        /// laser reads on past that, since the grid holds floats.
        /// </summary>
        public const float LaserMaxThickness = 0.1f;

        /// <summary>The thickness the laser's channel gives at 0 and at 1.</summary>
        public const float LaserDmxMinThickness = 0.0005f;
        public const float LaserDmxMaxThickness = 0.05f;

        /// <summary>Floats per fixture in the aim section, the first three rows of its world to aim matrix.</summary>
        public const int GpuAimStride = 12;

        /// <summary>
        /// The rows of 13 channels VRSL's 26 x 240 grid holds in its nine universe mode, which
        /// reads universes 1 to 3 from red, 4 to 6 from green and 7 to 9 from blue
        /// (<see cref="GpuDmxBandRows"/>). A fixture takes one, and a multi light bar four for
        /// its twelve segments, all in one band.
        /// </summary>
        public const int GpuMaxFixtures = 348;

        /// <summary>The colour bands of the grid, three universes each.</summary>
        public const int GpuDmxBands = 3;

        /// <summary>
        /// Rows a band spans. VRSL counts a universe as 520 channels, 40 rows, so band k starts
        /// on row 120k, absolute channel 1560k + 1. Not all of them can be read
        /// (<see cref="DmxBandRows"/>).
        /// </summary>
        public const int GpuDmxBandRows = 120;

        /// <summary>
        /// Rows from the first to past the last one VRSL reads. A row n reads absolute channel
        /// 13n + 1 on, and the rows past the end of a band stay empty.
        /// </summary>
        public const int GpuDmxRowSpan = 354;

        /// <summary>
        /// RGBA texels per fixture row of the frames target, room for the 17 channels, the aim
        /// flag, the show token and the held flag.
        /// </summary>
        public const int GpuFrameTexels = 5;

        /// <summary>Size of VRSL's horizontal DMX grid, which its shaders' texel maths expects.</summary>
        public const int GpuGridWidth = 26;
        public const int GpuGridHeight = 240;

        /// <summary>Most users one show can track, one shader vector each.</summary>
        public const int GpuMaxTrackedUsers = 8;

        /// <summary>Most penlight areas one show holds, one row of the penlight texture each.</summary>
        public const int GpuMaxPenlightAreas = 4;

        /// <summary>Most slots one penlight area is cut into, one texel each.</summary>
        public const int GpuMaxPenlightSlots = 64;

        /// <summary>
        /// Floats per area in the penlight section of the show data: <see cref="PenlightSlotCount"/>,
        /// <see cref="PenlightShape"/> and <see cref="PenlightSize"/>, the first three rows of
        /// its world to area matrix from <see cref="PenlightMatrix"/>, then the show's fixture
        /// on each slot from <see cref="PenlightSlots"/>.
        /// </summary>
        public const int GpuPenlightAreaStride = 16 + GpuMaxPenlightSlots;
        public const int PenlightSlotCount = 0;
        public const int PenlightShape = 1;
        public const int PenlightSize = 2;
        public const int PenlightMatrix = 4;
        public const int PenlightSlots = 16;

        /// <summary>
        /// Width of the penlight texture: a texel per slot, then the three matrix rows and the
        /// texel holding the slot count, the shape, the size and 1 for an area that is there.
        /// </summary>
        public const int GpuPenlightWidth = GpuMaxPenlightSlots + 4;

        /// <summary>The global the penlight texture goes by, which avatars read too.</summary>
        public const string PenlightTextureProperty = "_Udon_AlpsPenlight";

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

        /// <summary>Which segment of its bar a fixture is, for the ones that are bar segments.</summary>
        public int[] fixtureSegment = new int[0];
        public Transform[] aimTransforms = new Transform[0];

        /// <summary>
        /// The DMX grid row of every fixture. Rows are counted per show, so fixtures of other
        /// shows of the scene read the same rows. The segments of a bar all name the bar's
        /// first row.
        /// </summary>
        public int[] dmxRows = new int[0];

        /// <summary>
        /// The show's penlight areas, <see cref="GpuPenlightAreaStride"/> floats each. Their
        /// matrices are taken from <see cref="penlightTransforms"/> when the player starts.
        /// </summary>
        public float[] penlight = new float[0];
        public Transform[] penlightTransforms = new Transform[0];

        /// <summary>
        /// Tells this show's frames from the other shows' in the shared frames targets, so pan
        /// and tilt only carry on from a frame this show drew. Above 0, one per show of the scene.
        /// </summary>
        public int showToken;

        public Material framesMaterial;
        public Material gridMaterial;
        public Material penlightMaterial;

        /// <summary>Two frames targets taken in turn, so a head can carry on from the last frame.</summary>
        public RenderTexture frames;
        public RenderTexture framesPrevious;
        public RenderTexture grid;
        public RenderTexture spin;

        /// <summary>What the penlights read, shared by every show of the scene like the grid.</summary>
        public RenderTexture penlightTarget;

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
        private int _idShowToken;

        private void Start()
        {
            Initialize();
        }

        // Another show may have drawn on the shared rows while this one was off, so a show that
        // comes back draws again even when its time has not moved.
        private void OnEnable()
        {
            _lastTime = -1f;
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
                    if (fixtureAdapter[i] == AdapterVRSLBarSegment)
                    {
                        WriteBarSegmentDmxInfo(info, i * GpuFixtureInfoStride, i < fixtureSegment.Length ? fixtureSegment[i] : 0);
                    }
                    else
                    {
                        CaptureVRSLDmxInfo(vrslFixtures[i], info, i * GpuFixtureInfoStride);
                    }

                    ConfigureVRSLDmx(vrslFixtures[i], row, defaults[offset + AlpsShowLayout.FrameConeMeshLength]);
                }
                else
                {
                    WriteNeutralFrame(defaults, offset);
                    WriteNeutralDmxInfo(info, i * GpuFixtureInfoStride);
                }

                if (i < aimTransforms.Length && aimTransforms[i] != null)
                {
                    WriteAim(aimTransforms[i].worldToLocalMatrix, aim, i * GpuAimStride);
                    if (fixtureAdapter[i] == AdapterVRSLDmxStatic && IsVRSL(i))
                    {
                        OrientVRSLAim(vrslFixtures[i], aim, i * GpuAimStride);
                    }
                }
            }

            for (var a = 0; a < penlightTransforms.Length; a++)
            {
                var at = a * GpuPenlightAreaStride + PenlightMatrix;
                if (penlightTransforms[a] != null && at + GpuAimStride <= penlight.Length)
                {
                    WriteAim(penlightTransforms[a].worldToLocalMatrix, penlight, at);
                }
            }

            var data = PackShowData(
                clips, effects, parameters, colors, gobos, positions,
                bucketStart, bucketClips, bucketSeconds, groupCount, groupIndex,
                count, defaults, info, dmxRows, aim, penlight);
            _data = CreateShowTexture(data);

            _idData = VRCShader.PropertyToID("_AlpsData");
            _idPrevFrames = VRCShader.PropertyToID("_AlpsPrevFrames");
            _idTime = VRCShader.PropertyToID("_AlpsTime");
            _idDeltaTime = VRCShader.PropertyToID("_AlpsDeltaTime");
            _idFrameRows = VRCShader.PropertyToID("_AlpsFrameRows");
            _idShowToken = VRCShader.PropertyToID("_AlpsShowToken");
            _idTargets = new int[GpuMaxTrackedUsers];
            for (var u = 0; u < GpuMaxTrackedUsers; u++)
            {
                _idTargets[u] = VRCShader.PropertyToID("_AlpsTrackTarget" + u);
            }

            _tracks = userNames.Length > 0;
            _trackedPlayers = new VRCPlayerApi[userNames.Length];
            _players = new VRCPlayerApi[82];
            BindGrid(grid, spin);
            if (penlightTarget != null)
            {
                BindPenlight(penlightTarget);
            }

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
            framesMaterial.SetFloat(_idShowToken, showToken);
            gridMaterial.SetTexture(_idData, _data);
            RenderPasses(_data, framesMaterial, gridMaterial, target, grid, spin);
            if (penlightMaterial != null && penlightTarget != null)
            {
                penlightMaterial.SetTexture(_idData, _data);
                RenderPenlight(target, penlightMaterial, penlightTarget);
            }
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

        /// <summary>
        /// Lays the slots of the show's penlight areas out as the penlight texture, from the
        /// frames <see cref="RenderPasses"/> drew. A show without an area leaves it empty.
        /// </summary>
        public static void RenderPenlight(RenderTexture frames, Material penlightMaterial, RenderTexture penlight)
        {
            VRCGraphics.Blit(frames, penlight, penlightMaterial, 0);
        }

        /// <summary>Hands every shader of the world, the avatars' included, the penlight texture.</summary>
        public static void BindPenlight(RenderTexture penlight)
        {
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID(PenlightTextureProperty), penlight);
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

        /// <summary>
        /// Turns the mesh space of a VRSL head into the aim space the frames pass reads, where
        /// tilt 0 aims along +Z and pan 0 tilted forward along +Y. VRSL's beam rests along the
        /// mesh's -Y axis and its default tilt of 90 turns it onto +Z, which inverting the tilt
        /// mirrors to -Z, and inverting the pan mirrors X.
        /// </summary>
        public static void OrientVRSLAim(VRStageLighting_DMX_Static fixture, float[] aim, int offset)
        {
            // A laser's cone rests along its mesh's -Y axis and a positive tilt leans it
            // toward the mesh's +Z, so the mesh's Z row becomes Y and its Y row, negated, Z.
            if (GetVRSLLaserMaterial(fixture) != null)
            {
                for (var i = 0; i < 4; i++)
                {
                    var y = aim[offset + 4 + i];
                    aim[offset + 4 + i] = aim[offset + 8 + i];
                    aim[offset + 8 + i] = -y;
                }

                return;
            }

            if (fixture.invertPan)
            {
                for (var i = 0; i < 4; i++)
                {
                    aim[offset + i] = -aim[offset + i];
                }
            }

            if (!fixture.invertTilt)
            {
                for (var i = 8; i < 12; i++)
                {
                    aim[offset + i] = -aim[offset + i];
                }
            }
        }

        // ==================================================================================
        // Show data
        // ==================================================================================

        /// <summary>
        /// Lays the compiled show out as the floats of the GPU evaluator's data texture: a
        /// header of section offsets, then every array in turn, then the fixture defaults,
        /// the per fixture DMX info, which fixture reads each channel of the DMX grid, the aim
        /// spaces and the penlight areas. The length is a whole number of texture rows.
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
            float[] aim,
            float[] penlight)
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
            var channelFixturesAt = fixtureInfoAt + fixtureInfo.Length;
            var channels = GpuDmxRowSpan * GpuRowChannels;
            var aimAt = channelFixturesAt + channels;
            var penlightAt = aimAt + aim.Length;
            var size = penlightAt + penlight.Length;
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
            data[19] = channelFixturesAt;
            data[20] = aimAt;
            data[21] = penlightAt;
            data[22] = penlight.Length / GpuPenlightAreaStride;

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
            CopyFloats(penlight, data, penlightAt);

            // Which of the show's fixtures reads each channel of the grid and which of its
            // channels that is, as fixture * GpuChannelFixtureStep + channel, or -1 for the
            // channels the show does not drive. A fixture reads the first twelve of its row,
            // and a segment of a bar four of the channels that run on from the bar's first row.
            for (var channel = 0; channel < channels; channel++)
            {
                data[channelFixturesAt + channel] = -1f;
            }

            for (var i = 0; i < fixtureCount; i++)
            {
                var row = i < dmxRows.Length ? dmxRows[i] : i;
                if (!IsDmxRow(row))
                {
                    continue;
                }

                var at = i * GpuFixtureInfoStride;
                var isSegment = at + InfoBarSegment < fixtureInfo.Length && Mathf.RoundToInt(fixtureInfo[at + InfoLayout]) == DmxLayoutBarSegment;
                var first = row * GpuRowChannels + (isSegment ? Mathf.RoundToInt(fixtureInfo[at + InfoBarSegment]) * BarSegmentChannels : 0);
                var count = isSegment ? BarSegmentChannels : GpuRowChannels - 1;
                for (var channel = 0; channel < count; channel++)
                {
                    if (first + channel < channels)
                    {
                        data[channelFixturesAt + first + channel] = i * GpuChannelFixtureStep + channel;
                    }
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
            var adapter = fixtureAdapter[fixture];
            return (adapter == AdapterVRSLDmxStatic || adapter == AdapterVRSLBarSegment) && fixture < vrslFixtures.Length && vrslFixtures[fixture] != null;
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
            frame[offset + AlpsShowLayout.FrameLaserBeams] = LaserMinBeams;
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

            // A laser keeps its pose and its cone on the material, not on the script. Its tilt
            // is read the other way round, so a positive one raises the cone of a laser that
            // stands the way its prefab does.
            var laser = GetVRSLLaserMaterial(fixture);
            if (laser != null)
            {
                // The laser's length is what it adds to its own mesh, as a share of it.
                var stretch = Mathf.Clamp(1f + laser.GetFloat(LaserConeLengthProperty), LaserMinStretch, LaserMaxStretch);
                var opening = Mathf.Max(0f, laser.GetFloat(LaserConeWidthProperty) - LaserMinConeWidth) / -LaserMinConeWidth;
                frame[offset + AlpsShowLayout.FramePan] = laser.GetFloat(LaserPanProperty);
                frame[offset + AlpsShowLayout.FrameTilt] = -laser.GetFloat(LaserTiltProperty);
                frame[offset + AlpsShowLayout.FrameConeWidth] = Mathf.Atan(LaserMeshSlope * opening / stretch) * Mathf.Rad2Deg;
                frame[offset + AlpsShowLayout.FrameConeLength] = stretch * ModelConeLengthLimit;
                frame[offset + AlpsShowLayout.FrameConeMeshLength] = 1f;
                frame[offset + AlpsShowLayout.FrameGobo] = AlpsShowLayout.GoboOff;

                // How flat the cone is, how many beams it has and how thick they are, which a
                // laser no laser effect drives keeps. Its beams stand still.
                frame[offset + AlpsShowLayout.FrameLaserFlatness] = laser.GetFloat(LaserFlatnessProperty) / LaserMaxFlatness * 100f;
                frame[offset + AlpsShowLayout.FrameLaserBeams] = laser.GetFloat(LaserBeamsProperty);
                frame[offset + AlpsShowLayout.FrameLaserThickness] = laser.GetFloat(LaserThicknessProperty) / LaserMaxThickness * 100f;
            }
        }

        /// <summary>
        /// The material of a VRSL laser, or null for any other fixture. The laser reads
        /// channels of its own, so the grid lays its row out another way
        /// (<see cref="DmxLayoutLaser"/>).
        /// </summary>
        public static Material GetVRSLLaserMaterial(VRStageLighting_DMX_Static fixture)
        {
            if (fixture == null)
            {
                return null;
            }

            var renderers = fixture.objRenderers;
            if (renderers == null)
            {
                return null;
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var material = renderer.sharedMaterial;
                if (material != null && material.HasProperty(LaserBeamsProperty))
                {
                    return material;
                }
            }

            return null;
        }

        /// <summary>
        /// What the DMX grid needs to know about a fixture: its pan and tilt ranges, which way
        /// its gobo turns, and the DMX step between gobos, or that it is a laser.
        /// </summary>
        public static void CaptureVRSLDmxInfo(VRStageLighting_DMX_Static fixture, float[] info, int offset)
        {
            WriteNeutralDmxInfo(info, offset);
            var laser = GetVRSLLaserMaterial(fixture);
            if (laser != null)
            {
                info[offset + InfoPanRange] = LaserAngleRange;
                info[offset + InfoTiltRange] = LaserAngleRange;
                info[offset + InfoLayout] = DmxLayoutLaser;
                return;
            }

            info[offset + InfoPanRange] = fixture.maxMinPan / 2f;
            info[offset + InfoTiltRange] = fixture.maxMinTilt / 2f;
            info[offset + InfoPanSign] = fixture.invertPan ? -1f : 1f;
            info[offset + InfoGoboStep] = fixture.legacyGoboRange ? 42.5f : 30f;
        }

        /// <summary>What the DMX grid needs to know about a segment of a multi light bar: which one it is.</summary>
        public static void WriteBarSegmentDmxInfo(float[] info, int offset, int segment)
        {
            WriteNeutralDmxInfo(info, offset);
            info[offset + InfoLayout] = DmxLayoutBarSegment;
            info[offset + InfoBarSegment] = segment;
        }

        public static void WriteNeutralDmxInfo(float[] info, int offset)
        {
            info[offset + InfoPanRange] = 90f;
            info[offset + InfoTiltRange] = -90f;
            info[offset + InfoPanSign] = 1f;
            info[offset + InfoGoboStep] = 30f;
            info[offset + InfoLayout] = DmxLayoutStandard;
            info[offset + InfoBarSegment] = 0f;
        }

        /// <summary>
        /// Switches a VRSL fixture to DMX mode on grid row <paramref name="row"/>. Pan and tilt
        /// come from DMX on top of a base of 0 and 90, colour and brightness from the DMX
        /// colour over a white tint. The fine pan and tilt channels stay off, since the cone
        /// length rides on the fine pan channel: the cone reaches as far as the mesh stretch
        /// that VRSL's cone length via DMX adds to <paramref name="baseConeLength"/>, the
        /// fixture's own mesh length, with the fade along the cone left fully open. That
        /// option is a material property (<see cref="ConeLengthViaDmxProperty"/>). Every
        /// fixture reads in nine universe mode, which takes a channel from the colour of the
        /// grid its band is laid out in, since a fixture in the other mode reads all three.
        /// </summary>
        public static void ConfigureVRSLDmx(VRStageLighting_DMX_Static fixture, int row, float baseConeLength)
        {
            var channel = 13 * row + 1;
            var universe = (channel - 1) / 520 + 1;
            fixture.enableDMXChannels = true;
            fixture.useLegacySectorMode = false;
            fixture.nineUniverseMode = true;
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
            fixture.maxConeLength = baseConeLength;
            fixture._UpdateInstancedProperties();
        }

        /// <summary>
        /// The rows of band <paramref name="band"/> VRSL reads all of. Its getValueAtCoords
        /// takes a channel to a band by universes of 512 channels, so band k ends on absolute
        /// channel 1536(k + 1), and moves it back by 1560k to read the texel the same channel
        /// of band 0 is read from. That leaves the band its channels 1 to 1536 - 24k, and a
        /// row needs twelve of them, a bar's last row nine.
        /// </summary>
        public static int DmxBandRows(int band)
        {
            return (1536 - 24 * band - (GpuRowChannels - 1)) / GpuRowChannels + 1;
        }

        /// <summary>Whether VRSL reads all of row <paramref name="row"/>.</summary>
        public static bool IsDmxRow(int row)
        {
            if (row < 0 || row >= GpuDmxRowSpan)
            {
                return false;
            }

            return row % GpuDmxBandRows < DmxBandRows(row / GpuDmxBandRows);
        }

        /// <summary>
        /// The first row from <paramref name="row"/> on where <paramref name="size"/> rows in a
        /// row are all read and in one band, or <see cref="GpuDmxRowSpan"/> when none is left.
        /// </summary>
        public static int FitDmxRows(int row, int size)
        {
            if (row < 0)
            {
                row = 0;
            }

            while (row < GpuDmxRowSpan)
            {
                var band = row / GpuDmxBandRows;
                var last = row + size - 1;
                if (IsDmxRow(row) && IsDmxRow(last) && last / GpuDmxBandRows == band)
                {
                    return row;
                }

                // The rows a band reads come first, so nothing later in this band fits.
                row = (band + 1) * GpuDmxBandRows;
            }

            return GpuDmxRowSpan;
        }
    }
}
