using System.Collections.Generic;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Setting a show up from the container inspector: the Timeline asset, the director around
    /// the container and a group of lighting tracks per container, bound and ready to play.
    /// </summary>
    public class AlpsTimelineSetupTests
    {
        private readonly List<string> _assets = new List<string>();
        private AlpsLanguage _language;
        private bool _hadFolders;

        [SetUp]
        public void OpenScene()
        {
            _hadFolders = AssetDatabase.IsValidFolder(AlpsTimelineSetup.ShowsFolder);
            // Track names come from the language tables.
            _language = AlpsStrings.Language;
            AlpsStrings.Language = AlpsLanguage.English;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void CloseScene()
        {
            AlpsStrings.Language = _language;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var path in _assets)
            {
                AssetDatabase.DeleteAsset(path);
            }

            _assets.Clear();
            // The fixtures ALPS adds bring materials of their own.
            AssetDatabase.DeleteAsset(AlpsFixtureMaterials.Folder);
            AssetDatabase.DeleteAsset(AlpsProjectionLight.ShaderFolder);
            AlpsTimelineSetup.DeleteFoldersCreatedSince(_hadFolders);
        }

        /// <summary>A container named <paramref name="name"/> holding <paramref name="fixtures"/> par lights.</summary>
        private static AlpsContainer Container(string name, GameObject parent, int fixtures)
        {
            var containerObject = AlpsCreateMenu.CreateContainer(parent);
            containerObject.name = name;
            for (var i = 0; i < fixtures; i++)
            {
                AlpsCreateMenu.CreateFixture(AlpsCreateMenu.ParLight, containerObject);
            }

            return containerObject.GetComponent<AlpsContainer>();
        }

        private PlayableDirector SetUp(AlpsContainer container)
        {
            var director = AlpsTimelineSetup.SetUp(container);
            _assets.Add(AssetDatabase.GetAssetPath(director.playableAsset));
            return director;
        }

        [Test]
        public void CanSetUp_WaitsForATopContainerWithFixturesNothingPlays()
        {
            var top = Container("Setup Top", null, 0);
            Assert.IsFalse(AlpsTimelineSetup.CanSetUp(top), "Nothing to light yet.");

            AlpsCreateMenu.CreateFixture(AlpsCreateMenu.ParLight, top.gameObject);
            Assert.IsTrue(AlpsTimelineSetup.CanSetUp(top));

            var nested = Container("Setup Nested", top.gameObject, 1);
            Assert.IsFalse(AlpsTimelineSetup.CanSetUp(nested), "The container above drives it already.");

            var director = SetUp(top);
            Assert.IsFalse(AlpsTimelineSetup.CanSetUp(top), "A director plays it now.");
            Assert.AreSame(director, AlpsTimelineSetup.FindDirector(top));
            Assert.AreSame(director, AlpsTimelineSetup.FindDirector(nested), "Its own group is bound too.");
        }

        [Test]
        public void SetUp_PutsADirectorAroundTheContainer()
        {
            var stage = new GameObject("Stage");
            new GameObject("Before").transform.SetParent(stage.transform);
            var container = Container("Setup Around", stage, 2);
            container.transform.position = new Vector3(1f, 2f, 3f);

            var director = SetUp(container);

            // A project may already hold the folder in another case, which Windows keeps.
            var path = AssetDatabase.GetAssetPath(director.playableAsset);
            Assert.IsTrue(path.StartsWith(AlpsTimelineSetup.ShowsFolder + "/", System.StringComparison.OrdinalIgnoreCase), path);
            Assert.AreSame(director.transform, container.transform.parent, "The container sits under the director.");
            Assert.AreSame(stage.transform, director.transform.parent, "Where the container was.");
            Assert.AreEqual(1, director.transform.GetSiblingIndex());
            Assert.AreEqual(new Vector3(1f, 2f, 3f), container.transform.position, "The container stays where it was.");
            Assert.AreEqual(DirectorWrapMode.Hold, director.extrapolationMode);
            Assert.IsFalse(director.playOnAwake);
        }

        private static string[] LayerNames => new[] { AlpsStrings.Tr("track.layer", 1), AlpsStrings.Tr("track.layer", 2), AlpsStrings.Tr("track.layer", 3) };

        [Test]
        public void SetUp_GivesEachContainerATrackWithThreeLayers()
        {
            var top = Container("Setup Layers", null, 2);
            var left = Container("Left", top.gameObject, 2);
            var deep = Container("Deep", left.gameObject, 1);

            var director = SetUp(top);
            var timeline = (TimelineAsset)director.playableAsset;

            // A container with others in it groups its track with theirs, as the Hierarchy nests them.
            var topGroup = (GroupTrack)timeline.GetRootTracks().Single();
            Assert.AreEqual("Setup Layers", topGroup.name);
            var children = topGroup.GetChildTracks().ToList();
            CollectionAssert.AreEqual(new[] { "Setup Layers", "Left" }, children.Select(t => t.name));
            var topTrack = (AlpsLightingTrack)children[0];
            var leftGroup = (GroupTrack)children[1];
            var leftChildren = leftGroup.GetChildTracks().ToList();
            CollectionAssert.AreEqual(new[] { "Left", "Deep" }, leftChildren.Select(t => t.name), "A container with nothing in it needs no group.");

            Assert.AreSame(top, director.GetGenericBinding(topTrack));
            Assert.AreSame(left, director.GetGenericBinding(leftChildren[0]));
            Assert.AreSame(deep, director.GetGenericBinding(leftChildren[1]));

            CollectionAssert.AreEqual(LayerNames, topTrack.GetChildTracks().Select(t => t.name));
            Assert.AreEqual(0, topTrack.GetClips().Count(), "The track itself waits below its layers.");
            foreach (var layer in topTrack.GetChildTracks())
            {
                Assert.IsInstanceOf<AlpsLightingTrack>(layer);
                Assert.AreSame(topTrack, ((AlpsLightingTrack)layer).RootTrack, "A layer plays on the track's container.");
                Assert.AreEqual(1, layer.GetClips().Count(), "The top container's layers start with one clip each.");
            }

            foreach (var track in leftChildren)
            {
                CollectionAssert.AreEqual(LayerNames, track.GetChildTracks().Select(t => t.name));
                Assert.IsTrue(track.GetChildTracks().All(layer => !layer.GetClips().Any()), "A nested container starts empty, so it does not cover the top one.");
            }

            var show = AlpsShowCompiler.Compile(director);
            CollectionAssert.IsEmpty(show.errors);
            CollectionAssert.IsEmpty(show.warnings);
            Assert.AreEqual(5, show.FixtureCount);
            Assert.AreEqual(3, show.ClipCount);
        }

        [Test]
        public void SetUp_StartsWithAClipOfEachKind()
        {
            var director = SetUp(Container("Setup Clips", null, 1));
            var timeline = (TimelineAsset)director.playableAsset;
            var track = (AlpsLightingTrack)timeline.GetRootTracks().Single();
            var clips = track.GetChildTracks().Select(t => t.GetClips().Single()).ToList();
            CollectionAssert.AreEqual(
                new[] { AlpsStrings.Tr("effect.move.name"), AlpsStrings.Tr("effect.color.name"), AlpsStrings.Tr("effect.brightness.name") },
                clips.Select(c => c.displayName));

            AlpsClipEffectSet Data(int index) => ((AlpsLightingClip)clips[index].asset).data;

            var move = Data(0).effects.Single();
            Assert.AreEqual(AlpsEffectKind.Move, move.kind);
            Assert.IsTrue(move.tilt.isRange && move.pan.isRange, "The heads sweep.");
            Assert.AreEqual(AlpsEffectKind.Color, Data(1).effects.Single().kind);
            Assert.AreEqual(1, Data(1).effects.Single().colorStops.Count);
            Assert.AreEqual(100f, Data(2).effects.Single().brightness.value);

            foreach (var clip in clips)
            {
                Assert.AreEqual(0.0, clip.start);
                Assert.AreEqual(AlpsTimelineSetup.StarterBeats * 60.0 / AlpsLightingTrack.DefaultBpm, clip.duration, 0.0001);
            }

            Assert.AreSame(clips[2], AlpsTimelineSetup.FirstBrightnessClip(timeline), "The brightness clip is the one the window selects.");
        }

        [Test]
        public void SetUp_UndoesInOneStep()
        {
            var stage = new GameObject("Stage");
            var container = Container("Setup Undo", stage, 1);

            var director = SetUp(container);
            var show = director.gameObject;
            Undo.PerformUndo();

            Assert.IsTrue(show == null, "The director's object is gone.");
            Assert.AreSame(stage.transform, container.transform.parent, "The container is back where it was.");
            Assert.AreEqual(1, container.Fixtures().Count, "The fixture added before stays.");
        }
    }
}
