using System.Collections.Generic;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// The build copy of a timeline loses its ALPS tracks, and must not lose the length they
    /// gave it. A show played against a video player often has nothing else on its timeline.
    /// </summary>
    public class AlpsBuildTimelineTests
    {
        private const string Folder = "Assets/AlpsTestFixtures";
        private const string SourcePath = Folder + "/BuildTimelineLength.playable";
        private const double ShowLength = 8.0;

        private string _buildPath;

        [TearDown]
        public void DeleteAssets()
        {
            if (!string.IsNullOrEmpty(_buildPath))
            {
                AssetDatabase.DeleteAsset(_buildPath);
            }

            AssetDatabase.DeleteAsset(SourcePath);
        }

        [Test]
        public void BuildCopy_KeepsTheLengthOfAShowAlone()
        {
            var source = CreateSource(TimelineAsset.DurationMode.BasedOnClips);

            var build = Generate(source);

            Assert.AreEqual(ShowLength, build.duration, 0.0001, "Without the ALPS tracks the copy would be 0 long.");
        }

        [Test]
        public void BuildCopy_KeepsTheLengthPastTheOtherTracks()
        {
            var source = CreateSource(TimelineAsset.DurationMode.BasedOnClips);
            var activation = source.CreateTrack<ActivationTrack>(null, "Activation").CreateDefaultClip();
            activation.start = 0.0;
            activation.duration = 2.0;
            Save(source);

            var build = Generate(source);

            Assert.AreEqual(ShowLength, build.duration, 0.0001, "The copy would end with the activation clip.");
        }

        [Test]
        public void BuildCopy_KeepsAFixedLength()
        {
            var source = CreateSource(TimelineAsset.DurationMode.FixedLength);
            source.fixedDuration = 12.0;
            Save(source);

            var build = Generate(source);

            Assert.AreEqual(12.0, build.duration, 0.0001);
        }

        private TimelineAsset Generate(TimelineAsset source)
        {
            _buildPath = AlpsBuildTimeline.PathFor(source);
            var errors = new List<string>();
            var build = AlpsBuildTimeline.Generate(source, errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.NotNull(build);
            Assert.IsFalse(AlpsBuildTimeline.HasAlpsTracks(build));
            return build;
        }

        private static TimelineAsset CreateSource(TimelineAsset.DurationMode mode)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", Folder.Substring("Assets/".Length));
            }

            AssetDatabase.DeleteAsset(SourcePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, SourcePath);
            timeline.durationMode = mode;

            var clip = timeline.CreateTrack<AlpsTimelineTrack>(null, "Show").CreateClip<AlpsTimelineClip>();
            clip.start = 0.0;
            clip.duration = ShowLength;
            Save(timeline);
            return timeline;
        }

        private static void Save(TimelineAsset timeline)
        {
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssetIfDirty(timeline);
        }
    }
}
