using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Clips and containers saved while symmetric was an order of its own, between reverse and
    /// random. Symmetric loads as normal with the switch on, which counts from the middle the
    /// way it did, and random moves down into the place symmetric left.
    /// </summary>
    public class AlpsOrderUpgradeTests
    {
        private const int LegacyReverse = 1;
        private const int LegacySymmetric = 2;
        private const int LegacyRandom = 3;

        [TestCase(LegacyReverse, AlpsOrderMode.Reverse, false)]
        [TestCase(LegacySymmetric, AlpsOrderMode.Normal, true)]
        [TestCase(LegacyRandom, AlpsOrderMode.Random, false)]
        public void SavedClipOrder_Splits(int saved, AlpsOrderMode order, bool symmetric)
        {
            var set = JsonUtility.FromJson<AlpsClipEffectSet>("{\"order\":" + saved + "}");

            Assert.AreEqual(order, set.order);
            Assert.AreEqual(symmetric, set.symmetric);
        }

        [TestCase(LegacyReverse, AlpsOrderMode.Reverse, false)]
        [TestCase(LegacySymmetric, AlpsOrderMode.Normal, true)]
        [TestCase(LegacyRandom, AlpsOrderMode.Random, false)]
        public void SavedContainerOrder_Splits(int saved, AlpsOrderMode order, bool symmetric)
        {
            var settings = JsonUtility.FromJson<AlpsArrangementSettings>("{\"order\":" + saved + ",\"seed\":7}");

            Assert.AreEqual(order, settings.order);
            Assert.AreEqual(symmetric, settings.symmetric);
            Assert.AreEqual(7, settings.seed);
        }

        [Test]
        public void SavedSymmetricOrder_KeepsItsPositions()
        {
            var set = JsonUtility.FromJson<AlpsClipEffectSet>("{\"order\":" + LegacySymmetric + "}");

            // What the symmetric order gave five fixtures: outward from the middle.
            var positions = new int[5];
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = AlpsShowLayout.OrderPosition((int)set.order, set.symmetric, 0, i, positions.Length, 1);
            }

            CollectionAssert.AreEqual(new[] { 2, 1, 0, 1, 2 }, positions);
        }

        [Test]
        public void SavedRandomSymmetric_IsNotConvertedAgain()
        {
            var set = new AlpsClipEffectSet { order = AlpsOrderMode.Random, symmetric = true };
            var reloaded = JsonUtility.FromJson<AlpsClipEffectSet>(JsonUtility.ToJson(set));
            Assert.AreEqual(AlpsOrderMode.Random, reloaded.order);
            Assert.IsTrue(reloaded.symmetric);

            var settings = new AlpsArrangementSettings { order = AlpsOrderMode.Random, symmetric = true };
            var reloadedSettings = JsonUtility.FromJson<AlpsArrangementSettings>(JsonUtility.ToJson(settings));
            Assert.AreEqual(AlpsOrderMode.Random, reloadedSettings.order);
            Assert.IsTrue(reloadedSettings.symmetric);
        }
    }
}
