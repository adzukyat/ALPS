using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Clips saved while the fade belonged to the clip. The brightness effects take it, and a
    /// clip without one keeps it until one is added.
    /// </summary>
    public class AlpsFadeUpgradeTests
    {
        private const string Brightness = "{\"kind\":3,\"parity\":0}";

        private static AlpsClipEffectSet Load(string effects)
        {
            return JsonUtility.FromJson<AlpsClipEffectSet>(
                "{\"version\":1,\"fadeInBeats\":2,\"fadeOutBeats\":4,\"effects\":[" + effects + "]}");
        }

        [Test]
        public void SavedFade_MovesToEveryBrightnessEffect()
        {
            var set = Load("{\"kind\":3,\"parity\":1}," + "{\"kind\":3,\"parity\":2}");

            foreach (var effect in set.effects)
            {
                Assert.AreEqual(2f, effect.fadeInBeats, 0.0001f);
                Assert.AreEqual(4f, effect.fadeOutBeats, 0.0001f);
            }

            Assert.IsFalse(set.HasLegacyFade, "The clip keeps no fade of its own.");
        }

        [Test]
        public void SavedFade_LeavesOtherEffectsAlone()
        {
            var set = Load("{\"kind\":2,\"parity\":0}," + Brightness);

            Assert.AreEqual(0f, set.effects[0].fadeInBeats, 0.0001f, "Colour has no fade.");
            Assert.AreEqual(2f, set.effects[1].fadeInBeats, 0.0001f);
        }

        [Test]
        public void SavedFade_WaitsForABrightnessEffect()
        {
            var set = Load("{\"kind\":2,\"parity\":0}");
            Assert.IsTrue(set.HasLegacyFade, "Nothing took it yet, which the compiler warns about.");

            var reloaded = JsonUtility.FromJson<AlpsClipEffectSet>(JsonUtility.ToJson(set));
            Assert.IsTrue(reloaded.HasLegacyFade, "Saving keeps it.");

            var brightness = reloaded.Add(AlpsEffectKind.Brightness);
            Assert.AreEqual(2f, brightness.fadeInBeats, 0.0001f);
            Assert.AreEqual(4f, brightness.fadeOutBeats, 0.0001f);
            Assert.IsFalse(reloaded.HasLegacyFade);
        }

        [Test]
        public void SavedEffectFade_IsNotMovedAgain()
        {
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Brightness).fadeInBeats = 3f;

            var reloaded = JsonUtility.FromJson<AlpsClipEffectSet>(JsonUtility.ToJson(set));

            Assert.AreEqual(3f, reloaded.effects[0].fadeInBeats, 0.0001f);
            Assert.AreEqual(0f, reloaded.effects[0].fadeOutBeats, 0.0001f);
        }
    }
}
