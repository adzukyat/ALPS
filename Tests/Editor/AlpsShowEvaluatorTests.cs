using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Level 1: the evaluator's math on compiled arrays, with no timeline or scene involved.
    /// Tempo is 60 BPM throughout, so one beat is one second.
    /// </summary>
    public class AlpsShowEvaluatorTests
    {
        private const float Bpm = 60f;

        // ------------------------------------------------------------------ order

        [Test]
        public void Order_NormalGroupsNeighboursBeforeCounting()
        {
            int Position(int fixture) => AlpsShowEvaluator.OrderPosition(AlpsShowEvaluator.OrderNormal, 0, fixture, 6, 2);

            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1, 2, 2 }, Enumerable.Range(0, 6).Select(Position).ToArray());
        }

        [Test]
        public void Order_SymmetricCountsOutwardFromTheCentre()
        {
            int Position(int fixture, int count) => AlpsShowEvaluator.OrderPosition(AlpsShowEvaluator.OrderSymmetric, 0, fixture, count, 1);

            CollectionAssert.AreEqual(new[] { 2, 1, 0, 1, 2 }, Enumerable.Range(0, 5).Select(i => Position(i, 5)).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 0, 0, 1 }, Enumerable.Range(0, 4).Select(i => Position(i, 4)).ToArray());
        }

        [Test]
        public void Order_ReverseCountsFromTheLastGroup()
        {
            int Position(int fixture, int groupSize) => AlpsShowEvaluator.OrderPosition(AlpsShowEvaluator.OrderReverse, 0, fixture, 6, groupSize);

            CollectionAssert.AreEqual(new[] { 5, 4, 3, 2, 1, 0 }, Enumerable.Range(0, 6).Select(i => Position(i, 1)).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 2, 1, 1, 0, 0 }, Enumerable.Range(0, 6).Select(i => Position(i, 2)).ToArray());
            Assert.IsFalse(AlpsShowEvaluator.IsMirrored(AlpsShowEvaluator.OrderReverse, 0, 6, 1), "Only symmetric mirrors pan.");
        }

        [Test]
        public void Order_SymmetricMirrorsOnlyTheFirstHalf()
        {
            bool Mirrored(int fixture, int count) => AlpsShowEvaluator.IsMirrored(AlpsShowEvaluator.OrderSymmetric, fixture, count, 1);

            CollectionAssert.AreEqual(
                new[] { true, true, false, false, false },
                Enumerable.Range(0, 5).Select(i => Mirrored(i, 5)).ToArray(),
                "The middle fixture stays unmirrored.");
            CollectionAssert.AreEqual(new[] { true, true, false, false }, Enumerable.Range(0, 4).Select(i => Mirrored(i, 4)).ToArray());
            Assert.IsFalse(AlpsShowEvaluator.IsMirrored(AlpsShowEvaluator.OrderNormal, 0, 5, 1));
        }

        [Test]
        public void Order_RandomIsAPermutationThatDependsOnTheSeed()
        {
            int[] Positions(int seed) => Enumerable.Range(0, 8)
                .Select(i => AlpsShowEvaluator.OrderPosition(AlpsShowEvaluator.OrderRandom, seed, i, 8, 1))
                .ToArray();

            var first = Positions(11);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 8).ToArray(), first, "Every position is used exactly once.");
            CollectionAssert.AreEqual(first, Positions(11), "The same seed gives the same order.");
            CollectionAssert.AreNotEqual(first, Positions(12), "A different seed shuffles differently.");
        }

        // ------------------------------------------------------------------ phase

        private static float Wave(float rise, float holdHigh, float fall, float cycles, bool inverse = false, AlpsEaseType ease = AlpsEaseType.Linear)
        {
            return AlpsShowEvaluator.Phase(AlpsShowEvaluator.PhaseWave, (int)ease, rise, holdHigh, fall, inverse, cycles, 0, 0);
        }

        [Test]
        public void Phase_RiseOverTheWholeCycleIsASawtoothAndInverseFlipsIt()
        {
            Assert.AreEqual(0.25f, Wave(1f, 0f, 0f, 0.25f), 0.001f);
            Assert.AreEqual(0.25f, Wave(1f, 0f, 0f, 3.25f), 0.001f, "The phase wraps every cycle.");
            Assert.AreEqual(0.75f, Wave(1f, 0f, 0f, 0.25f, inverse: true), 0.001f);
        }

        [Test]
        public void Phase_EaseShapesTheCycle()
        {
            Assert.AreEqual(0.25f, Wave(1f, 0f, 0f, 0.5f, ease: AlpsEaseType.InQuad), 0.001f);
        }

        [Test]
        public void Phase_TheFallRunsTheRiseEaseBackwards()
        {
            var up = Wave(0.5f, 0f, 0.5f, 0.1f, ease: AlpsEaseType.InQuad);
            var down = Wave(0.5f, 0f, 0.5f, 0.9f, ease: AlpsEaseType.InQuad);
            Assert.AreEqual(0.04f, up, 0.001f);
            Assert.AreEqual(up, down, 0.001f, "The same distance from the bottom reads the same on both sides.");
        }

        [Test]
        public void Phase_RandomStaysInRangeAndIsDeterministic()
        {
            for (var i = 0; i < 50; i++)
            {
                var cycles = i * 0.137f;
                var a = AlpsShowEvaluator.Phase(AlpsShowEvaluator.PhaseRandom, 0, 0.5f, 0f, 0.5f, false, cycles, 3, 9);
                var b = AlpsShowEvaluator.Phase(AlpsShowEvaluator.PhaseRandom, 0, 0.5f, 0f, 0.5f, false, cycles, 3, 9);
                Assert.That(a, Is.InRange(0f, 1f));
                Assert.AreEqual(a, b);
            }
        }

        [Test]
        public void Phase_WaveHoldsAtTheTopAndAtTheBottom()
        {
            // 25% up, 25% held high, 25% down, 25% held low.
            float Quarters(float cycles) => Wave(0.25f, 0.25f, 0.25f, cycles);

            Assert.AreEqual(0.5f, Quarters(0.125f), 0.001f, "Half way up.");
            Assert.AreEqual(1f, Quarters(0.3f), 0.001f, "Held at the top.");
            Assert.AreEqual(0.5f, Quarters(0.625f), 0.001f, "Half way down.");
            Assert.AreEqual(0f, Quarters(0.8f), 0.001f, "Held at the bottom.");
            Assert.AreEqual(0f, Quarters(0.99f), 0.001f, "Still at the bottom as the cycle ends.");
        }

        [Test]
        public void Phase_WithoutHoldsIsATriangle()
        {
            Assert.AreEqual(1f, Wave(0.25f, 0f, 0.75f, 0.25f), 0.001f, "The peak sits where the rise ends.");
            Assert.AreEqual(0.5f, Wave(0.25f, 0f, 0.75f, 0.625f), 0.001f);
        }

        [Test]
        public void Phase_ZeroLengthRampsMakeASquareWave()
        {
            Assert.AreEqual(1f, Wave(0f, 0.5f, 0f, 0f), 0.001f, "A rise of zero jumps straight up.");
            Assert.AreEqual(1f, Wave(0f, 0.5f, 0f, 0.49f), 0.001f);
            Assert.AreEqual(0f, Wave(0f, 0.5f, 0f, 0.5f), 0.001f, "A fall of zero drops straight down.");
        }

        [Test]
        public void Phase_ReturnLegIsTheFallAndTheLowHold()
        {
            Assert.IsFalse(AlpsShowEvaluator.IsReturnLeg(AlpsShowEvaluator.PhaseWave, 0.25f, 0.25f, 0.45f), "Still at the top.");
            Assert.IsTrue(AlpsShowEvaluator.IsReturnLeg(AlpsShowEvaluator.PhaseWave, 0.25f, 0.25f, 0.55f), "Falling.");
            Assert.IsTrue(AlpsShowEvaluator.IsReturnLeg(AlpsShowEvaluator.PhaseWave, 0.25f, 0.25f, 0.9f), "Waiting at the bottom.");
            Assert.IsFalse(AlpsShowEvaluator.IsReturnLeg(AlpsShowEvaluator.PhaseRandom, 0.25f, 0.25f, 0.9f), "Only a wave has a return leg.");
        }

        [Test]
        public void Phase_AWaveThatNeverFallsNeverReturns()
        {
            Assert.IsFalse(AlpsShowEvaluator.IsReturnLeg(AlpsShowEvaluator.PhaseWave, 1f, 0f, 0.99f), "A sawtooth drops but does not return.");
            Assert.IsFalse(AlpsShowEvaluator.IsReturnLeg(AlpsShowEvaluator.PhaseWave, 0.5f, 0.5f, 0.99f));
        }

        // ------------------------------------------------------------------ values

        [Test]
        public void Brightness_StaticValueReachesEveryFixture()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Brightness).brightness.value = 40f;
            var show = Compile(4, set);

            for (var fixture = 0; fixture < 4; fixture++)
            {
                Assert.AreEqual(40f, Evaluate(show, fixture, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f);
            }
        }

        [Test]
        public void Range_WithinCycleFollowsThePhase()
        {
            var set = Set();
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            var show = Compile(1, set);

            Assert.AreEqual(25f, Evaluate(show, 0, 0.25f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(75f, Evaluate(show, 0, 0.75f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void Tempo_BeatsCountFromTheClipStart()
        {
            var set = Set();
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            AlpsCompiledShow CompileFromHalfSecond() => AlpsShowCompiler.CompileStandalone(
                1, Bpm, new AlpsStandaloneClip { set = set, start = 0.5f, end = 2f });

            var show = CompileFromHalfSecond();
            Assert.AreEqual(0f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "The clip starts on its own first beat.");
            Assert.AreEqual(12.5f, Evaluate(show, 0, 0.625f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "An eighth of a beat at the show's 60 BPM after the clip starts.");

            set.bpm = 120f;
            var overridden = CompileFromHalfSecond();
            Assert.AreEqual(25f, Evaluate(overridden, 0, 0.625f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "A quarter beat at the clip's own 120 BPM.");
            Assert.AreEqual(75f, Evaluate(overridden, 0, 0.875f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void Tempo_EachClipOnALayerStartsItsOwnBeat()
        {
            var a = Set();
            var first = a.Add(AlpsEffectKind.Brightness).brightness;
            first.isRange = true;
            first.range = new Vector2(0f, 100f);
            var b = new AlpsClipEffectSet(a);

            var show = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = a, start = 0f, end = 1.3f },
                new AlpsStandaloneClip { set = b, start = 1.3f, end = 3f });

            Assert.AreEqual(20f, Evaluate(show, 0, 1.2f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "The first clip is 1.2 beats in.");
            Assert.AreEqual(10f, Evaluate(show, 0, 1.4f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "The next clip starts over at its own start.");
        }

        [Test]
        public void ClipFade_RisesAndFallsOverBeats()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Brightness).brightness.value = 80f;
            set.fadeInBeats = 1f;
            set.fadeOutBeats = 2f;
            var show = Compile(1, set, end: 4f);

            Assert.AreEqual(0f, Evaluate(show, 0, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Dark on the first beat.");
            Assert.AreEqual(40f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Half way through the fade in.");
            Assert.AreEqual(80f, Evaluate(show, 0, 1.5f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Between the fades.");
            Assert.AreEqual(40f, Evaluate(show, 0, 3f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Half way through the fade out.");
            Assert.AreEqual(0f, Evaluate(show, 0, 4f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Dark at the end.");
        }

        [Test]
        public void ClipFade_CountsInTheClipTempo()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Brightness).brightness.value = 100f;
            set.bpm = 120f;
            set.fadeInBeats = 2f;
            var show = Compile(1, set, end: 4f);

            Assert.AreEqual(50f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "One of two beats at 120 BPM.");
        }

        [Test]
        public void ClipFade_BlendsEveryChannelLikeAnEmptyClip()
        {
            var set = Set();
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.value = 60f;
            move.pan.value = 90f;
            set.fadeInBeats = 1f;
            set.fadeOutBeats = 1f;
            var show = Compile(1, set, end: 4f);

            var entering = Evaluate(show, 0, 0.5f);
            Assert.AreEqual(30f, entering[AlpsShowEvaluator.FrameTilt], 0.01f, "Half way from the default tilt of 0.");
            Assert.AreEqual(45f, entering[AlpsShowEvaluator.FramePan], 0.01f, "Half way from the default pan of 0.");
            Assert.AreEqual(60f, Evaluate(show, 0, 2f)[AlpsShowEvaluator.FrameTilt], 0.01f);
            Assert.AreEqual(15f, Evaluate(show, 0, 3.75f)[AlpsShowEvaluator.FrameTilt], 0.01f, "A quarter of the fade out left.");
        }

        [Test]
        public void ClipFade_BlendsTowardTheLayerBelow()
        {
            var below = Set();
            below.Add(AlpsEffectKind.Brightness).brightness.value = 100f;
            var above = Set();
            above.Add(AlpsEffectKind.Brightness).brightness.value = 20f;
            above.fadeInBeats = 2f;
            var show = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = below, start = 0f, end = 4f, layer = 0 },
                new AlpsStandaloneClip { set = above, start = 0f, end = 4f, layer = 1 });

            Assert.AreEqual(60f, Evaluate(show, 0, 1f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Half way from the lower layer's 100 to 20.");
        }

        [Test]
        public void Crossfade_IntoAClipWithoutBrightnessGoesDark()
        {
            var lit = Set();
            lit.Add(AlpsEffectKind.Brightness).brightness.value = 100f;
            var plain = Set();
            plain.Add(AlpsEffectKind.Cone);
            var show = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = lit, start = 0f, end = 2f, mixOut = 1f },
                new AlpsStandaloneClip { set = plain, start = 1f, end = 3f, mixIn = 1f });

            Assert.AreEqual(50f, Evaluate(show, 0, 1.5f)[AlpsShowEvaluator.FrameBrightness], 0.5f, "Half way from 100 to dark.");
        }

        [Test]
        public void NoClip_LeavesTheFixtureDark()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Cone);
            var show = AlpsShowCompiler.CompileStandalone(1, Bpm, new AlpsStandaloneClip { set = set, start = 1f, end = 2f });

            Assert.AreEqual(0f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f, "Before the clip.");
            Assert.AreEqual(0f, Evaluate(show, 0, 1.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f, "Inside a clip without a brightness effect.");
            Assert.AreEqual(0f, Evaluate(show, 0, 2.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f, "After the clip.");
        }

        [Test]
        public void Range_PerCycleAlternatesMinAndMax()
        {
            var set = Set();
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(10f, 90f);
            brightness.timing = AlpsTimingMode.PerCycle;
            var show = Compile(1, set, end: 4f);

            Assert.AreEqual(10f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f);
            Assert.AreEqual(90f, Evaluate(show, 0, 1.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f);
            Assert.AreEqual(10f, Evaluate(show, 0, 2.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f);
        }

        [Test]
        public void Spread_LagsEachOrderPosition()
        {
            var set = Set();
            // Three fixtures over three quarters of a cycle, so a quarter each.
            set.phase.spread = 0.75f;
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            var show = Compile(3, set);

            Assert.AreEqual(50f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(25f, Evaluate(show, 1, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(0f, Evaluate(show, 2, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void Spread_AddsAPerFixtureOffset()
        {
            var set = Set();
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.value = 10f;
            cone.coneWidth.hasSpread = true;
            cone.coneWidth.spread = 5f;
            var show = Compile(4, set);

            var widths = Enumerable.Range(0, 4).Select(i => Evaluate(show, i, 0.1f)[AlpsShowEvaluator.FrameConeWidth]).ToArray();
            CollectionAssert.AreEqual(new[] { 10f, 15f, 20f, 25f }, widths);
        }

        [Test]
        public void SpreadRange_FollowsThePhaseLikeAValueRange()
        {
            var set = Set();
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneLength.value = 0f;
            cone.coneLength.hasSpread = true;
            cone.coneLength.isRange = true;
            cone.coneLength.spreadRange = new Vector2(0f, 10f);
            var show = Compile(4, set);

            float[] Lengths(float time) => Enumerable.Range(0, 4).Select(i => Evaluate(show, i, time)[AlpsShowEvaluator.FrameConeLength]).ToArray();

            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f, 0f }, Lengths(0f), "At the start of the cycle the fan is closed.");
            var half = Lengths(0.5f);
            for (var i = 0; i < 4; i++)
            {
                Assert.AreEqual(5f * i, half[i], 0.01f, "Half way through, each position adds half the spread range.");
            }

            cone.coneLength.timing = AlpsTimingMode.PerCycle;
            show = Compile(4, set);
            Assert.AreEqual(0f, Lengths(0.5f)[3], 0.01f, "Per cycle holds the first end for the whole first cycle.");
            Assert.AreEqual(30f, Lengths(1.5f)[3], 0.01f, "And the other end for the next one.");
        }

        [Test]
        public void Spread_KeepsTheValueAsAFixedOffsetAndSetsTheValueRangeAside()
        {
            var set = Set();
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneLength.value = 3f;
            cone.coneLength.range = new Vector2(0f, 8f);
            cone.coneLength.spread = 2f;
            cone.coneLength.spreadRange = new Vector2(0f, 10f);
            cone.coneLength.hasSpread = true;
            var show = Compile(2, set);

            Assert.AreEqual(3f + 2f, Evaluate(show, 1, 0.5f)[AlpsShowEvaluator.FrameConeLength], 0.01f, "Without R the spread is fixed on top of the offset.");

            // R now ranges the spread, the offset stays put and the value range is ignored.
            cone.coneLength.isRange = true;
            show = Compile(2, set);
            Assert.AreEqual(3f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameConeLength], 0.01f);
            Assert.AreEqual(3f + 5f, Evaluate(show, 1, 0.5f)[AlpsShowEvaluator.FrameConeLength], 0.01f);

            // The spread range takes the row's own phase like any range.
            cone.coneLength.useOwnPhase = true;
            cone.coneLength.ownPhase.SetShares(1f, 0f, 0f);
            cone.coneLength.ownPhase.ease = AlpsEaseType.Linear;
            cone.coneLength.ownPhase.beatsPerCycle = 2f;
            show = Compile(2, set);
            Assert.AreEqual(3f + 2.5f, Evaluate(show, 1, 0.5f)[AlpsShowEvaluator.FrameConeLength], 0.01f, "A quarter of the own cycle opens a quarter of the spread range.");

            // Turning spread off brings the value range back and drops the spread.
            cone.coneLength.hasSpread = false;
            cone.coneLength.useOwnPhase = false;
            show = Compile(2, set);
            Assert.AreEqual(4f, Evaluate(show, 1, 0.5f)[AlpsShowEvaluator.FrameConeLength], 0.01f);
        }

        [Test]
        public void SpreadRange_ReverseOrderFansFromTheOtherEnd()
        {
            var set = Set();
            set.order = AlpsOrderMode.Reverse;
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.value = 0f;
            cone.coneWidth.hasSpread = true;
            cone.coneWidth.spread = 10f;
            var show = Compile(4, set);

            var widths = Enumerable.Range(0, 4).Select(i => Evaluate(show, i, 0.1f)[AlpsShowEvaluator.FrameConeWidth]).ToArray();
            CollectionAssert.AreEqual(new[] { 30f, 20f, 10f, 0f }, widths);
        }

        [Test]
        public void Spread_CanBeNegative()
        {
            var set = Set();
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.value = 30f;
            cone.coneWidth.hasSpread = true;
            cone.coneWidth.spread = -5f;
            var show = Compile(4, set);

            var widths = Enumerable.Range(0, 4).Select(i => Evaluate(show, i, 0.1f)[AlpsShowEvaluator.FrameConeWidth]).ToArray();
            CollectionAssert.AreEqual(new[] { 30f, 25f, 20f, 15f }, widths);
        }

        [Test]
        public void Move_SymmetricPanSpreadFansOut()
        {
            var set = Set();
            set.order = AlpsOrderMode.Symmetric;
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.value = 10f;
            move.tilt.hasSpread = true;
            move.tilt.spread = 5f;
            move.pan.hasSpread = true;
            move.pan.spread = 20f;
            var show = Compile(5, set);

            float[] Channel(int channel) => Enumerable.Range(0, 5).Select(i => Evaluate(show, i, 0.1f)[channel]).ToArray();

            CollectionAssert.AreEqual(new[] { -40f, -20f, 0f, 20f, 40f }, Channel(AlpsShowEvaluator.FramePan), "Pan mirrors into a fan.");
            CollectionAssert.AreEqual(new[] { 20f, 15f, 10f, 15f, 20f }, Channel(AlpsShowEvaluator.FrameTilt), "Tilt has no left and right.");

            move.pan.spread = -20f;
            show = Compile(5, set);
            CollectionAssert.AreEqual(new[] { 40f, 20f, 0f, -20f, -40f }, Channel(AlpsShowEvaluator.FramePan), "Negative spread crosses the fan.");
        }

        [Test]
        public void Move_SymmetricMirrorsTheWholePanSweep()
        {
            var set = Set();
            set.order = AlpsOrderMode.Symmetric;
            var move = set.Add(AlpsEffectKind.Move);
            move.pan.isRange = true;
            move.pan.range = new Vector2(0f, 60f);
            var show = Compile(4, set);

            for (var i = 0; i < 8; i++)
            {
                var time = i / 8f;
                Assert.AreEqual(
                    -Evaluate(show, 3, time)[AlpsShowEvaluator.FramePan],
                    Evaluate(show, 0, time)[AlpsShowEvaluator.FramePan],
                    0.001f,
                    "The edges sweep as mirror images.");
            }
        }

        [Test]
        public void Spread_NegativeRunsTheOrderBackwards()
        {
            Assert.AreEqual(0.25f, AlpsShowEvaluator.FixtureCycles(0f, 1f, -0.25f, 1, 0f), 0.0001f);
            Assert.AreEqual(-0.25f, AlpsShowEvaluator.FixtureCycles(0f, 1f, 0.25f, 1, 0f), 0.0001f);

            var set = Set();
            set.order = AlpsOrderMode.Symmetric;
            // Symmetric reaches from the middle to the edge, three positions over five
            // fixtures, so a quarter cycle each again.
            set.phase.spread = -0.75f;
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            var show = Compile(5, set);

            Assert.AreEqual(50f, Evaluate(show, 0, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "The edges lead.");
            Assert.AreEqual(0f, Evaluate(show, 2, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "The centre trails.");
        }

        [Test]
        public void Spread_KeepsTheLookWhateverTheFixtureCount()
        {
            var set = Set();
            // One cycle over the whole group, the value a chase is authored with.
            set.phase.spread = 1f;
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);

            var four = Compile(4, set);
            var eight = Compile(8, set);

            Assert.AreEqual(50f, Evaluate(four, 2, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(
                50f,
                Evaluate(eight, 4, 0f)[AlpsShowEvaluator.FrameBrightness],
                0.01f,
                "Half way along the group is half way through the cycle, whatever the count.");

            Assert.AreEqual(75f, Evaluate(four, 1, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(75f, Evaluate(eight, 2, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f);

            Assert.AreEqual(
                0.25f,
                AlpsShowEvaluator.DelayFromSpread(1f, AlpsShowEvaluator.OrderNormal, 4, 1),
                0.0001f,
                "A full spread over four positions steps a quarter cycle each.");
            Assert.AreEqual(
                1f / 3f,
                AlpsShowEvaluator.DelayFromSpread(1f, AlpsShowEvaluator.OrderSymmetric, 5, 1),
                0.0001f,
                "Symmetric only reaches from the middle to the edge.");
        }

        [Test]
        public void Spread_InBeatsHoldsWhenTheSpeedChanges()
        {
            var set = Set();
            set.phase.beatsPerCycle = 2f;
            set.phase.spreadInBeats = true;
            set.phase.spreadBeats = 2f;
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);

            // Two beats over a two beat cycle is the full trip, a quarter each over four.
            Assert.AreEqual(75f, Evaluate(Compile(4, set), 1, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f);

            // Doubling the speed keeps the two beats, which is now half the trip.
            set.phase.beatsPerCycle = 4f;
            Assert.AreEqual(87.5f, Evaluate(Compile(4, set), 1, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f);

            // Without a speed there are no beats, so the delay falls back to its share of the cycle.
            set.phase.beatsPerCycle = 0f;
            set.phase.spread = 1f;
            Assert.AreEqual(1f, set.phase.SpreadCycles, 0.0001f);
        }

        [Test]
        public void Spread_DividesAnOwnPhaseByTheClipGrouping()
        {
            var set = Set();
            // The order position comes from the clip, so an own phase is divided by the
            // clip's grouping: eight fixtures in pairs are four positions.
            set.phase.fixtureGroupSize = 2;
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            brightness.useOwnPhase = true;
            brightness.ownPhase.SetShares(1f, 0f, 0f);
            brightness.ownPhase.ease = AlpsEaseType.Linear;
            brightness.ownPhase.beatsPerCycle = 1f;
            brightness.ownPhase.fixtureGroupSize = 1;
            brightness.ownPhase.spread = 1f;
            var show = Compile(8, set);

            Assert.AreEqual(0f, Evaluate(show, 1, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "The first pair starts the cycle.");
            Assert.AreEqual(75f, Evaluate(show, 2, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(50f, Evaluate(show, 4, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(25f, Evaluate(show, 6, 0f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void OwnPhase_IgnoresTheSharedSettings()
        {
            var set = Set();
            set.phase.beatsPerCycle = 4f;
            var brightness = set.Add(AlpsEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            brightness.useOwnPhase = true;
            brightness.ownPhase.SetShares(1f, 0f, 0f);
            brightness.ownPhase.ease = AlpsEaseType.Linear;
            brightness.ownPhase.beatsPerCycle = 1f;
            var show = Compile(1, set);

            Assert.AreEqual(50f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void Parity_OddCountsFromTheFirstFixture()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Brightness).brightness.value = 20f;
            set.Add(AlpsEffectKind.Brightness).brightness.value = 80f;
            Assert.AreEqual(AlpsParity.Even, set.effects[0].parity);
            Assert.AreEqual(AlpsParity.Odd, set.effects[1].parity);
            var show = Compile(4, set);

            Assert.AreEqual(80f, Evaluate(show, 0, 0.1f)[AlpsShowEvaluator.FrameBrightness], 0.001f, "Fixture 1 of 4 is odd.");
            Assert.AreEqual(20f, Evaluate(show, 1, 0.1f)[AlpsShowEvaluator.FrameBrightness], 0.001f);
            Assert.AreEqual(80f, Evaluate(show, 2, 0.1f)[AlpsShowEvaluator.FrameBrightness], 0.001f);
        }

        [Test]
        public void BlackoutOnReturn_MutesBrightnessOnTheReturnLeg()
        {
            var set = Set(0.5f, 0f, 0.5f);
            var effect = set.Add(AlpsEffectKind.Brightness);
            effect.brightness.isRange = true;
            effect.brightness.range = new Vector2(50f, 100f);
            effect.blackoutOnReturn = true;
            var show = Compile(1, set);

            Assert.AreEqual(75f, Evaluate(show, 0, 0.25f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(0f, Evaluate(show, 0, 0.75f)[AlpsShowEvaluator.FrameBrightness], 0.001f);
        }

        [Test]
        public void BlackoutOnReturn_FadesInAndOutInsideTheOutboundLeg()
        {
            // One beat per cycle, half of it outbound: the outbound leg is 0 to 0.5.
            var set = Set(0.5f, 0f, 0.5f);
            var effect = set.Add(AlpsEffectKind.Brightness);
            effect.brightness.value = 100f;
            effect.blackoutOnReturn = true;
            effect.blackoutFadeIn = 0.25f;
            effect.blackoutFadeOut = 0.5f;
            var show = Compile(1, set);

            Assert.AreEqual(50f, Evaluate(show, 0, 0.0625f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Half way through the fade in.");
            Assert.AreEqual(100f, Evaluate(show, 0, 0.25f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Between the two fades.");
            Assert.AreEqual(50f, Evaluate(show, 0, 0.375f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Half way through the fade out.");
            Assert.AreEqual(0f, Evaluate(show, 0, 0.75f)[AlpsShowEvaluator.FrameBrightness], 0.001f, "Dark on the return leg.");
        }

        [Test]
        public void BlackoutOnReturn_StaysLitThroughTheHold()
        {
            // 25% up, 25% held, 50% down: the lit leg is 0 to 0.5 and the fade out takes its second half.
            var set = Set(0.25f, 0.25f, 0.5f);
            var effect = set.Add(AlpsEffectKind.Brightness);
            effect.brightness.value = 100f;
            effect.blackoutOnReturn = true;
            effect.blackoutFadeOut = 0.5f;
            var show = Compile(1, set);

            Assert.AreEqual(100f, Evaluate(show, 0, 0.2f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Lit while going out.");
            Assert.AreEqual(50f, Evaluate(show, 0, 0.375f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Fading out during the hold.");
            Assert.AreEqual(0f, Evaluate(show, 0, 0.6f)[AlpsShowEvaluator.FrameBrightness], 0.001f, "Dark on the way back.");
        }

        [Test]
        public void BlackoutOnReturn_StaysDarkWhileWaitingAtTheBottom()
        {
            var set = Set(0.25f, 0.25f, 0.25f);
            var effect = set.Add(AlpsEffectKind.Brightness);
            effect.brightness.value = 100f;
            effect.blackoutOnReturn = true;
            var show = Compile(1, set);

            Assert.AreEqual(100f, Evaluate(show, 0, 0.4f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Lit at the top.");
            Assert.AreEqual(0f, Evaluate(show, 0, 0.9f)[AlpsShowEvaluator.FrameBrightness], 0.001f, "Dark at the bottom.");
            Assert.AreEqual(100f, Evaluate(show, 0, 1.1f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Lit again on the next rise.");
        }

        [Test]
        public void BlackoutOnReturn_FollowsTheOwnPhaseOfBrightness()
        {
            // The clip's wave comes back, but brightness runs on its own sawtooth with no return leg.
            var set = Set(0.5f, 0f, 0.5f);
            var effect = set.Add(AlpsEffectKind.Brightness);
            effect.brightness.isRange = true;
            effect.brightness.range = new Vector2(50f, 100f);
            effect.brightness.useOwnPhase = true;
            effect.brightness.ownPhase.SetShares(1f, 0f, 0f);
            effect.brightness.ownPhase.ease = AlpsEaseType.Linear;
            effect.brightness.ownPhase.beatsPerCycle = 1f;
            effect.blackoutOnReturn = true;
            var show = Compile(1, set);

            Assert.AreEqual(87.5f, Evaluate(show, 0, 0.75f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void PhaseOffset_LetsOddAndEvenTakeTurns()
        {
            // A blink with short fades: 10% up, 40% on, 10% down, 40% off. The odd card runs
            // half a cycle late, so one side is on while the other is off.
            var set = Set(0.1f, 0.4f, 0.1f);
            foreach (var parity in new[] { AlpsParity.Even, AlpsParity.Odd })
            {
                var effect = set.Add(AlpsEffectKind.Brightness);
                effect.brightness.isRange = true;
                effect.brightness.range = new Vector2(0f, 100f);
            }

            set.effects.Single(e => e.parity == AlpsParity.Odd).phaseOffset = 0.5f;
            var show = Compile(4, set);

            // Fixture index 0 is fixture 1, which is odd.
            Assert.AreEqual(100f, Evaluate(show, 1, 0.3f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Even is on.");
            Assert.AreEqual(0f, Evaluate(show, 0, 0.3f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Odd is off meanwhile.");
            Assert.AreEqual(0f, Evaluate(show, 1, 0.8f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Then even is off.");
            Assert.AreEqual(100f, Evaluate(show, 0, 0.8f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "And odd is on.");
            Assert.AreEqual(50f, Evaluate(show, 1, 0.55f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "Even half way through its fade out.");
            Assert.AreEqual(50f, Evaluate(show, 0, 0.55f)[AlpsShowEvaluator.FrameBrightness], 0.01f, "As odd is half way through its fade in.");
        }

        [Test]
        public void PhaseOffset_RunsTheWholeCardLate()
        {
            var set = Set();
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.isRange = true;
            move.tilt.range = new Vector2(-40f, 40f);
            move.phaseOffset = 0.25f;

            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(new AlpsColorStop(Color.blue));
            color.phaseOffset = 0.5f;
            var show = Compile(1, set);

            var frame = Evaluate(show, 0, 0.5f);
            Assert.AreEqual(-20f, frame[AlpsShowEvaluator.FrameTilt], 0.01f, "Half a cycle in, tilt is where it was a quarter in.");
            Assert.AreEqual(1f, frame[AlpsShowEvaluator.FrameRed], 0.01f, "The palette is still on its first colour.");
        }

        [Test]
        public void PhaseOffset_MovesTheReturnLegWithTheCard()
        {
            var set = Set(0.5f, 0f, 0.5f);
            var effect = set.Add(AlpsEffectKind.Brightness);
            effect.brightness.value = 100f;
            effect.blackoutOnReturn = true;
            effect.phaseOffset = 0.5f;
            var show = Compile(1, set);

            Assert.AreEqual(0f, Evaluate(show, 0, 0.25f)[AlpsShowEvaluator.FrameBrightness], 0.001f, "Half a cycle late, the card is still on its way back.");
            Assert.AreEqual(100f, Evaluate(show, 0, 0.75f)[AlpsShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void Move_PhaseOffsetShiftsPanAgainstTilt()
        {
            var set = Set();
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.isRange = true;
            move.tilt.range = new Vector2(0f, 100f);
            move.pan.isRange = true;
            move.pan.range = new Vector2(0f, 100f);
            move.panTiltPhaseOffsetDegrees = 90f;
            var show = Compile(1, set);

            var frame = Evaluate(show, 0, 0.25f);
            Assert.AreEqual(25f, frame[AlpsShowEvaluator.FrameTilt], 0.01f);
            Assert.AreEqual(50f, frame[AlpsShowEvaluator.FramePan], 0.01f, "90 degrees is a quarter cycle ahead.");
        }

        // ------------------------------------------------------------------ circle

        [Test]
        public void Circle_KeepsTheOpeningAngleWhereverTheCenterPoints()
        {
            foreach (var centerTilt in new[] { 0f, 30f, 60f, -25f })
            {
                var set = Set();
                var move = set.Add(AlpsEffectKind.Move);
                move.moveMode = AlpsMoveMode.Circle;
                move.circleCenterTilt.value = centerTilt;
                move.circleCenterPan.value = 40f;
                move.circleRadius.value = 12f;
                var show = Compile(1, set);

                for (var i = 0; i < 12; i++)
                {
                    var frame = Evaluate(show, 0, i / 12f);
                    var opening = Vector3.Angle(
                        Direction(frame[AlpsShowEvaluator.FrameTilt], frame[AlpsShowEvaluator.FramePan]),
                        Direction(centerTilt, 40f));
                    Assert.AreEqual(12f, opening, 0.01f, $"center tilt {centerTilt}, step {i} left the ring.");
                }
            }
        }

        [Test]
        public void Circle_WalksAllTheWayRoundOncePerCycle()
        {
            var set = Set();
            var move = set.Add(AlpsEffectKind.Move);
            move.moveMode = AlpsMoveMode.Circle;
            move.circleCenterTilt.value = 30f;
            move.circleRadius.value = 10f;
            var show = Compile(1, set);

            var start = Evaluate(show, 0, 0f);
            var quarter = Evaluate(show, 0, 0.25f);
            var half = Evaluate(show, 0, 0.5f);
            var full = Evaluate(show, 0, 1f);

            var center = Direction(30f, 0f);
            Assert.AreEqual(
                180f,
                Vector3.Angle(
                    Vector3.ProjectOnPlane(Direction(start[AlpsShowEvaluator.FrameTilt], start[AlpsShowEvaluator.FramePan]), center),
                    Vector3.ProjectOnPlane(Direction(half[AlpsShowEvaluator.FrameTilt], half[AlpsShowEvaluator.FramePan]), center)),
                0.05f,
                "Half a cycle is the opposite side of the ring.");
            Assert.AreEqual(
                90f,
                Vector3.Angle(
                    Vector3.ProjectOnPlane(Direction(start[AlpsShowEvaluator.FrameTilt], start[AlpsShowEvaluator.FramePan]), center),
                    Vector3.ProjectOnPlane(Direction(quarter[AlpsShowEvaluator.FrameTilt], quarter[AlpsShowEvaluator.FramePan]), center)),
                0.05f,
                "A quarter cycle is a quarter turn.");
            Assert.AreEqual(start[AlpsShowEvaluator.FrameTilt], full[AlpsShowEvaluator.FrameTilt], 0.01f);
            Assert.AreEqual(start[AlpsShowEvaluator.FramePan], full[AlpsShowEvaluator.FramePan], 0.01f);
        }

        [Test]
        public void Circle_RadiusZeroHoldsTheCenterAndAspectStretchesTheRing()
        {
            var set = Set();
            var move = set.Add(AlpsEffectKind.Move);
            move.moveMode = AlpsMoveMode.Circle;
            move.circleCenterTilt.value = 35f;
            move.circleCenterPan.value = -20f;
            move.circleRadius.value = 0f;
            var still = Evaluate(Compile(1, set), 0, 0.3f);
            Assert.AreEqual(35f, still[AlpsShowEvaluator.FrameTilt], 0.01f);
            Assert.AreEqual(-20f, still[AlpsShowEvaluator.FramePan], 0.01f);

            move.circleRadius.value = 10f;
            move.circleAspect = 2f;
            var show = Compile(1, set);
            var center = Direction(35f, -20f);
            var wide = Vector3.Angle(
                Direction(Evaluate(show, 0, 0f)[AlpsShowEvaluator.FrameTilt], Evaluate(show, 0, 0f)[AlpsShowEvaluator.FramePan]),
                center);
            var tall = Vector3.Angle(
                Direction(Evaluate(show, 0, 0.25f)[AlpsShowEvaluator.FrameTilt], Evaluate(show, 0, 0.25f)[AlpsShowEvaluator.FramePan]),
                center);
            Assert.AreEqual(20f, wide, 0.01f, "Width is the radius times the ratio.");
            Assert.AreEqual(10f, tall, 0.01f, "Height stays the radius.");
        }

        [Test]
        public void Circle_SpreadWalksTheFixturesRoundTheRing()
        {
            var set = Set();
            // A whole cycle over the group puts the four fixtures a quarter turn apart.
            set.phase.spread = 1f;
            var move = set.Add(AlpsEffectKind.Move);
            move.moveMode = AlpsMoveMode.Circle;
            move.circleCenterTilt.value = 30f;
            move.circleRadius.value = 10f;
            var show = Compile(4, set);

            var center = Direction(30f, 0f);
            Vector3 Spoke(int fixture)
            {
                var frame = Evaluate(show, fixture, 0f);
                return Vector3.ProjectOnPlane(
                    Direction(frame[AlpsShowEvaluator.FrameTilt], frame[AlpsShowEvaluator.FramePan]),
                    center);
            }

            Assert.AreEqual(90f, Vector3.Angle(Spoke(0), Spoke(1)), 0.05f);
            Assert.AreEqual(180f, Vector3.Angle(Spoke(0), Spoke(2)), 0.05f);
        }

        [Test]
        public void Circle_SymmetricMirrorsTheRings()
        {
            var set = Set();
            set.order = AlpsOrderMode.Symmetric;
            var move = set.Add(AlpsEffectKind.Move);
            move.moveMode = AlpsMoveMode.Circle;
            move.circleCenterTilt.value = 30f;
            move.circleCenterPan.hasSpread = true;
            move.circleCenterPan.spread = 25f;
            move.circleRadius.value = 8f;
            var show = Compile(4, set);

            for (var i = 0; i < 8; i++)
            {
                var time = i / 8f;
                var left = Evaluate(show, 0, time);
                var right = Evaluate(show, 3, time);
                Assert.AreEqual(right[AlpsShowEvaluator.FrameTilt], left[AlpsShowEvaluator.FrameTilt], 0.01f);
                Assert.AreEqual(-right[AlpsShowEvaluator.FramePan], left[AlpsShowEvaluator.FramePan], 0.01f, "The outer rings turn as mirror images.");
            }

            var edge = Evaluate(show, 3, 0.3f);
            Assert.AreEqual(
                8f,
                Vector3.Angle(Direction(edge[AlpsShowEvaluator.FrameTilt], edge[AlpsShowEvaluator.FramePan]), Direction(30f, 25f)),
                0.01f,
                "Spread moves the ring's centre, the ring keeps its size.");
        }

        [Test]
        public void Circle_RadiusRangeFollowsItsOwnPhase()
        {
            var set = Set();
            set.phase.beatsPerCycle = 1f;
            var move = set.Add(AlpsEffectKind.Move);
            move.moveMode = AlpsMoveMode.Circle;
            move.circleCenterTilt.value = 40f;
            move.circleRadius.isRange = true;
            move.circleRadius.range = new Vector2(0f, 20f);
            move.circleRadius.useOwnPhase = true;
            move.circleRadius.ownPhase.SetShares(1f, 0f, 0f);
            move.circleRadius.ownPhase.ease = AlpsEaseType.Linear;
            move.circleRadius.ownPhase.beatsPerCycle = 8f;
            var show = Compile(1, set, 10f);

            float Opening(float time)
            {
                var frame = Evaluate(show, 0, time);
                return Vector3.Angle(Direction(frame[AlpsShowEvaluator.FrameTilt], frame[AlpsShowEvaluator.FramePan]), Direction(40f, 0f));
            }

            Assert.AreEqual(5f, Opening(2f), 0.01f, "A quarter of the slow phase opens a quarter of the range.");
            Assert.AreEqual(10f, Opening(4f), 0.01f, "Half the slow phase opens half the range.");
        }

        /// <summary>The beam direction for a tilt and pan, matching the evaluator's frame.</summary>
        private static Vector3 Direction(float tilt, float pan)
        {
            return Quaternion.Euler(0f, pan, 0f) * (Quaternion.Euler(tilt, 0f, 0f) * Vector3.down);
        }

        // ------------------------------------------------------------------ palettes

        [Test]
        public void ColorPalette_EmptyIsOffAndLeavesTheDefault()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Color);
            var show = Compile(1, set);

            var frame = Evaluate(show, 0, 0.3f);
            Assert.AreEqual(1f, frame[AlpsShowEvaluator.FrameRed], 0.001f, "The neutral default is white.");
        }

        [Test]
        public void ColorPalette_WithinCycleStepsThroughTheStops()
        {
            var set = Set();
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(new AlpsColorStop(Color.green));
            color.colorStops.Add(new AlpsColorStop(Color.blue));
            var show = Compile(1, set);

            Assert.AreEqual(Color.red, ColorOf(Evaluate(show, 0, 0.1f)));
            Assert.AreEqual(Color.green, ColorOf(Evaluate(show, 0, 0.5f)));
            Assert.AreEqual(Color.blue, ColorOf(Evaluate(show, 0, 0.9f)));
        }

        [Test]
        public void ColorPalette_PerCycleAdvancesOnceACycle()
        {
            var set = Set();
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(new AlpsColorStop(Color.blue));
            color.colorPhasing.timing = AlpsTimingMode.PerCycle;
            var show = Compile(1, set, end: 4f);

            Assert.AreEqual(Color.red, ColorOf(Evaluate(show, 0, 0.9f)));
            Assert.AreEqual(Color.blue, ColorOf(Evaluate(show, 0, 1.1f)));
            Assert.AreEqual(Color.red, ColorOf(Evaluate(show, 0, 2.1f)));
        }

        [Test]
        public void ColorPalette_GradientStopSweepsInsideItsSegment()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            var set = Set();
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop { isGradient = true, gradient = gradient });
            var show = Compile(1, set);

            Assert.AreEqual(0.5f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameRed], 0.05f);
        }

        [Test]
        public void GoboPalette_PicksByPhaseAndRotatesWithStagger()
        {
            var set = Set();
            var gobo = set.Add(AlpsEffectKind.Gobo);
            gobo.goboStops.Add(new AlpsGoboStop());
            gobo.goboStops.Add(new AlpsGoboStop(5));
            gobo.goboRotationBeats = 4f;
            gobo.goboFixtureStaggerDegrees = 30f;
            var show = Compile(2, set);

            var early = Evaluate(show, 0, 0.25f);
            Assert.AreEqual(AlpsGoboStop.OffIndex, early[AlpsShowEvaluator.FrameGobo], 0.001f);
            Assert.AreEqual(5f, Evaluate(show, 0, 0.75f)[AlpsShowEvaluator.FrameGobo], 0.001f);

            Assert.AreEqual(22.5f, early[AlpsShowEvaluator.FrameGoboRotation], 0.01f, "A quarter beat of a four beat turn.");
            Assert.AreEqual(52.5f, Evaluate(show, 1, 0.25f)[AlpsShowEvaluator.FrameGoboRotation], 0.01f);
        }

        [Test]
        public void Flicker_ScalesBrightnessDeterministically()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Brightness).brightness.value = 100f;
            var flicker = set.Add(AlpsEffectKind.Flicker);
            flicker.flickerStrength = 0.5f;
            var show = Compile(2, set, end: 4f);

            for (var i = 0; i < 20; i++)
            {
                var time = i * 0.173f;
                var scale = Evaluate(show, 1, time)[AlpsShowEvaluator.FrameBrightnessScale];
                Assert.That(scale, Is.InRange(0.5f, 1f));
                Assert.AreEqual(scale, Evaluate(show, 1, time)[AlpsShowEvaluator.FrameBrightnessScale]);
            }
        }

        // ------------------------------------------------------------------ composition

        [Test]
        public void Layers_OverrideOnlyTheChannelsTheyDrive()
        {
            var lower = Set();
            lower.Add(AlpsEffectKind.Brightness).brightness.value = 80f;
            var color = lower.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));

            var upper = Set();
            upper.Add(AlpsEffectKind.Brightness).brightness.value = 20f;

            var show = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = lower, start = 0f, end = 2f, layer = 0 },
                new AlpsStandaloneClip { set = upper, start = 1f, end = 2f, layer = 1 });

            Assert.AreEqual(80f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f);
            var covered = Evaluate(show, 0, 1.5f);
            Assert.AreEqual(20f, covered[AlpsShowEvaluator.FrameBrightness], 0.001f);
            Assert.AreEqual(Color.red, ColorOf(covered), "The upper layer has no color effect, so red shows through.");
        }

        [Test]
        public void Clips_OnOneLayerCrossfadeByTheirMixWeights()
        {
            var a = Set();
            a.Add(AlpsEffectKind.Brightness).brightness.value = 0f;
            var b = Set();
            b.Add(AlpsEffectKind.Brightness).brightness.value = 100f;

            var show = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = a, start = 0f, end = 2f, mixOut = 1f },
                new AlpsStandaloneClip { set = b, start = 1f, end = 3f, mixIn = 1f });

            Assert.AreEqual(50f, Evaluate(show, 0, 1.5f)[AlpsShowEvaluator.FrameBrightness], 0.5f);
            Assert.AreEqual(100f, Evaluate(show, 0, 2.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f);
        }

        [Test]
        public void Clips_EasingInFromNothingRisesFromDark()
        {
            var set = Set();
            set.Add(AlpsEffectKind.Brightness).brightness.value = 80f;
            var show = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = set, start = 0f, end = 2f, mixIn = 1f });

            Assert.AreEqual(40f, Evaluate(show, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.5f, "Half way from dark to 80.");

            var plain = Set();
            plain.Add(AlpsEffectKind.Cone);
            var withoutBrightness = AlpsShowCompiler.CompileStandalone(
                1,
                Bpm,
                new AlpsStandaloneClip { set = plain, start = 0f, end = 2f, mixIn = 1f });

            Assert.AreEqual(0f, Evaluate(withoutBrightness, 0, 0.5f)[AlpsShowEvaluator.FrameBrightness], 0.001f, "Nothing lights it.");
        }

        [Test]
        public void Compiler_CopiesParametersVerbatim()
        {
            var set = FullSet();
            var show = Compile(8, set);

            Assert.AreEqual(1, show.ClipCount);
            Assert.AreEqual(set.effects.Count, show.effects.Length / AlpsShowEvaluator.EffectStride);
            Assert.AreEqual(
                set.effects.Sum(ParameterCount),
                show.parameters.Length / AlpsShowEvaluator.ParamStride);

            var tiltRow = 0;
            var tilt = set.effects.First(e => e.kind == AlpsEffectKind.Move).tilt;
            Assert.AreEqual(tilt.range.x, show.parameters[tiltRow + AlpsShowEvaluator.ParamRangeMin]);
            Assert.AreEqual(tilt.range.y, show.parameters[tiltRow + AlpsShowEvaluator.ParamRangeMax]);

            for (var fixture = 0; fixture < 8; fixture++)
            {
                var frame = Evaluate(show, fixture, 1.3f);
                Assert.IsFalse(frame.Any(float.IsNaN), "No channel may come out as NaN.");
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Every effect kind with ranges, own phase, palettes and an odd and even split.</summary>
        private static AlpsClipEffectSet FullSet()
        {
            var set = Set(0.5f, 0f, 0.5f);
            set.order = AlpsOrderMode.Symmetric;
            set.phase.fixtureGroupSize = 2;
            set.phase.spread = 0.5f;

            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.isRange = true;
            move.pan.isRange = true;
            move.pan.useOwnPhase = true;
            move.pan.ownPhase.mode = AlpsPhaseMode.Random;

            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.isRange = true;

            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Add(new AlpsColorStop(Color.red));
            color.colorStops.Add(new AlpsColorStop { isGradient = true });
            set.Add(AlpsEffectKind.Color);

            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.brightness.isRange = true;
            brightness.brightness.timing = AlpsTimingMode.PerCycle;
            brightness.blackoutOnReturn = true;
            brightness.blackoutFadeIn = 0.2f;
            brightness.blackoutFadeOut = 0.3f;

            set.Add(AlpsEffectKind.Flicker);

            var gobo = set.Add(AlpsEffectKind.Gobo);
            gobo.goboStops.Add(new AlpsGoboStop());
            gobo.goboStops.Add(new AlpsGoboStop(7));
            gobo.goboRotationBeats = 8f;
            gobo.goboFixtureStaggerDegrees = 45f;
            return set;
        }

        private static int ParameterCount(AlpsEffect effect)
        {
            switch (effect.kind)
            {
                case AlpsEffectKind.Move:
                    // Tilt and pan, then the circle's center tilt, center pan and radius.
                    return 5;
                case AlpsEffectKind.Cone:
                    return 2;
                case AlpsEffectKind.Flicker:
                    return 0;
                default:
                    return 1;
            }
        }

        /// <summary>A linear wave of one beat per cycle. By default it rises over the whole cycle, a sawtooth.</summary>
        private static AlpsClipEffectSet Set(float rise = 1f, float holdHigh = 0f, float fall = 0f)
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.Wave;
            set.phase.SetShares(rise, holdHigh, fall);
            set.phase.ease = AlpsEaseType.Linear;
            set.phase.beatsPerCycle = 1f;
            set.phase.spread = 0f;
            set.phase.fixtureGroupSize = 1;
            return set;
        }

        private static AlpsCompiledShow Compile(int fixtures, AlpsClipEffectSet set, float end = 2f)
        {
            return AlpsShowCompiler.CompileStandalone(fixtures, Bpm, new AlpsStandaloneClip { set = set, start = 0f, end = end });
        }

        private static float[] Evaluate(AlpsCompiledShow show, int fixture, float time)
        {
            var stride = AlpsShowEvaluator.FrameStride;
            var fixtureCount = show.fixtureCount;
            var defaults = new float[fixtureCount * stride];
            for (var i = 0; i < fixtureCount; i++)
            {
                AlpsShowPlayer.WriteNeutralFrame(defaults, i * stride);
            }

            var frame = new float[stride];
            AlpsShowEvaluator.EvaluateFixture(
                show.clips, show.effects, show.parameters, show.colors, show.gobos, show.ClipCount,
                show.groupCount, show.groupIndex,
                fixture, time, defaults, frame,
                new float[stride], new float[stride], new float[stride], new float[stride], new float[1]);
            return frame;
        }

        private static Color ColorOf(IReadOnlyList<float> frame)
        {
            return new Color(frame[AlpsShowEvaluator.FrameRed], frame[AlpsShowEvaluator.FrameGreen], frame[AlpsShowEvaluator.FrameBlue], 1f);
        }
    }
}
