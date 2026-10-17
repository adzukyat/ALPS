using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Clips saved while blackout on return belonged to the brightness effect. The moves take
    /// it where they go back on the same phase, and a brightness effect keeps it otherwise.
    /// </summary>
    public class AlpsBlackoutUpgradeTests
    {
        private const string Brightness = "{\"kind\":3,\"parity\":0,\"blackoutOnReturn\":true,\"blackoutFadeIn\":0.1,\"blackoutFadeOut\":0.3}";

        private static AlpsClipEffectSet Load(params string[] effects)
        {
            return JsonUtility.FromJson<AlpsClipEffectSet>(
                "{\"version\":2,\"effects\":[" + string.Join(",", effects) + "]}");
        }

        private static string Move(int parity = 0, int mode = 0, float phaseOffset = 0f, bool tiltOwnPhase = false)
        {
            return "{\"kind\":0,\"parity\":" + parity + ",\"moveMode\":" + mode
                   + ",\"phaseOffset\":" + phaseOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ",\"tilt\":{\"isRange\":true,\"useOwnPhase\":" + (tiltOwnPhase ? "true" : "false") + "}}";
        }

        private static AlpsEffect Find(AlpsClipEffectSet set, AlpsEffectKind kind, AlpsParity parity = AlpsParity.All)
        {
            return set.effects[set.IndexOf(kind, parity)];
        }

        [Test]
        public void SavedBlackout_MovesToTheMove()
        {
            var set = Load(Move(), Brightness);

            var move = Find(set, AlpsEffectKind.Move);
            Assert.IsTrue(move.blackoutOnReturn);
            Assert.AreEqual(0.1f, move.blackoutFadeIn, 0.0001f);
            Assert.AreEqual(0.3f, move.blackoutFadeOut, 0.0001f);
            Assert.IsFalse(Find(set, AlpsEffectKind.Brightness).blackoutOnReturn, "The brightness effect no longer carries it.");
        }

        [Test]
        public void SavedBlackout_MovesToBothHalvesOfASplitMove()
        {
            var set = Load(Move(1), Move(2, mode: 1), Brightness);

            Assert.IsTrue(Find(set, AlpsEffectKind.Move, AlpsParity.Even).blackoutOnReturn);
            Assert.IsTrue(Find(set, AlpsEffectKind.Move, AlpsParity.Odd).blackoutOnReturn, "A circle follows the clip's phase too.");
            Assert.IsFalse(Find(set, AlpsEffectKind.Brightness).blackoutOnReturn);
        }

        [Test]
        public void SavedBlackout_StaysWhereAMoveWouldReturnOnAnotherPhase()
        {
            Assert.IsTrue(Find(Load(Brightness), AlpsEffectKind.Brightness).blackoutOnReturn, "No move to take it.");
            Assert.IsTrue(Find(Load(Move(1), Brightness), AlpsEffectKind.Brightness).blackoutOnReturn, "Half the fixtures have no move.");
            Assert.IsTrue(Find(Load(Move(tiltOwnPhase: true), Brightness), AlpsEffectKind.Brightness).blackoutOnReturn, "Tilt runs on its own phase.");
            Assert.IsTrue(Find(Load(Move(phaseOffset: 0.5f), Brightness), AlpsEffectKind.Brightness).blackoutOnReturn, "The move runs late.");
            Assert.IsTrue(Find(Load(Move(mode: 2), Brightness), AlpsEffectKind.Brightness).blackoutOnReturn, "A tracking head has no return.");

            var kept = Load(Move(1), Brightness);
            Assert.IsFalse(Find(kept, AlpsEffectKind.Move, AlpsParity.Even).blackoutOnReturn, "Nothing is moved part way.");
        }

        [Test]
        public void SavedMoveBlackout_IsNotMovedAgain()
        {
            var set = new AlpsClipEffectSet();
            set.Add(AlpsEffectKind.Move).blackoutOnReturn = true;
            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.blackoutOnReturn = true;

            var reloaded = JsonUtility.FromJson<AlpsClipEffectSet>(JsonUtility.ToJson(set));

            Assert.IsTrue(Find(reloaded, AlpsEffectKind.Move).blackoutOnReturn);
            Assert.IsTrue(Find(reloaded, AlpsEffectKind.Brightness).blackoutOnReturn, "A set saved since keeps what it has.");
        }
    }
}
