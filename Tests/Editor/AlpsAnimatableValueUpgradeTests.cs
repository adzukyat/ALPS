using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Values saved when the spread was a step per order position. The step cannot become
    /// the first and last fixture's values without the fixture count, so the spread loads
    /// closed on the value and every fixture matches until it is set again.
    /// </summary>
    public class AlpsAnimatableValueUpgradeTests
    {
        private static AlpsAnimatableValue Load(string json)
        {
            return JsonUtility.FromJson<AlpsAnimatableValue>(json);
        }

        [Test]
        public void SavedStepSpread_ClosesOnTheValue()
        {
            var value = Load(
                "{\"limit\":{\"x\":0,\"y\":90},\"value\":30,\"range\":{\"x\":0,\"y\":90},\"isRange\":true," +
                "\"hasSpread\":true,\"spread\":10,\"spreadRange\":{\"x\":0,\"y\":5}}");

            Assert.IsTrue(value.hasSpread, "S stays on.");
            Assert.AreEqual(new Vector2(30f, 30f), value.spreadRange);
            Assert.AreEqual(new Vector2(30f, 30f), value.spreadRangeEnd);
            Assert.IsFalse(value.HasMultipleStops, "Two closed spreads on the same value have nothing to move between.");
        }

        [Test]
        public void SavedSpreads_AreNotClosedAgain()
        {
            var value = new AlpsAnimatableValue(30f, new Vector2(0f, 90f))
            {
                hasSpread = true,
                isRange = true,
                spreadRange = new Vector2(10f, 40f),
                spreadRangeEnd = new Vector2(60f, 20f),
            };

            var reloaded = Load(JsonUtility.ToJson(value));

            Assert.AreEqual(new Vector2(10f, 40f), reloaded.spreadRange);
            Assert.AreEqual(new Vector2(60f, 20f), reloaded.spreadRangeEnd);
            Assert.IsTrue(reloaded.HasMultipleStops);
        }
    }
}
