using NUnit.Framework;
using UnityEngine;
using AdzukiSoft.ALPS.Editor;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// The color strip the Timeline window draws on ALPS clips. Tempo is 60 BPM, so one
    /// beat is one second, and every clip here cycles every 2 beats.
    /// </summary>
    public class AlpsClipStripTests
    {
        private const float Bpm = 60f;

        private static AlpsClipEffectSet NewSet()
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.Forward;
            set.phase.ease = AlpsEaseType.Linear;
            set.phase.beatsPerCycle = 2f;
            return set;
        }

        private static Color At(AlpsClipStrip strip, int lane, float time, float start, float end)
        {
            var i = Mathf.FloorToInt((time - start) / (end - start) * strip.width);
            return strip.pixels[lane * strip.width + i];
        }

        private static void AssertColor(Color expected, Color actual, string message = null)
        {
            Assert.AreEqual(expected.r, actual.r, 0.01f, message);
            Assert.AreEqual(expected.g, actual.g, 0.01f, message);
            Assert.AreEqual(expected.b, actual.b, 0.01f, message);
            Assert.AreEqual(expected.a, actual.a, 0.01f, message);
        }

        [Test]
        public void Strip_StaticColorFillsTheWholeClip()
        {
            var set = NewSet();
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.red));

            var strip = AlpsClipStrip.Build(set, 4, Bpm, 0f, 4f, 0);

            Assert.IsTrue(strip.HasColor);
            Assert.AreEqual(1, strip.lanes);
            foreach (var pixel in strip.pixels)
            {
                AssertColor(Color.red, pixel);
            }
        }

        [Test]
        public void Strip_PaletteFollowsThePhaseOverTime()
        {
            var set = NewSet();
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(new AlpsColorStop(Color.blue));

            var strip = AlpsClipStrip.Build(set, 4, Bpm, 0f, 4f, 0);

            AssertColor(Color.red, At(strip, 0, 0.25f, 0f, 4f), "First half of the first cycle.");
            AssertColor(Color.blue, At(strip, 0, 1.25f, 0f, 4f), "Second half of the first cycle.");
            AssertColor(Color.red, At(strip, 0, 2.25f, 0f, 4f), "The palette starts over each cycle.");
        }

        [Test]
        public void Strip_BrightnessDimsTheBand()
        {
            var set = NewSet();
            set.Add(AlpsEffectKind.Color).colorStops.Add(new AlpsColorStop(Color.white));
            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.brightness.isRange = false;
            brightness.brightness.value = 50f;

            var strip = AlpsClipStrip.Build(set, 1, Bpm, 0f, 2f, 0);

            AssertColor(new Color(1f, 1f, 1f, 0.5f), At(strip, 0, 1f, 0f, 2f));
        }

        [Test]
        public void Strip_WithoutColorHasNoBandButStillMarksCycles()
        {
            var set = NewSet();
            set.Add(AlpsEffectKind.Color);

            var strip = AlpsClipStrip.Build(set, 4, Bpm, 1f, 7f, 0);

            Assert.IsFalse(strip.HasColor, "A palette without stops plays no color.");
            Assert.AreEqual(2f, strip.secondsPerCycle, 0.0001f);
            CollectionAssert.AreEqual(new[] { 2f, 4f }, strip.cycleTimes, "Beats count from the clip start at 1 s, so cycles start at 3 and 5 seconds.");
        }

        [Test]
        public void Strip_ClipTempoOverrideSetsTheCycleLength()
        {
            var set = NewSet();
            set.bpm = 120f;

            var strip = AlpsClipStrip.Build(set, 1, Bpm, 1.5f, 5f, 0);

            Assert.AreEqual(1f, strip.secondsPerCycle, 0.0001f);
            CollectionAssert.AreEqual(new[] { 1f, 2f, 3f }, strip.cycleTimes);
        }

        [Test]
        public void Strip_OddAndEvenColorsGetALaneEach()
        {
            var set = NewSet();
            var even = set.Add(AlpsEffectKind.Color);
            var odd = set.Add(AlpsEffectKind.Color);
            even.colorStops.Add(new AlpsColorStop(Color.blue));
            odd.colorStops.Add(new AlpsColorStop(Color.red));

            var strip = AlpsClipStrip.Build(set, 4, Bpm, 0f, 2f, 0);

            Assert.AreEqual(2, strip.lanes);
            AssertColor(Color.red, At(strip, 0, 1f, 0f, 2f), "Odd fixtures are on top.");
            AssertColor(Color.blue, At(strip, 1, 1f, 0f, 2f));
            Assert.AreEqual(1, AlpsClipStrip.Build(set, 1, Bpm, 0f, 2f, 0).lanes, "A single fixture is always odd.");
        }
    }
}
