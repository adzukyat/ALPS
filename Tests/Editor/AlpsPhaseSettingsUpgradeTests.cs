using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Settings saved with the forward / ping-pong / random modes and the step ease load as
    /// the same motion in the wave's shares.
    ///
    /// JsonUtility does not follow renamed fields, so the renamed ping-pong shares are only
    /// read through a real asset. The JSON cases cover everything that kept its name.
    /// </summary>
    public class AlpsPhaseSettingsUpgradeTests
    {
        private const string TempFolder = "Assets/AlpsUpgradeTemp";

        /// <summary>The script GUID of <see cref="AlpsClipProfile"/>, which the asset below points at.</summary>
        private const string ProfileScriptGuid = "2a57934446d44b0ca793db9de904ce39";

        [TearDown]
        public void DeleteTempAssets()
        {
            AssetDatabase.DeleteAsset(TempFolder);
        }

        private static AlpsPhaseSettings Load(string json)
        {
            return JsonUtility.FromJson<AlpsPhaseSettings>(json);
        }

        [Test]
        public void Forward_BecomesARiseOverTheWholeCycle()
        {
            var settings = Load("{\"mode\":0,\"ease\":3}");

            Assert.AreEqual(AlpsPhaseMode.Wave, settings.mode);
            Assert.AreEqual(AlpsEaseType.InOutSine, settings.ease);
            Assert.AreEqual(1f, settings.rise, 0.0001f);
            Assert.AreEqual(0f, settings.holdHigh, 0.0001f);
            Assert.AreEqual(0f, settings.fall, 0.0001f);
        }

        [Test]
        public void SavedPingPong_KeepsItsSharesAndComesBackOverTheRest()
        {
            var profile = ImportProfile(
                "      mode: 1\n" +
                "      ease: 12\n" +
                "      pingPongRatio: 0.3\n" +
                "      pingPongHold: 0.2\n");
            var settings = profile.data.phase;

            Assert.AreEqual(AlpsPhaseMode.Wave, settings.mode);
            Assert.AreEqual(AlpsEaseType.InCubic, settings.ease, "Step no longer sits before InCubic.");
            Assert.AreEqual(0.3f, settings.rise, 0.0001f);
            Assert.AreEqual(0.2f, settings.holdHigh, 0.0001f);
            Assert.AreEqual(0.5f, settings.fall, 0.0001f);
            Assert.AreEqual(0f, settings.HoldLow, 0.0001f);
        }

        [Test]
        public void SavedForward_BecomesASawtooth()
        {
            var settings = ImportProfile("      mode: 0\n      ease: 0\n      pingPongRatio: 0.3\n").data.phase;

            Assert.AreEqual(AlpsPhaseMode.Wave, settings.mode);
            Assert.AreEqual(1f, settings.rise, 0.0001f, "Forward ignored the ping-pong ratio it carried.");
            Assert.AreEqual(0f, settings.fall, 0.0001f);
        }

        [Test]
        public void Random_StaysRandom()
        {
            Assert.AreEqual(AlpsPhaseMode.Random, Load("{\"mode\":2,\"ease\":0}").mode);
        }

        [Test]
        public void EasesAfterStep_MoveUpOnePlace()
        {
            // InCubic was 12 and InOutBounce 18 while Step sat at 11.
            Assert.AreEqual(AlpsEaseType.InCubic, Load("{\"mode\":1,\"ease\":12}").ease);
            Assert.AreEqual(AlpsEaseType.InOutBounce, Load("{\"mode\":1,\"ease\":18}").ease);
            Assert.AreEqual(AlpsEaseType.OutBounce, Load("{\"mode\":1,\"ease\":10}").ease);
        }

        [Test]
        public void ForwardStep_BecomesTheSameSquareWave()
        {
            // The old forward step sat at 0 for the first half and at 1 for the second.
            var settings = Load("{\"mode\":0,\"ease\":11}");
            Assert.AreEqual(AlpsEaseType.Linear, settings.ease);
            AssertSamePhase(settings, u => u < 0.5f ? 0f : 1f);

            var inverted = Load("{\"mode\":0,\"ease\":11,\"inverse\":true}");
            AssertSamePhase(inverted, u => u < 0.5f ? 1f : 0f);
        }

        [Test]
        public void PingPongStep_KeepsHowLongItStaysAtOne()
        {
            // A 50 / 50 ping-pong (the ratio's default) stepped at its middle sat at 1 from 0.25
            // to 0.75. The shares can only start the stretch at a cycle's start or end it at a
            // cycle's end, so it keeps its length and moves.
            var settings = Load("{\"mode\":1,\"ease\":11}");
            var high = Enumerable.Range(0, 100)
                .Select(i => Phase(settings, i / 100f))
                .Count(v => v > 0.5f);
            Assert.AreEqual(50, high);
        }

        [Test]
        public void SavedSettings_AreNotConvertedAgain()
        {
            var settings = new AlpsPhaseSettings { mode = AlpsPhaseMode.Random, ease = AlpsEaseType.InCubic };
            settings.SetShares(0.2f, 0.3f, 0.1f);

            var reloaded = Load(JsonUtility.ToJson(settings));

            Assert.AreEqual(AlpsPhaseMode.Random, reloaded.mode);
            Assert.AreEqual(AlpsEaseType.InCubic, reloaded.ease);
            Assert.AreEqual(0.2f, reloaded.rise, 0.0001f);
            Assert.AreEqual(0.3f, reloaded.holdHigh, 0.0001f);
            Assert.AreEqual(0.1f, reloaded.fall, 0.0001f);
        }

        [Test]
        public void PreviewSmokeTimeline_LoadsItsPingPongClipsAsTriangles()
        {
            // The fixture was saved before the wave shares, as a 50% ping-pong.
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(AlpsPreviewSmokeFixtureBuilder.TimelinePath);
            Assert.IsNotNull(timeline);

            var sets = timeline.GetOutputTracks()
                .SelectMany(track => track.GetClips())
                .Select(clip => clip.asset)
                .OfType<AlpsTimelineClip>()
                .Select(asset => asset.data)
                .ToList();
            Assert.IsNotEmpty(sets);

            foreach (var set in sets)
            {
                Assert.AreEqual(AlpsPhaseMode.Wave, set.phase.mode);
                Assert.AreEqual(0.5f, set.phase.rise, 0.0001f);
                Assert.AreEqual(0f, set.phase.holdHigh, 0.0001f);
                Assert.AreEqual(0.5f, set.phase.fall, 0.0001f);
            }
        }

        /// <summary>
        /// Writes a clip profile the way the editor saved it before the wave shares, with
        /// <paramref name="phase"/> as the body of its shared settings, and imports it.
        /// </summary>
        private static AlpsClipProfile ImportProfile(string phase)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "AlpsUpgradeTemp");
            }

            var path = TempFolder + "/Legacy.asset";
            var yaml =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!114 &11400000\n" +
                "MonoBehaviour:\n" +
                "  m_ObjectHideFlags: 0\n" +
                "  m_CorrespondingSourceObject: {fileID: 0}\n" +
                "  m_PrefabInstance: {fileID: 0}\n" +
                "  m_PrefabAsset: {fileID: 0}\n" +
                "  m_GameObject: {fileID: 0}\n" +
                "  m_Enabled: 1\n" +
                "  m_EditorHideFlags: 0\n" +
                "  m_Script: {fileID: 11500000, guid: " + ProfileScriptGuid + ", type: 3}\n" +
                "  m_Name: Legacy\n" +
                "  m_EditorClassIdentifier:\n" +
                "  data:\n" +
                "    bpm: 0\n" +
                "    phase:\n" +
                phase +
                "    effects: []\n";
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), path), yaml);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var profile = AssetDatabase.LoadAssetAtPath<AlpsClipProfile>(path);
            Assert.IsNotNull(profile, "The legacy profile did not import.");
            return profile;
        }

        [Test]
        public void SharedEase_SplitsIntoTheSameRiseAndFall()
        {
            var settings = Load("{\"version\":1,\"mode\":0,\"ease\":4,\"rise\":0.3,\"holdHigh\":0.2,\"fall\":0.4}");

            Assert.AreEqual(AlpsEaseType.InQuad, settings.ease);
            Assert.AreEqual(AlpsEaseType.OutQuad, settings.fallEase, "The fall ran the shared ease backwards.");
            AssertSamePhase(settings, u => SharedEasePhase(AlpsEaseType.InQuad, settings, false, u));
        }

        [Test]
        public void SharedEase_InvertedSwapsTheTwoAndKeepsTheMotion()
        {
            var settings = Load("{\"version\":1,\"mode\":0,\"ease\":10,\"rise\":0.3,\"holdHigh\":0.2,\"fall\":0.4,\"inverse\":true}");

            Assert.AreEqual(AlpsEaseType.InBounce, settings.ease);
            Assert.AreEqual(AlpsEaseType.OutBounce, settings.fallEase);
            AssertSamePhase(settings, u => SharedEasePhase(AlpsEaseType.OutBounce, settings, true, u));
        }

        [Test]
        public void SavedFallEase_IsNotSplitAgain()
        {
            var settings = new AlpsPhaseSettings { ease = AlpsEaseType.InQuad, fallEase = AlpsEaseType.InBounce, inverse = true };

            var reloaded = Load(JsonUtility.ToJson(settings));

            Assert.AreEqual(AlpsEaseType.InQuad, reloaded.ease);
            Assert.AreEqual(AlpsEaseType.InBounce, reloaded.fallEase);
        }

        /// <summary>
        /// The phase before version 2: the plain wave, inverted, then shaped by one ease that
        /// the fall ran backwards.
        /// </summary>
        private static float SharedEasePhase(AlpsEaseType shared, AlpsPhaseSettings settings, bool inverse, float u)
        {
            var wave = AlpsShowEvaluator.Wave(0, 0, settings.rise, settings.holdHigh, settings.fall, u);
            return AlpsEase.Evaluate(shared, inverse ? 1f - wave : wave);
        }

        private static float Phase(AlpsPhaseSettings settings, float u)
        {
            return AlpsShowEvaluator.Phase(
                (int)settings.mode,
                (int)settings.ease,
                (int)settings.fallEase,
                settings.rise,
                settings.holdHigh,
                settings.fall,
                settings.inverse,
                u,
                0,
                0);
        }

        private static void AssertSamePhase(AlpsPhaseSettings settings, System.Func<float, float> expected)
        {
            for (var i = 0; i < 20; i++)
            {
                var u = (i + 0.5f) / 20f;
                Assert.AreEqual(expected(u), Phase(settings, u), 0.0001f, $"At {u} of the cycle.");
            }
        }
    }
}
