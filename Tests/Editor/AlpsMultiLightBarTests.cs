using System.Collections.Generic;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// A multi light bar from the create menu to the DMX grid, through the preview and through
    /// the show player a build makes. Needs a graphics device, so it is ignored under -nographics.
    /// </summary>
    public class AlpsMultiLightBarTests
    {
        private const int Segments = AlpsVRSLMultiLightBar.SegmentCount;
        private static readonly Color Tint = new Color(0.2f, 0.4f, 1f);

        private readonly List<Object> _created = new List<Object>();
        private AlpsContainer _container;
        private AlpsVRSLMultiLightBar _bar;
        private PlayableDirector _director;

        [SetUp]
        public void BuildRig()
        {
            AlpsGpuShow.RequireGraphics();

            // A mover, a bar and a par under one container, which a single track drives.
            var container = Keep(AlpsCreateMenu.CreateContainer(null));
            _container = container.GetComponent<AlpsContainer>();
            AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MoverSpotlight, container);
            _bar = AlpsCreateMenu.CreateFixture(AlpsCreateMenu.MultiLightBar, container).GetComponent<AlpsVRSLMultiLightBar>();
            AlpsCreateMenu.CreateFixture(AlpsCreateMenu.ParLight, container);

            var timeline = Keep(ScriptableObject.CreateInstance<TimelineAsset>());
            _director = Keep(new GameObject("Show")).AddComponent<PlayableDirector>();
            _director.playableAsset = timeline;
            var track = timeline.CreateTrack<AlpsLightingTrack>(null, "Rig");
            _director.SetGenericBinding(track, _container);
            var clip = track.CreateClip<AlpsLightingClip>();
            clip.start = 0.0;
            clip.duration = 10.0;
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Tint));
            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.brightness.hasSpread = true;
            brightness.brightness.spreadRange = new Vector2(20f, 150f);
            ((AlpsLightingClip)clip.asset).data = set;
        }

        [TearDown]
        public void DestroyCreated()
        {
            RenderTexture.active = null;
            foreach (var created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
            AlpsPreviewDriver.MarkDirty();
        }

        [Test]
        public void Show_CountsTheBarsSegmentsAsFixtures()
        {
            var show = AlpsShowCompiler.Compile(_director);

            Assert.IsEmpty(show.errors, string.Join("\n", show.errors));
            Assert.AreEqual(2 + Segments, show.fixtures.Count);
            var segments = _bar.Fixtures();
            for (var s = 0; s < Segments; s++)
            {
                Assert.AreSame(segments[s], show.fixtures[1 + s], $"Segment {s} comes after the mover, in the bar's own order.");
            }
        }

        [Test]
        public void Show_WarnsAboutABarDrivenAsOneFixture()
        {
            // What the create menu made of a multi light bar before its segments were fixtures.
            var old = Keep((GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(AlpsCreateMenu.MultiLightBar.Load()));
            old.transform.SetParent(_container.transform, false);
            old.name = "Old Bar";
            old.AddComponent<AlpsVRSLFixture>();

            var show = AlpsShowCompiler.Compile(_director);

            Assert.IsEmpty(show.errors, string.Join("\n", show.errors));
            Assert.AreEqual(1, show.warnings.Count(warning => warning.Contains("Old Bar")), string.Join("\n", show.warnings));
            Assert.AreEqual(1, show.warnings.Count, string.Join("\n", show.warnings));

            // No other fixture of the create menu is taken for a bar.
            foreach (var prefab in AlpsCreateMenu.Fixtures)
            {
                if (prefab.Segmented)
                {
                    continue;
                }

                var created = Keep(AlpsCreateMenu.CreateFixture(prefab, null));
                Assert.IsNull(created.GetComponent<AlpsFixture>().SetupWarning, prefab.Name);
            }
        }

        [Test]
        public void Player_DrivesTheBarsSegmentsOnItsFourRows()
        {
            var show = AlpsShowCompiler.Compile(_director);
            Assert.IsEmpty(show.errors, string.Join("\n", show.errors));
            var errors = new List<string>();
            var rows = AlpsShowApplier.AssignDmxRows(new List<List<AlpsFixture>> { show.fixtures }, new List<string> { "Show" }, errors);
            Assert.IsEmpty(errors);

            // What Apply hands the player, without the timeline and the scene it also changes.
            var player = AlpsShowSetup.GetOrCreatePlayer(_director);
            Assert.NotNull(player);
            AlpsShowApplier.CopyShowToPlayer(show, player, _director);
            player.dmxRows = show.fixtures.Select(fixture => rows[fixture]).ToArray();
            player.showToken = 1;
            var shared = AlpsGpuAssets.CreateShared();
            Assert.IsTrue(shared.IsComplete);
            Keep(shared.framesMaterial);
            Keep(shared.gridMaterial);
            Keep(shared.penlightMaterial);
            Keep(shared.frames);
            Keep(shared.framesPrevious);
            Keep(shared.spin);
            Keep(shared.penlight);
            player.framesMaterial = shared.framesMaterial;
            player.gridMaterial = shared.gridMaterial;
            player.penlightMaterial = shared.penlightMaterial;
            player.frames = shared.frames;
            player.framesPrevious = shared.framesPrevious;
            player.grid = Keep(shared.grid);
            player.spin = shared.spin;
            player.penlightTarget = shared.penlight;

            CollectionAssert.AreEqual(new[] { 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 + AlpsVRSLMultiLightBar.RowCount }, player.dmxRows);
            for (var s = 0; s < Segments; s++)
            {
                Assert.AreEqual(AlpsShowPlayer.AdapterVRSLBarSegment, player.fixtureAdapter[1 + s]);
                Assert.AreEqual(s, player.fixtureSegment[1 + s]);
                Assert.AreSame(_bar.ResolveTarget(), player.vrslFixtures[1 + s], "Every segment sets the same bar up.");
            }

            player.EvaluateAt(1f);

            var vrsl = _bar.ResolveTarget();
            Assert.IsTrue(vrsl.enableDMXChannels, "The bar is in DMX mode.");
            Assert.AreEqual(13 * 1 + 1, vrsl.dmxChannel, "Its first channel is the first of row 1.");
            AssertBarOnGrid(player.grid, AlpsGpuShow.ReadFrames(player.GetCurrentFrames(), show.fixtures.Count), 1, 1);
        }

        [Test]
        public void Preview_DrivesTheBarAndPutsItBack()
        {
            var vrsl = _bar.ResolveTarget();
            var authoredDmx = vrsl.enableDMXChannels;
            var authoredChannel = vrsl.dmxChannel;
            var authoredTint = vrsl.lightColorTint;

            _director.timeUpdateMode = DirectorUpdateMode.Manual;
            _director.RebuildGraph();
            _director.time = 1.0;
            _director.Evaluate();

            var show = AlpsPreviewDriver.GetShow(_director);
            Assert.NotNull(show, "The preview compiled the show.");
            Assert.AreEqual(2 + Segments, show.fixtures.Count);
            Assert.IsTrue(vrsl.enableDMXChannels);
            Assert.AreEqual(13 * 1 + 1, vrsl.dmxChannel, "The mover takes row 0 and the bar the four after it.");
            Assert.AreEqual(Color.white, vrsl.lightColorTint);
            var frames = AlpsGpuShow.ReadFrames(AlpsPreviewDriver.GetFrames(_director), show.fixtures.Count);
            AssertBarOnGrid(AlpsPreviewDriver.GetGrid(_director), frames, 1, 1);

            foreach (var fixture in show.fixtures)
            {
                fixture.RestoreAuthored(null, 0);
            }

            Assert.AreEqual(authoredDmx, vrsl.enableDMXChannels);
            Assert.AreEqual(authoredChannel, vrsl.dmxChannel);
            Assert.AreEqual(authoredTint, vrsl.lightColorTint, "The bar is back the way it was authored.");
        }

        /// <summary>
        /// Checks the bar's 48 channels from <paramref name="row"/> on against the frames of
        /// its segments, which start at fixture <paramref name="first"/>.
        /// </summary>
        private void AssertBarOnGrid(RenderTexture target, float[] frames, int row, int first)
        {
            var grid = Keep(AlpsGpuShow.ReadBackTexture(target));
            var colour = new[] { Tint.r, Tint.g, Tint.b };
            var levels = new HashSet<float>();
            for (var s = 0; s < Segments; s++)
            {
                var at = (first + s) * AlpsShowLayout.FrameStride;
                var level = frames[at + AlpsShowLayout.FrameBrightness] * frames[at + AlpsShowLayout.FrameBrightnessScale] / 100f;
                Assert.Greater(level, 0f);
                levels.Add(level);
                for (var k = 0; k < AlpsShowPlayer.BarSegmentChannels; k++)
                {
                    // Channel c of the grid is read from texel (2 (c mod 13) + 1, 2 (c / 13) + 1).
                    var channel = row * 13 + s * AlpsShowPlayer.BarSegmentChannels + k;
                    var stored = grid.GetPixel(2 * (channel % 13) + 1, 2 * (channel / 13) + 1).r;
                    var expected = k == 0 ? 1f : AlpsGpuShow.Light(colour[k - 1], level);
                    Assert.AreEqual(expected, stored, 1e-4f, $"Segment {s} channel {k}.");
                }
            }

            Assert.AreEqual(Segments, levels.Count, "The spread gives every segment a level of its own.");
        }

        private T Keep<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }
    }
}
