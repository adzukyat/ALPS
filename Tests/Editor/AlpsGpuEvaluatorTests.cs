using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// The DMX grid against where VRSL reads it, the DMX rows shows share, and tracking a
    /// user. These need a graphics device, so they are ignored under -nographics.
    /// </summary>
    public class AlpsGpuEvaluatorTests
    {
        private const float Bpm = 60f;
        private const int Fixtures = 6;

        private readonly List<Object> _created = new List<Object>();
        private readonly List<AlpsGpuShow> _shows = new List<AlpsGpuShow>();

        [SetUp]
        public void RequireGraphics()
        {
            AlpsGpuShow.RequireGraphics();
        }

        [TearDown]
        public void DestroyCreated()
        {
            // A blit leaves its target active, and releasing the active target warns.
            RenderTexture.active = null;
            foreach (var created in _created)
            {
                Object.DestroyImmediate(created);
            }

            foreach (var show in _shows)
            {
                show.Dispose();
            }

            _created.Clear();
            _shows.Clear();
        }

        [Test]
        public void Tracking_AimsAtTheUserAndFollowsAtItsSpeed()
        {
            var set = Set();
            var move = set.Add(AlpsEffectKind.Move);
            move.moveMode = AlpsMoveMode.TrackUser;
            move.trackUserName = "Someone";
            move.trackSpeed = 2f;
            var show = AlpsShowCompiler.CompileStandalone(1, Bpm, new AlpsStandaloneClip { set = set, end = 10f });
            Assert.AreEqual(new[] { "Someone" }, show.userNames);

            // The fixture's aim space is the world turned by 90 degrees around Y.
            var aim = new float[AlpsShowPlayer.GpuAimStride];
            AlpsShowPlayer.WriteAim(Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one).inverse, aim, 0);
            var gpu = Keep(new AlpsGpuShow(show, 1, aim: aim));
            var previous = Keep(AlpsPreviewDriver.CreateGpuTarget("Previous", AlpsShowPlayer.GpuFrameTexels, 1));
            gpu.FramesMaterial.SetTexture("_AlpsPrevFrames", previous);
            gpu.FramesMaterial.SetFloat("_AlpsShowToken", 1f);

            float[] Render(Vector3 head, float deltaTime)
            {
                gpu.FramesMaterial.SetVector("_AlpsTrackTarget0", new Vector4(head.x, head.y, head.z, 1f));
                gpu.FramesMaterial.SetFloat("_AlpsDeltaTime", deltaTime);
                gpu.Render(1f);
                var frame = AlpsGpuShow.ReadFrames(gpu.Frames, 1, AlpsShowLayout.FrameAimed + 1);
                Graphics.Blit(gpu.Frames, previous);
                return frame;
            }

            float Pan(Vector3 head)
            {
                var local = Quaternion.Inverse(Quaternion.Euler(0f, 90f, 0f)) * head;
                return AimPan(local);
            }

            var first = new Vector3(1f, -2f, 3f);
            var aimed = Render(first, 0f);
            Assert.AreEqual(1f, aimed[AlpsShowLayout.FrameAimed], "The head is aimed.");
            Assert.AreEqual(Pan(first), aimed[AlpsShowLayout.FramePan], 0.01f, "The first frame points straight at the user.");
            var local = Quaternion.Inverse(Quaternion.Euler(0f, 90f, 0f)) * first;
            Assert.AreEqual(AimTilt(local), aimed[AlpsShowLayout.FrameTilt], 0.01f);

            // Half a follow later the head has turned part of the way to where the user went.
            var second = new Vector3(-2f, -2f, 1f);
            var followed = Render(second, 0.1f);
            var share = 1f - Mathf.Exp(-2f * 0.1f);
            var expectedPan = aimed[AlpsShowLayout.FramePan] + Mathf.DeltaAngle(aimed[AlpsShowLayout.FramePan], Pan(second)) * share;
            Assert.AreEqual(expectedPan, followed[AlpsShowLayout.FramePan], 0.01f, "Pan follows the short way round.");

            // Once the user leaves, nothing else moves the head, so it stops where it was.
            gpu.FramesMaterial.SetVector("_AlpsTrackTarget0", Vector4.zero);
            float[] RenderHeld(float token)
            {
                gpu.FramesMaterial.SetFloat("_AlpsShowToken", token);
                gpu.Render(1f);
                var frame = AlpsGpuShow.ReadFrames(gpu.Frames, 1, AlpsShowLayout.FrameTrackHeld + 1);
                Graphics.Blit(gpu.Frames, previous);
                return frame;
            }

            var gone = RenderHeld(1f);
            Assert.AreEqual(0f, gone[AlpsShowLayout.FrameAimed]);
            Assert.AreEqual(1f, gone[AlpsShowLayout.FrameTrackHeld], "The head stays where the user left it.");
            Assert.AreEqual(followed[AlpsShowLayout.FramePan], gone[AlpsShowLayout.FramePan], 0.001f, "The pan the head was following with.");
            Assert.AreEqual(followed[AlpsShowLayout.FrameTilt], gone[AlpsShowLayout.FrameTilt], 0.001f);

            var still = RenderHeld(1f);
            Assert.AreEqual(followed[AlpsShowLayout.FramePan], still[AlpsShowLayout.FramePan], 0.001f, "Frames later it still stays.");

            var other = RenderHeld(2f);
            Assert.AreEqual(0f, other[AlpsShowLayout.FrameTrackHeld], "Another show's frame does not count.");
            Assert.AreEqual(0f, other[AlpsShowLayout.FramePan], 0.001f, "Nothing turned the head before, so the default pan.");
        }

        [TestCase(false, true)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public void Tracking_PointsTheVrslBeamAtTheUser(bool invertPan, bool invertTilt)
        {
            var mover = (GameObject)Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MoverPrefab));
            _created.Add(mover);
            mover.transform.rotation = Quaternion.Euler(70f, 30f, 15f);
            var fixture = mover.GetComponentInChildren<AdzukiSoft.ALPS.Vrsl.VRStageLighting_DMX_Static>();
            fixture.invertPan = invertPan;
            fixture.invertTilt = invertTilt;
            var head = AlpsShowPlayer.GetVRSLAimTransform(fixture);

            // Where the beam rests in the mesh, from the far end of the volumetric cone.
            var volumetric = System.Array.Find(fixture.objRenderers, r => r != null && r.sharedMaterial.shader.name.EndsWith("/Volumetric"));
            var mesh = volumetric.GetComponent<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var rest = Vector3.zero;
            var far = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                if (uvs[i].x > 0.9f)
                {
                    rest += vertices[i];
                    far++;
                }
            }

            rest = rest / far - (Vector3)volumetric.sharedMaterial.GetVector("_FixtureRotationOrigin");
            rest = rest.normalized;

            var aim = new float[AlpsShowPlayer.GpuAimStride];
            AlpsShowPlayer.WriteAim(head.worldToLocalMatrix, aim, 0);
            AlpsShowPlayer.OrientVRSLAim(fixture, aim, 0);
            foreach (var user in new[] { new Vector3(3f, -8f, 2f), new Vector3(-6f, -1f, -4f), new Vector3(1f, 5f, 7f) })
            {
                var target = head.position + user;
                var local = new Vector3(
                    aim[0] * target.x + aim[1] * target.y + aim[2] * target.z + aim[3],
                    aim[4] * target.x + aim[5] * target.y + aim[6] * target.z + aim[7],
                    aim[8] * target.x + aim[9] * target.y + aim[10] * target.z + aim[11]);

                // VRSL's own rotation (VRSL-StandardMover-Vertex.cginc) of the pan and tilt the
                // grid hands it, which reads the pan negated.
                var panAngle = -AimPan(local) * (invertPan ? -1f : 1f) * Mathf.Deg2Rad;
                var tiltAngle = (AlpsShowPlayer.VrslDefaultTiltOffset + AimTilt(local)) * (invertTilt ? -1f : 1f) * Mathf.Deg2Rad;
                var tilted = new Vector3(rest.x, Mathf.Cos(tiltAngle) * rest.y - Mathf.Sin(tiltAngle) * rest.z, Mathf.Sin(tiltAngle) * rest.y + Mathf.Cos(tiltAngle) * rest.z);
                var beam = new Vector3(Mathf.Cos(panAngle) * tilted.x - Mathf.Sin(panAngle) * tilted.y, Mathf.Sin(panAngle) * tilted.x + Mathf.Cos(panAngle) * tilted.y, tilted.z);

                Assert.Less(Vector3.Angle(head.TransformDirection(beam), user), 1f, $"The beam points at a user at {user}.");
            }
        }

        private const string MoverPrefab = "Packages/me.adzuki.live-performance-system/Runtime/VRSL/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Mover-Spotlight-H-13CH.prefab";

        /// <summary>Pan that points the head at a point in its aim space, turning from +Y around Z.</summary>
        private static float AimPan(Vector3 local)
        {
            return Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;
        }

        /// <summary>Tilt that points the head at a point in its aim space. Tilt 0 aims along +Z.</summary>
        private static float AimTilt(Vector3 local)
        {
            return Mathf.Atan2(Mathf.Sqrt(local.x * local.x + local.y * local.y), local.z) * Mathf.Rad2Deg;
        }

        /// <summary>The spin phase for a gobo angle, which VRSL turns into degrees(phase * 4).</summary>
        private static float SpinPhase(float degrees)
        {
            return degrees * Mathf.Deg2Rad / 4f;
        }

        /// <summary>The dimmer the grid holds at brightness <paramref name="level"/>, a share of 100%: up while the fixture is lit.</summary>
        private static float GridDimmer(float level)
        {
            return level > 0f ? 1.26f : 0f;
        }

        /// <summary>The colour channel the grid holds, which carries the brightness.</summary>
        private static float GridColour(float channel, float level)
        {
            return level > 0f ? AlpsGpuShow.Light(channel, level) * 2.02f : 0f;
        }

        /// <summary>The colour channel a laser reads, which shares the brightness with its intensity up to 100%.</summary>
        private static float LaserColour(float channel, float level)
        {
            return AlpsGpuShow.Light(channel, Mathf.Max(level, 1f)) * Mathf.Pow(Mathf.Min(level, 1f), 1.2f);
        }

        // ------------------------------------------------------------------ laser

        private const string LaserPrefab = "Packages/me.adzuki.live-performance-system/Runtime/VRSL/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Static-Laser-H-13CH.prefab";

        /// <summary>The pan and tilt the grid hands a laser, which reaches 90 degrees to either side.</summary>
        private static void LaserFold(ref float pan, ref float tilt)
        {
            pan -= 360f * Mathf.Round(pan / 360f);
            if (Mathf.Abs(pan) > AlpsShowPlayer.LaserAngleRange)
            {
                pan -= pan > 0f ? 180f : -180f;
                tilt = -tilt;
            }
        }

        private static float LaserStretch(float length)
        {
            return Mathf.Clamp(length / AlpsShowPlayer.ModelConeLengthLimit, AlpsShowPlayer.LaserMinStretch, AlpsShowPlayer.LaserMaxStretch);
        }

        /// <summary>The width channel that opens a laser's cone to twice <paramref name="width"/> degrees.</summary>
        private static float LaserWidth(float width, float length)
        {
            var opening = Mathf.Tan(Mathf.Clamp(width, 0f, 89f) * Mathf.Deg2Rad) * LaserStretch(length) / AlpsShowPlayer.LaserMeshSlope;
            return opening * -AlpsShowPlayer.LaserMinConeWidth / (AlpsShowPlayer.LaserMaxConeWidth - AlpsShowPlayer.LaserMinConeWidth);
        }

        private static float[] LaserInfo(float flatness, float beams, float thickness)
        {
            var info = NeutralInfo();
            info[AlpsShowPlayer.InfoLayout] = AlpsShowPlayer.DmxLayoutLaser;
            info[AlpsShowPlayer.InfoLaserFlatness] = flatness;
            info[AlpsShowPlayer.InfoLaserBeams] = beams;
            info[AlpsShowPlayer.InfoLaserThickness] = thickness;
            return info;
        }

        [Test]
        public void Grid_LaysALaserOutOnItsOwnChannels()
        {
            var set = Set(0f);
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.value = 30f;
            move.pan.hasSpread = true;
            move.pan.spreadRange = new Vector2(40f, 120f);
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.value = 30f;
            cone.coneLength.value = 100f;
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(new Color(0.2f, 0.4f, 1f)));
            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.brightness.hasSpread = true;
            brightness.brightness.spreadRange = new Vector2(50f, 150f);
            var show = AlpsShowCompiler.CompileStandalone(3, Bpm, new AlpsStandaloneClip { set = set, end = 10f });

            // The first fixture is a mover, the other two are lasers.
            const float flatness = 1.2f;
            const float beams = 12f;
            const float thickness = 0.03f;
            var info = new List<float>(NeutralInfo());
            info.AddRange(LaserInfo(flatness, beams, thickness));
            info.AddRange(LaserInfo(flatness, beams, thickness));
            var gpu = Keep(new AlpsGpuShow(show, 3, info: info.ToArray()));
            var frames = gpu.FramesAt(1f);
            var grid = gpu.ReadBack(gpu.Grid);
            var spin = gpu.ReadBack(gpu.Spin);

            float Channel(int fixture, int ch) => frames[fixture * AlpsShowLayout.FrameStride + ch];
            float Read(Texture2D texture, int fixture, int offset)
            {
                var texel = VrslTexel(13 * fixture + 1 + offset);
                return texture.GetPixel(texel.x, texel.y).r;
            }

            Assert.AreEqual(GridDimmer(0.5f), Read(grid, 0, 5), 1e-4f, "The mover keeps its own channels.");
            Assert.AreEqual(GridColour(1f, 0.5f), Read(grid, 0, 9), 1e-4f);

            var folded = 0;
            for (var fixture = 1; fixture < 3; fixture++)
            {
                var level = Channel(fixture, AlpsShowLayout.FrameBrightness) * Channel(fixture, AlpsShowLayout.FrameBrightnessScale) / 100f;
                var pan = Channel(fixture, AlpsShowLayout.FramePan);
                var tilt = Channel(fixture, AlpsShowLayout.FrameTilt);
                Assert.AreEqual(30f, tilt, 1e-3f);
                LaserFold(ref pan, ref tilt);
                folded += tilt < 0f ? 1 : 0;
                var range = AlpsShowPlayer.LaserAngleRange;
                var diagnostics = $"fixture {fixture}";
                Assert.AreEqual((-tilt + range) / (2f * range), Read(grid, fixture, 0), 1e-4f, "Tilt. " + diagnostics);
                Assert.AreEqual((pan + range) / (2f * range), Read(grid, fixture, 1), 1e-4f, "Pan. " + diagnostics);
                Assert.AreEqual((2f - 0.5f) / 5.5f, Read(grid, fixture, 2), 1e-4f, "Twice the mesh. " + diagnostics);
                Assert.AreEqual(LaserWidth(30f, 100f), Read(grid, fixture, 3), 1e-4f, "Cone width. " + diagnostics);
                Assert.AreEqual(flatness / 1.999f, Read(grid, fixture, 4), 1e-4f, "Flatness. " + diagnostics);
                Assert.AreEqual((beams - 4f) / 64f, Read(grid, fixture, 5), 1e-4f, "Beams. " + diagnostics);
                Assert.AreEqual(0f, Read(grid, fixture, 6), 1e-4f, "Spin. " + diagnostics);
                Assert.AreEqual(LaserColour(0.2f, level), Read(grid, fixture, 7), 1e-4f, "Red. " + diagnostics);
                Assert.AreEqual(LaserColour(0.4f, level), Read(grid, fixture, 8), 1e-4f, "Green. " + diagnostics);
                Assert.AreEqual(LaserColour(1f, level), Read(grid, fixture, 9), 1e-4f, "Blue. " + diagnostics);
                Assert.AreEqual(Mathf.Min(level, 1f), Read(grid, fixture, 10), 1e-4f, "Intensity. " + diagnostics);
                Assert.AreEqual((thickness - 0.0005f) / 0.0495f, Read(grid, fixture, 11), 1e-4f, "Thickness. " + diagnostics);
                Assert.AreEqual(0f, Read(spin, fixture, 6), 1e-6f, "The beams stand still. " + diagnostics);
                Assert.AreEqual(0f, Read(spin, fixture, 10), 1e-6f, "A laser has no gobo to turn. " + diagnostics);
            }

            Assert.AreEqual(1, folded, "The pan of 120 is the one of -60 with the tilt the other way.");
            Assert.AreEqual(100f, Channel(1, AlpsShowLayout.FrameBrightness), 1e-3f);
            Assert.AreEqual(1f, Read(grid, 1, 10), 1e-4f, "100% is the whole intensity.");
        }

        [Test]
        public void Grid_LaysAMultiLightBarOutOverFourRows()
        {
            const int segments = AlpsVRSLMultiLightBar.SegmentCount;
            const int count = segments + 2;
            const int barRow = 5;
            var set = Set(0f);
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(new Color(0.2f, 0.4f, 1f)));
            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.brightness.hasSpread = true;
            brightness.brightness.spreadRange = new Vector2(20f, 150f);
            var show = AlpsShowCompiler.CompileStandalone(count, Bpm, new AlpsStandaloneClip { set = set, end = 10f });

            // A mover on the row before the bar, the bar's twelve segments on its four rows,
            // and a mover on the row after them. Rows 6 and 7 hold channels 91 and 104, which
            // VRSL reads through its exceptions for the thirteenth channel.
            var info = new List<float>(NeutralInfo());
            var rows = new List<int> { barRow - 1 };
            for (var s = 0; s < segments; s++)
            {
                var segment = new float[AlpsShowPlayer.GpuFixtureInfoStride];
                AlpsShowPlayer.WriteBarSegmentDmxInfo(segment, 0, s);
                info.AddRange(segment);
                rows.Add(barRow);
            }

            info.AddRange(NeutralInfo());
            rows.Add(barRow + AlpsVRSLMultiLightBar.RowCount);
            var gpu = Keep(new AlpsGpuShow(show, count, rows.ToArray(), info: info.ToArray()));
            var frames = gpu.FramesAt(1f);
            var grid = gpu.ReadBack(gpu.Grid);

            // What VRSL's own getValueAtCoords reads for every channel of the first ten rows.
            var probe = Keep(new Material(Shader.Find("Hidden/ALPS/Tests/VRSL Probe")));
            const int channels = 13 * 10;
            probe.SetFloat("_AlpsProbeChannels", channels);
            Shader.SetGlobalTexture("_Udon_DMXGridRenderTexture", gpu.Grid);
            Shader.SetGlobalTexture("_Udon_DMXGridSpinTimer", gpu.Spin);
            Shader.SetGlobalVector("_Udon_DMXGridRenderTexture_TexelSize", new Vector4(1f / gpu.Grid.width, 1f / gpu.Grid.height, gpu.Grid.width, gpu.Grid.height));
            var read = Keep(AlpsPreviewDriver.CreateGpuTarget("Probe", channels, 1));
            Graphics.Blit(null, read, probe);
            var values = Keep(AlpsGpuShow.ReadBackTexture(read));

            float Level(int fixture) => frames[fixture * AlpsShowLayout.FrameStride + AlpsShowLayout.FrameBrightness]
                                        * frames[fixture * AlpsShowLayout.FrameStride + AlpsShowLayout.FrameBrightnessScale] / 100f;
            float Stored(int channel)
            {
                var texel = VrslTexel(channel + 1);
                return grid.GetPixel(texel.x, texel.y).r;
            }

            var thirteenth = 0;
            var failures = new List<string>();
            for (var s = 0; s < segments; s++)
            {
                var fixture = 1 + s;
                var level = Level(fixture);
                Assert.Greater(level, 0f);
                var colour = new[] { 0.2f, 0.4f, 1f };
                for (var k = 0; k < AlpsShowPlayer.BarSegmentChannels; k++)
                {
                    var channel = barRow * 13 + s * AlpsShowPlayer.BarSegmentChannels + k;
                    thirteenth += channel % 13 == 12 ? 1 : 0;
                    var expected = k == 0 ? 1f : AlpsGpuShow.Light(colour[k - 1], level);
                    if (Mathf.Abs(expected - Stored(channel)) > 1e-4f)
                    {
                        failures.Add($"segment {s} channel {k}: expected {expected:0.####}, the grid holds {Stored(channel):0.####}");
                    }

                    if (Mathf.Abs(expected - values.GetPixel(channel, 0).r) > 1e-3f)
                    {
                        failures.Add($"segment {s} channel {k} (DMX {channel + 1}): expected {expected:0.####}, VRSL reads {values.GetPixel(channel, 0).r:0.####}");
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.AreEqual(3, thirteenth, "The bar's channels cross three thirteenth columns.");
            Assert.AreNotEqual(Level(1), Level(segments), "The spread runs along the bar.");

            // The four channels the bar leaves of its last row stay dark, and the movers
            // either side keep their own rows.
            for (var channel = (barRow + 3) * 13 + 9; channel < (barRow + 4) * 13; channel++)
            {
                Assert.AreEqual(0f, Stored(channel), 1e-6f, $"Channel {channel + 1} past the bar's last.");
            }

            Assert.AreEqual(GridDimmer(Level(0)), Stored((barRow - 1) * 13 + 5), 1e-4f, "The mover before the bar.");
            Assert.AreEqual(GridDimmer(Mathf.Min(Level(count - 1), 1f)), Stored((barRow + 4) * 13 + 5), 1e-4f, "The mover after the bar.");
            Assert.AreEqual(0f, Stored((barRow - 1) * 13 + 12), 1e-6f, "A mover reads nothing from its thirteenth channel.");
        }


        [Test]
        public void Laser_KeepsWhatItsMaterialSays()
        {
            var laser = (GameObject)Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(LaserPrefab));
            _created.Add(laser);
            var fixture = laser.GetComponentInChildren<AdzukiSoft.ALPS.Vrsl.VRStageLighting_DMX_Static>();
            var material = AlpsShowPlayer.GetVRSLLaserMaterial(fixture);
            Assert.NotNull(material, "The laser prefab is drawn with VRSL's laser shader.");

            var mover = (GameObject)Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MoverPrefab));
            _created.Add(mover);
            Assert.IsNull(AlpsShowPlayer.GetVRSLLaserMaterial(mover.GetComponentInChildren<AdzukiSoft.ALPS.Vrsl.VRStageLighting_DMX_Static>()));

            var info = new float[AlpsShowPlayer.GpuFixtureInfoStride];
            AlpsShowPlayer.CaptureVRSLDmxInfo(fixture, info, 0);
            Assert.AreEqual(AlpsShowPlayer.DmxLayoutLaser, info[AlpsShowPlayer.InfoLayout]);
            Assert.AreEqual(material.GetFloat(AlpsShowPlayer.LaserFlatnessProperty), info[AlpsShowPlayer.InfoLaserFlatness]);
            Assert.AreEqual(material.GetFloat(AlpsShowPlayer.LaserBeamsProperty), info[AlpsShowPlayer.InfoLaserBeams]);
            Assert.AreEqual(material.GetFloat(AlpsShowPlayer.LaserThicknessProperty), info[AlpsShowPlayer.InfoLaserThickness]);
            Assert.Greater(info[AlpsShowPlayer.InfoLaserBeams], 3f);

            var frame = new float[AlpsShowLayout.FrameStride];
            AlpsShowPlayer.CaptureVRSL(fixture, frame, 0);
            Assert.AreEqual(material.GetFloat(AlpsShowPlayer.LaserPanProperty), frame[AlpsShowLayout.FramePan], 1e-4f);
            Assert.AreEqual(-material.GetFloat(AlpsShowPlayer.LaserTiltProperty), frame[AlpsShowLayout.FrameTilt], 1e-4f);

            // What the grid hands back for the captured cone is the material's own.
            var width = material.GetFloat(AlpsShowPlayer.LaserConeWidthProperty);
            var length = material.GetFloat(AlpsShowPlayer.LaserConeLengthProperty);
            var range = AlpsShowPlayer.LaserMaxConeWidth - AlpsShowPlayer.LaserMinConeWidth;
            Assert.AreEqual((width - AlpsShowPlayer.LaserMinConeWidth) / range, LaserWidth(frame[AlpsShowLayout.FrameConeWidth], frame[AlpsShowLayout.FrameConeLength]), 1e-4f);
            Assert.AreEqual(1f + length, LaserStretch(frame[AlpsShowLayout.FrameConeLength]), 1e-4f);
        }

        [Test]
        public void Laser_OpensItsConeByTwiceTheModelWidth()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(LaserPrefab);
            var mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uv2 = mesh.uv2;
            foreach (var length in new[] { 50f, 120f })
            {
                foreach (var width in new[] { 10f, 30f, 55f })
                {
                    // VRSL's own vertex maths (VRSL-BasicLaser-DMX.shader) on the far end of the cone.
                    var dmxLength = Mathf.Lerp(-0.5f, 5f, (LaserStretch(length) - 0.5f) / 5.5f);
                    var dmxWidth = Mathf.LerpUnclamped(AlpsShowPlayer.LaserMinConeWidth, AlpsShowPlayer.LaserMaxConeWidth, LaserWidth(width, length));
                    var widest = 0f;
                    var far = 0;
                    for (var i = 0; i < vertices.Length; i++)
                    {
                        if (uv2[i].y < 0.99f)
                        {
                            continue;
                        }

                        var y = Mathf.LerpUnclamped(vertices[i].y, vertices[i].y * 2f, dmxLength);
                        var moved = vertices[i] + normals[i] * dmxWidth;
                        widest = Mathf.Max(widest, Mathf.Atan2(new Vector2(moved.x, moved.z).magnitude, Mathf.Abs(y)) * Mathf.Rad2Deg);
                        far++;
                    }

                    Assert.Greater(far, 8, "The far end of the laser's mesh.");
                    Assert.AreEqual(width, widest, 0.75f, $"Half the opening angle at a width of {width} and a length of {length}.");
                }
            }
        }

        [Test]
        public void Tracking_PointsTheLasersConeAtTheUser()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(LaserPrefab);
            var laser = (GameObject)Object.Instantiate(prefab);
            _created.Add(laser);
            var fixture = laser.GetComponentInChildren<AdzukiSoft.ALPS.Vrsl.VRStageLighting_DMX_Static>();
            var head = AlpsShowPlayer.GetVRSLAimTransform(fixture);

            // A laser that stands the way its prefab does fires along +Z and tilts up.
            laser.transform.SetPositionAndRotation(Vector3.zero, prefab.transform.rotation);
            var upright = new float[AlpsShowPlayer.GpuAimStride];
            AlpsShowPlayer.WriteAim(head.worldToLocalMatrix, upright, 0);
            AlpsShowPlayer.OrientVRSLAim(fixture, upright, 0);
            Assert.AreEqual(1f, upright[0], 1e-4f, "Aim space is the world for a laser at its prefab's rotation.");
            Assert.AreEqual(1f, upright[5], 1e-4f);
            Assert.AreEqual(1f, upright[10], 1e-4f);

            laser.transform.SetPositionAndRotation(new Vector3(2f, 3f, -1f), Quaternion.Euler(-70f, 30f, 15f));
            var aim = new float[AlpsShowPlayer.GpuAimStride];
            AlpsShowPlayer.WriteAim(head.worldToLocalMatrix, aim, 0);
            AlpsShowPlayer.OrientVRSLAim(fixture, aim, 0);
            var reached = 0;
            foreach (var user in new[] { new Vector3(3f, -8f, 2f), new Vector3(-6f, -1f, -4f), new Vector3(1f, 5f, 7f), new Vector3(-2f, 3f, -9f), new Vector3(4f, 6f, 1f) })
            {
                var target = head.position + user;
                var local = new Vector3(
                    aim[0] * target.x + aim[1] * target.y + aim[2] * target.z + aim[3],
                    aim[4] * target.x + aim[5] * target.y + aim[6] * target.z + aim[7],
                    aim[8] * target.x + aim[9] * target.y + aim[10] * target.z + aim[11]);
                var pan = AimPan(local);
                var tilt = AimTilt(local);
                LaserFold(ref pan, ref tilt);
                if (Mathf.Abs(tilt) > AlpsShowPlayer.LaserAngleRange)
                {
                    // Behind the laser, where its tilt does not reach.
                    continue;
                }

                // VRSL's own rotation of the cone, which rests along the mesh's -Y: the tilt
                // around X, then the pan around Y. The grid hands it the tilt negated.
                var x = -tilt * Mathf.Deg2Rad;
                var y = pan * Mathf.Deg2Rad;
                var rest = Vector3.down;
                var tilted = new Vector3(rest.x, Mathf.Cos(x) * rest.y - Mathf.Sin(x) * rest.z, Mathf.Sin(x) * rest.y + Mathf.Cos(x) * rest.z);
                var cone = new Vector3(Mathf.Cos(y) * tilted.x + Mathf.Sin(y) * tilted.z, tilted.y, -Mathf.Sin(y) * tilted.x + Mathf.Cos(y) * tilted.z);

                Assert.Less(Vector3.Angle(head.TransformDirection(cone), user), 1f, $"The cone points at a user at {user}.");
                reached++;
            }

            Assert.Greater(reached, 1, "Users in front of the laser.");
        }

        [Test]
        public void Grid_PutsEachFixtureOnItsSharedRow()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Brightness).brightness.value = 50f;
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
            var show = AlpsShowCompiler.CompileStandalone(2, Bpm, new AlpsStandaloneClip { set = set, end = 10f });

            // Another show holds rows 0 to 4, so these two fixtures went on rows 5 and 9.
            var gpu = Keep(new AlpsGpuShow(show, 2, new[] { 5, 9 }));
            gpu.Render(1f);
            var grid = gpu.ReadBack(gpu.Grid);
            for (var row = 0; row < 12; row++)
            {
                var texel = VrslTexel(13 * row + 1 + 7);
                var red = grid.GetPixel(texel.x, texel.y).r;
                Assert.AreEqual(row == 5 || row == 9 ? GridColour(1f, 0.5f) : 0f, red, 1e-4f, $"Red of row {row}.");
            }
        }

        [Test]
        public void Grid_PutsEveryChannelWhereVrslReadsIt()
        {
            var show = Standalone(MoveSet(), ColorSet(), BrightnessSet(), ConeSet(), GoboSet());
            const float time = 1.3f;
            var gpu = Keep(new AlpsGpuShow(show, Fixtures));
            var frames = gpu.FramesAt(time);
            var grid = gpu.ReadBack(gpu.Grid);
            var spin = gpu.ReadBack(gpu.Spin);
            var info = NeutralInfo();

            for (var fixture = 0; fixture < Fixtures; fixture++)
            {
                float Channel(int ch) => frames[fixture * AlpsShowLayout.FrameStride + ch];
                float Read(Texture2D texture, int offset)
                {
                    var texel = VrslTexel(13 * fixture + 1 + offset);
                    var color = texture.GetPixel(texel.x, texel.y);
                    return texture == grid ? color.r * 0.2126729f + color.g * 0.7151522f + color.b * 0.0721750f : color.r;
                }

                var level = Mathf.Max(0f, Channel(AlpsShowLayout.FrameBrightness) * Channel(AlpsShowLayout.FrameBrightnessScale) / 100f);
                var diagnostics = $"fixture {fixture}";
                Assert.AreEqual((-Channel(AlpsShowLayout.FramePan) + info[0]) / (2f * info[0]), Read(grid, 0), 1e-4f, "Pan. " + diagnostics);
                Assert.AreEqual(ConeStretch(Channel(AlpsShowLayout.FrameConeLength), Channel(AlpsShowLayout.FrameConeMeshLength)), Read(grid, 1), 1e-4f, "Cone length. " + diagnostics);
                Assert.AreEqual((Channel(AlpsShowLayout.FrameTilt) + info[1]) / (2f * info[1]), Read(grid, 2), 1e-4f, "Tilt. " + diagnostics);
                Assert.AreEqual(Mathf.Max(0f, Channel(AlpsShowLayout.FrameConeWidth)) / 90f * 6f / 5.5f, Read(grid, 4), 1e-4f, "Cone width. " + diagnostics);
                Assert.AreEqual(GridDimmer(level), Read(grid, 5), 1e-4f, "Dimmer. " + diagnostics);
                Assert.AreEqual(GridColour(Channel(AlpsShowLayout.FrameRed), level), Read(grid, 7), 1e-4f, "Red. " + diagnostics);
                Assert.AreEqual(GridColour(Channel(AlpsShowLayout.FrameGreen), level), Read(grid, 8), 1e-4f, "Green. " + diagnostics);
                Assert.AreEqual(GridColour(Channel(AlpsShowLayout.FrameBlue), level), Read(grid, 9), 1e-4f, "Blue. " + diagnostics);
                Assert.AreEqual(Mathf.Round(Channel(AlpsShowLayout.FrameGobo)), Mathf.Round(Read(grid, 11) * 255f / 30f), "Gobo. " + diagnostics);
                Assert.AreEqual(SpinPhase(Channel(AlpsShowLayout.FrameGoboRotation)), Read(spin, 10), 1e-4f, "Gobo angle. " + diagnostics);
            }
        }

        [Test]
        public void Grid_ReadsBackThroughVrslsOwnFunctions()
        {
            var show = Standalone(MoveSet(), ColorSet(), BrightnessSet(), ConeSet(), GoboSet());
            const float time = 1.3f;
            var gpu = Keep(new AlpsGpuShow(show, Fixtures));
            var frames = gpu.FramesAt(time);
            var grids = new[] { gpu.Grid, gpu.Spin };
            var info = NeutralInfo();

            var probeShader = Shader.Find("Hidden/ALPS/Tests/VRSL Probe");
            Assert.NotNull(probeShader, "The VRSL probe shader did not compile.");
            var probe = Keep(new Material(probeShader));
            var channels = Fixtures * 13;
            probe.SetFloat("_AlpsProbeChannels", channels);
            Shader.SetGlobalTexture("_Udon_DMXGridRenderTexture", grids[0]);
            Shader.SetGlobalTexture("_Udon_DMXGridSpinTimer", grids[1]);
            Shader.SetGlobalVector("_Udon_DMXGridRenderTexture_TexelSize", new Vector4(1f / grids[0].width, 1f / grids[0].height, grids[0].width, grids[0].height));
            var read = Keep(AlpsPreviewDriver.CreateGpuTarget("Probe", channels, 1));
            Graphics.Blit(null, read, probe);
            var values = Keep(AlpsGpuShow.ReadBackTexture(read));

            var failures = new List<string>();
            for (var fixture = 0; fixture < Fixtures; fixture++)
            {
                float Channel(int ch) => frames[fixture * AlpsShowLayout.FrameStride + ch];
                float Vrsl(int offset) => values.GetPixel(13 * fixture + offset, 0).r;
                var level = Mathf.Max(0f, Channel(AlpsShowLayout.FrameBrightness) * Channel(AlpsShowLayout.FrameBrightnessScale) / 100f);
                var expected = new[]
                {
                    (-Channel(AlpsShowLayout.FramePan) + info[0]) / (2f * info[0]),
                    ConeStretch(Channel(AlpsShowLayout.FrameConeLength), Channel(AlpsShowLayout.FrameConeMeshLength)),
                    (Channel(AlpsShowLayout.FrameTilt) + info[1]) / (2f * info[1]),
                    0f,
                    Mathf.Max(0f, Channel(AlpsShowLayout.FrameConeWidth)) / 90f * 6f / 5.5f,
                    GridDimmer(level),
                    1f,
                    GridColour(Channel(AlpsShowLayout.FrameRed), level),
                    GridColour(Channel(AlpsShowLayout.FrameGreen), level),
                    GridColour(Channel(AlpsShowLayout.FrameBlue), level),
                    0f,
                    Mathf.Round(Channel(AlpsShowLayout.FrameGobo)) * 30f / 255f,
                };

                for (var offset = 0; offset < expected.Length; offset++)
                {
                    if (Mathf.Abs(expected[offset] - Vrsl(offset)) > 1e-3f)
                    {
                        failures.Add($"fixture {fixture} offset {offset}: expected {expected[offset]:0.####}, VRSL reads {Vrsl(offset):0.####}");
                    }
                }

                var spin = values.GetPixel(13 * fixture + 10, 0).g;
                if (Mathf.Abs(SpinPhase(Channel(AlpsShowLayout.FrameGoboRotation)) - spin) > 1e-3f)
                {
                    failures.Add($"fixture {fixture} gobo angle: VRSL reads {spin:0.####}");
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>Logs which texel VRSL's getValueAtCoords reads for each channel, from a grid that holds its own coordinates.</summary>
        [Test, Explicit]
        public void Diagnose_WhereVrslReads()
        {
            var width = AlpsShowPlayer.GpuGridWidth;
            var height = AlpsShowPlayer.GpuGridHeight;
            var coded = Keep(new Texture2D(width, height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point });
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var v = x + y * 100f;
                    coded.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            }

            coded.Apply();
            var grid = Keep(AlpsPreviewDriver.CreateGpuTarget("Coded", width, height));
            Graphics.Blit(coded, grid);

            var probe = Keep(new Material(Shader.Find("Hidden/ALPS/Tests/VRSL Probe")));
            const int channels = 13 * 12;
            probe.SetFloat("_AlpsProbeChannels", channels);
            Shader.SetGlobalTexture("_Udon_DMXGridRenderTexture", grid);
            Shader.SetGlobalTexture("_Udon_DMXGridSpinTimer", grid);
            var read = Keep(AlpsPreviewDriver.CreateGpuTarget("Probe", channels, 1));
            Graphics.Blit(null, read, probe);
            var values = Keep(AlpsGpuShow.ReadBackTexture(read));

            var text = new System.Text.StringBuilder("[ALPS Probe] channel: texel x,y\n");
            for (var c = 0; c < channels; c++)
            {
                var v = Mathf.RoundToInt(values.GetPixel(c, 0).r);
                text.Append($"{c + 1}:{v % 100},{v / 100} ");
                if ((c + 1) % 13 == 0)
                {
                    text.Append('\n');
                }
            }

            Debug.Log(text.ToString());
        }

        // ------------------------------------------------------------------ shows

        private static AlpsClipEffectSet Set(float spread = 0.5f)
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.Wave;
            set.phase.SetShares(0.4f, 0.1f, 0.3f);
            set.phase.ease = AlpsEaseType.InOutSine;
            set.phase.fallEase = AlpsEaseType.OutBounce;
            set.phase.beatsPerCycle = 2f;
            set.phase.spread = spread;
            return set;
        }

        private static AlpsClipEffectSet MoveSet()
        {
            var set = Set();
            set.symmetric = true;
            var move = set.Add(AlpsEffectKind.Move);
            move.pan.isRange = true;
            move.pan.range = new Vector2(-60f, 60f);
            move.tilt.isRange = true;
            move.tilt.range = new Vector2(20f, 80f);
            move.tilt.hasSpread = true;
            move.tilt.spreadRange = new Vector2(20f, 40f);
            move.tilt.spreadRangeEnd = new Vector2(70f, 90f);
            move.panTiltPhaseOffsetDegrees = 90f;
            return set;
        }

        private static AlpsClipEffectSet ColorSet()
        {
            var set = Set();
            set.order = AlpsOrderMode.Random;
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(new AlpsColorStop(Color.green));
            color.colorStops.Add(new AlpsColorStop(new Color(0.2f, 0.4f, 1f)));
            set.Add(AlpsEffectKind.Brightness).brightness.value = 80f;
            return set;
        }

        private static AlpsClipEffectSet BrightnessSet()
        {
            var set = Set();
            set.order = AlpsOrderMode.Reverse;
            set.phase.fixtureGroupSize = 2;
            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.brightness.isRange = true;
            brightness.brightness.range = new Vector2(10f, 150f);
            brightness.blackoutOnReturn = true;
            brightness.blackoutFadeIn = 0.2f;
            brightness.blackoutFadeOut = 0.1f;
            return set;
        }

        private static AlpsClipEffectSet ConeSet()
        {
            var set = Set();
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.isRange = true;
            cone.coneWidth.timing = AlpsTimingMode.PerCycle;
            cone.coneWidth.range = new Vector2(10f, 70f);
            cone.coneLength.value = 30f;
            return set;
        }

        private static AlpsClipEffectSet GoboSet()
        {
            var set = Set();
            var gobo = set.Add(AlpsEffectKind.Gobo);
            gobo.goboStops.Add(new AlpsGoboStop(2));
            gobo.goboStops.Add(new AlpsGoboStop(5));
            gobo.goboPhasing.timing = AlpsTimingMode.PerCycle;
            gobo.goboRotationBeats = 3f;
            gobo.goboFixtureStaggerDegrees = 20f;
            return set;
        }

        /// <summary>Each set as its own clip on its own layer, over the whole test.</summary>
        private static AlpsCompiledShow Standalone(params AlpsClipEffectSet[] sets)
        {
            var clips = new AlpsStandaloneClip[sets.Length];
            for (var i = 0; i < sets.Length; i++)
            {
                clips[i] = new AlpsStandaloneClip { set = sets[i], start = 0f, end = 10f, layer = i, seed = 17 + i };
            }

            return AlpsShowCompiler.CompileStandalone(Fixtures, Bpm, clips);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// What VRSL's volumetric mesh adds to the fixture's own mesh length, 4 per unit of
        /// the fine pan channel, for a model cone length.
        /// </summary>
        private static float ConeStretch(float length, float mesh)
        {
            var stretched = mesh * Mathf.Max(0f, length) / AlpsShowPlayer.ModelConeLengthLimit;
            return (stretched - mesh) / 4f;
        }

        private T Keep<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        private AlpsGpuShow Keep(AlpsGpuShow gpu)
        {
            _shows.Add(gpu);
            return gpu;
        }

        private static float[] NeutralInfo()
        {
            var info = new float[AlpsShowPlayer.GpuFixtureInfoStride];
            AlpsShowPlayer.WriteNeutralDmxInfo(info, 0);
            return info;
        }

        /// <summary>
        /// The texel VRSL samples for absolute channel <paramref name="dmx"/>, as its
        /// getValueAtCoords and IndustryRead work it out on the 26 x 240 horizontal grid.
        /// IndustryRead takes the row as an int, so the fraction of dmx / 13 is dropped.
        /// <see cref="Grid_LaysAMultiLightBarOutOverFourRows"/> holds it to VRSL's own read.
        /// </summary>
        private static Vector2Int VrslTexel(int dmx)
        {
            var x = dmx % 13;
            var row = dmx / 13 + 1;
            if (x == 0)
            {
                // The thirteenth channel of a row is read from that row's thirteenth column.
                x = 13;
                row -= 1;
            }

            var resMultiplier = 26f / 13f;
            var u = x * resMultiplier * (1f / 26f) - 0.015f;
            var v = row * resMultiplier * (1f / 240f) - 0.001915f;
            return new Vector2Int(Mathf.FloorToInt(u * 26f), Mathf.FloorToInt(v * 240f));
        }
    }
}
