using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ManeuverForVRC.Tests
{
    /// <summary>
    /// Level 1: the evaluator's math on compiled arrays, with no timeline or scene involved.
    /// Tempo is 60 BPM throughout, so one beat is one second.
    /// </summary>
    public class MfvShowEvaluatorTests
    {
        private const float Bpm = 60f;

        // ------------------------------------------------------------------ order

        [Test]
        public void Order_NormalGroupsNeighboursBeforeCounting()
        {
            int Position(int fixture) => MfvShowEvaluator.OrderPosition(MfvShowEvaluator.OrderNormal, 0, fixture, 6, 2);

            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1, 2, 2 }, Enumerable.Range(0, 6).Select(Position).ToArray());
        }

        [Test]
        public void Order_SymmetricCountsOutwardFromTheCentre()
        {
            int Position(int fixture, int count) => MfvShowEvaluator.OrderPosition(MfvShowEvaluator.OrderSymmetric, 0, fixture, count, 1);

            CollectionAssert.AreEqual(new[] { 2, 1, 0, 1, 2 }, Enumerable.Range(0, 5).Select(i => Position(i, 5)).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 0, 0, 1 }, Enumerable.Range(0, 4).Select(i => Position(i, 4)).ToArray());
        }

        [Test]
        public void Order_ReverseCountsFromTheLastGroup()
        {
            int Position(int fixture, int groupSize) => MfvShowEvaluator.OrderPosition(MfvShowEvaluator.OrderReverse, 0, fixture, 6, groupSize);

            CollectionAssert.AreEqual(new[] { 5, 4, 3, 2, 1, 0 }, Enumerable.Range(0, 6).Select(i => Position(i, 1)).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 2, 1, 1, 0, 0 }, Enumerable.Range(0, 6).Select(i => Position(i, 2)).ToArray());
            Assert.IsFalse(MfvShowEvaluator.IsMirrored(MfvShowEvaluator.OrderReverse, 0, 6, 1), "Only symmetric mirrors pan.");
        }

        [Test]
        public void Order_SymmetricMirrorsOnlyTheFirstHalf()
        {
            bool Mirrored(int fixture, int count) => MfvShowEvaluator.IsMirrored(MfvShowEvaluator.OrderSymmetric, fixture, count, 1);

            CollectionAssert.AreEqual(
                new[] { true, true, false, false, false },
                Enumerable.Range(0, 5).Select(i => Mirrored(i, 5)).ToArray(),
                "The middle fixture stays unmirrored.");
            CollectionAssert.AreEqual(new[] { true, true, false, false }, Enumerable.Range(0, 4).Select(i => Mirrored(i, 4)).ToArray());
            Assert.IsFalse(MfvShowEvaluator.IsMirrored(MfvShowEvaluator.OrderNormal, 0, 5, 1));
        }

        [Test]
        public void Order_RandomIsAPermutationThatDependsOnTheSeed()
        {
            int[] Positions(int seed) => Enumerable.Range(0, 8)
                .Select(i => MfvShowEvaluator.OrderPosition(MfvShowEvaluator.OrderRandom, seed, i, 8, 1))
                .ToArray();

            var first = Positions(11);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 8).ToArray(), first, "Every position is used exactly once.");
            CollectionAssert.AreEqual(first, Positions(11), "The same seed gives the same order.");
            CollectionAssert.AreNotEqual(first, Positions(12), "A different seed shuffles differently.");
        }

        // ------------------------------------------------------------------ phase

        [Test]
        public void Phase_ForwardIsASawtoothAndInverseFlipsIt()
        {
            float Forward(float cycles, bool inverse) => MfvShowEvaluator.Phase(
                MfvShowEvaluator.PhaseForward, (int)MfvEaseType.Linear, 0.5f, inverse, cycles, 0, 0);

            Assert.AreEqual(0.25f, Forward(0.25f, false), 0.001f);
            Assert.AreEqual(0.25f, Forward(3.25f, false), 0.001f, "The phase wraps every cycle.");
            Assert.AreEqual(0.75f, Forward(0.25f, true), 0.001f);
        }

        [Test]
        public void Phase_EaseShapesTheCycle()
        {
            var eased = MfvShowEvaluator.Phase(MfvShowEvaluator.PhaseForward, (int)MfvEaseType.InQuad, 0.5f, false, 0.5f, 0, 0);
            Assert.AreEqual(0.25f, eased, 0.001f);
        }

        [Test]
        public void Phase_RandomStaysInRangeAndIsDeterministic()
        {
            for (var i = 0; i < 50; i++)
            {
                var cycles = i * 0.137f;
                var a = MfvShowEvaluator.Phase(MfvShowEvaluator.PhaseRandom, 0, 0.5f, false, cycles, 3, 9);
                var b = MfvShowEvaluator.Phase(MfvShowEvaluator.PhaseRandom, 0, 0.5f, false, cycles, 3, 9);
                Assert.That(a, Is.InRange(0f, 1f));
                Assert.AreEqual(a, b);
            }
        }

        [Test]
        public void Phase_ReturnLegFollowsThePingPongRatio()
        {
            Assert.IsFalse(MfvShowEvaluator.IsReturnLeg(MfvShowEvaluator.PhasePingPong, 0.25f, 0.2f));
            Assert.IsTrue(MfvShowEvaluator.IsReturnLeg(MfvShowEvaluator.PhasePingPong, 0.25f, 0.3f));
            Assert.IsFalse(MfvShowEvaluator.IsReturnLeg(MfvShowEvaluator.PhaseForward, 0.25f, 0.3f), "Only ping-pong has a return leg.");
        }

        // ------------------------------------------------------------------ values

        [Test]
        public void Brightness_StaticValueReachesEveryFixture()
        {
            var set = Set();
            set.Add(MfvEffectKind.Brightness).brightness.value = 40f;
            var show = Compile(4, set);

            for (var fixture = 0; fixture < 4; fixture++)
            {
                Assert.AreEqual(40f, Evaluate(show, fixture, 0.5f)[MfvShowEvaluator.FrameBrightness], 0.001f);
            }
        }

        [Test]
        public void Range_WithinCycleFollowsThePhase()
        {
            var set = Set();
            var brightness = set.Add(MfvEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            var show = Compile(1, set);

            Assert.AreEqual(25f, Evaluate(show, 0, 0.25f)[MfvShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(75f, Evaluate(show, 0, 0.75f)[MfvShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void Range_PerCycleAlternatesMinAndMax()
        {
            var set = Set();
            var brightness = set.Add(MfvEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(10f, 90f);
            brightness.timing = MfvTimingMode.PerCycle;
            var show = Compile(1, set, end: 4f);

            Assert.AreEqual(10f, Evaluate(show, 0, 0.5f)[MfvShowEvaluator.FrameBrightness], 0.001f);
            Assert.AreEqual(90f, Evaluate(show, 0, 1.5f)[MfvShowEvaluator.FrameBrightness], 0.001f);
            Assert.AreEqual(10f, Evaluate(show, 0, 2.5f)[MfvShowEvaluator.FrameBrightness], 0.001f);
        }

        [Test]
        public void Spread_LagsEachOrderPosition()
        {
            var set = Set();
            // Three fixtures over three quarters of a cycle, so a quarter each.
            set.phase.spread = 0.75f;
            var brightness = set.Add(MfvEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            var show = Compile(3, set);

            Assert.AreEqual(50f, Evaluate(show, 0, 0.5f)[MfvShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(25f, Evaluate(show, 1, 0.5f)[MfvShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(0f, Evaluate(show, 2, 0.5f)[MfvShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void Spread_AddsAPerFixtureOffset()
        {
            var set = Set();
            var cone = set.Add(MfvEffectKind.Cone);
            cone.coneWidth.value = 10f;
            cone.coneWidth.hasSpread = true;
            cone.coneWidth.spread = 5f;
            var show = Compile(4, set);

            var widths = Enumerable.Range(0, 4).Select(i => Evaluate(show, i, 0.1f)[MfvShowEvaluator.FrameConeWidth]).ToArray();
            CollectionAssert.AreEqual(new[] { 10f, 15f, 20f, 25f }, widths);
        }

        [Test]
        public void SpreadRange_FollowsThePhaseLikeAValueRange()
        {
            var set = Set();
            var cone = set.Add(MfvEffectKind.Cone);
            cone.coneLength.value = 0f;
            cone.coneLength.hasSpread = true;
            cone.coneLength.isRange = true;
            cone.coneLength.spreadRange = new Vector2(0f, 10f);
            var show = Compile(4, set);

            float[] Lengths(float time) => Enumerable.Range(0, 4).Select(i => Evaluate(show, i, time)[MfvShowEvaluator.FrameConeLength]).ToArray();

            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f, 0f }, Lengths(0f), "At the start of the cycle the fan is closed.");
            var half = Lengths(0.5f);
            for (var i = 0; i < 4; i++)
            {
                Assert.AreEqual(5f * i, half[i], 0.01f, "Half way through, each position adds half the spread range.");
            }

            cone.coneLength.timing = MfvTimingMode.PerCycle;
            show = Compile(4, set);
            Assert.AreEqual(0f, Lengths(0.5f)[3], 0.01f, "Per cycle holds the first end for the whole first cycle.");
            Assert.AreEqual(30f, Lengths(1.5f)[3], 0.01f, "And the other end for the next one.");
        }

        [Test]
        public void Spread_KeepsTheValueAsAFixedOffsetAndSetsTheValueRangeAside()
        {
            var set = Set();
            var cone = set.Add(MfvEffectKind.Cone);
            cone.coneLength.value = 3f;
            cone.coneLength.range = new Vector2(0f, 8f);
            cone.coneLength.spread = 2f;
            cone.coneLength.spreadRange = new Vector2(0f, 10f);
            cone.coneLength.hasSpread = true;
            var show = Compile(2, set);

            Assert.AreEqual(3f + 2f, Evaluate(show, 1, 0.5f)[MfvShowEvaluator.FrameConeLength], 0.01f, "Without R the spread is fixed on top of the offset.");

            // R now ranges the spread, the offset stays put and the value range is ignored.
            cone.coneLength.isRange = true;
            show = Compile(2, set);
            Assert.AreEqual(3f, Evaluate(show, 0, 0.5f)[MfvShowEvaluator.FrameConeLength], 0.01f);
            Assert.AreEqual(3f + 5f, Evaluate(show, 1, 0.5f)[MfvShowEvaluator.FrameConeLength], 0.01f);

            // The spread range takes the row's own phase like any range.
            cone.coneLength.useOwnPhase = true;
            cone.coneLength.ownPhase.mode = MfvPhaseMode.Forward;
            cone.coneLength.ownPhase.ease = MfvEaseType.Linear;
            cone.coneLength.ownPhase.beatsPerCycle = 2f;
            show = Compile(2, set);
            Assert.AreEqual(3f + 2.5f, Evaluate(show, 1, 0.5f)[MfvShowEvaluator.FrameConeLength], 0.01f, "A quarter of the own cycle opens a quarter of the spread range.");

            // Turning spread off brings the value range back and drops the spread.
            cone.coneLength.hasSpread = false;
            cone.coneLength.useOwnPhase = false;
            show = Compile(2, set);
            Assert.AreEqual(4f, Evaluate(show, 1, 0.5f)[MfvShowEvaluator.FrameConeLength], 0.01f);
        }

        [Test]
        public void SpreadRange_ReverseOrderFansFromTheOtherEnd()
        {
            var set = Set();
            set.order = MfvOrderMode.Reverse;
            var cone = set.Add(MfvEffectKind.Cone);
            cone.coneWidth.value = 0f;
            cone.coneWidth.hasSpread = true;
            cone.coneWidth.spread = 10f;
            var show = Compile(4, set);

            var widths = Enumerable.Range(0, 4).Select(i => Evaluate(show, i, 0.1f)[MfvShowEvaluator.FrameConeWidth]).ToArray();
            CollectionAssert.AreEqual(new[] { 30f, 20f, 10f, 0f }, widths);
        }

        [Test]
        public void Spread_CanBeNegative()
        {
            var set = Set();
            var cone = set.Add(MfvEffectKind.Cone);
            cone.coneWidth.value = 30f;
            cone.coneWidth.hasSpread = true;
            cone.coneWidth.spread = -5f;
            var show = Compile(4, set);

            var widths = Enumerable.Range(0, 4).Select(i => Evaluate(show, i, 0.1f)[MfvShowEvaluator.FrameConeWidth]).ToArray();
            CollectionAssert.AreEqual(new[] { 30f, 25f, 20f, 15f }, widths);
        }

        [Test]
        public void Move_SymmetricPanSpreadFansOut()
        {
            var set = Set();
            set.order = MfvOrderMode.Symmetric;
            var move = set.Add(MfvEffectKind.Move);
            move.tilt.value = 10f;
            move.tilt.hasSpread = true;
            move.tilt.spread = 5f;
            move.pan.hasSpread = true;
            move.pan.spread = 20f;
            var show = Compile(5, set);

            float[] Channel(int channel) => Enumerable.Range(0, 5).Select(i => Evaluate(show, i, 0.1f)[channel]).ToArray();

            CollectionAssert.AreEqual(new[] { -40f, -20f, 0f, 20f, 40f }, Channel(MfvShowEvaluator.FramePan), "Pan mirrors into a fan.");
            CollectionAssert.AreEqual(new[] { 20f, 15f, 10f, 15f, 20f }, Channel(MfvShowEvaluator.FrameTilt), "Tilt has no left and right.");

            move.pan.spread = -20f;
            show = Compile(5, set);
            CollectionAssert.AreEqual(new[] { 40f, 20f, 0f, -20f, -40f }, Channel(MfvShowEvaluator.FramePan), "Negative spread crosses the fan.");
        }

        [Test]
        public void Move_SymmetricMirrorsTheWholePanSweep()
        {
            var set = Set();
            set.order = MfvOrderMode.Symmetric;
            var move = set.Add(MfvEffectKind.Move);
            move.pan.isRange = true;
            move.pan.range = new Vector2(0f, 60f);
            var show = Compile(4, set);

            for (var i = 0; i < 8; i++)
            {
                var time = i / 8f;
                Assert.AreEqual(
                    -Evaluate(show, 3, time)[MfvShowEvaluator.FramePan],
                    Evaluate(show, 0, time)[MfvShowEvaluator.FramePan],
                    0.001f,
                    "The edges sweep as mirror images.");
            }
        }

        [Test]
        public void Spread_NegativeRunsTheOrderBackwards()
        {
            Assert.AreEqual(0.25f, MfvShowEvaluator.FixtureCycles(0f, 1f, -0.25f, 1, 0f), 0.0001f);
            Assert.AreEqual(-0.25f, MfvShowEvaluator.FixtureCycles(0f, 1f, 0.25f, 1, 0f), 0.0001f);

            var set = Set();
            set.order = MfvOrderMode.Symmetric;
            // Symmetric reaches from the middle to the edge, three positions over five
            // fixtures, so a quarter cycle each again.
            set.phase.spread = -0.75f;
            var brightness = set.Add(MfvEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            var show = Compile(5, set);

            Assert.AreEqual(50f, Evaluate(show, 0, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f, "The edges lead.");
            Assert.AreEqual(0f, Evaluate(show, 2, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f, "The centre trails.");
        }

        [Test]
        public void Spread_KeepsTheLookWhateverTheFixtureCount()
        {
            var set = Set();
            // One cycle over the whole group, the value a chase is authored with.
            set.phase.spread = 1f;
            var brightness = set.Add(MfvEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);

            var four = Compile(4, set);
            var eight = Compile(8, set);

            Assert.AreEqual(50f, Evaluate(four, 2, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(
                50f,
                Evaluate(eight, 4, 0f)[MfvShowEvaluator.FrameBrightness],
                0.01f,
                "Half way along the group is half way through the cycle, whatever the count.");

            Assert.AreEqual(75f, Evaluate(four, 1, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(75f, Evaluate(eight, 2, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f);

            Assert.AreEqual(
                0.25f,
                MfvShowEvaluator.DelayFromSpread(1f, MfvShowEvaluator.OrderNormal, 4, 1),
                0.0001f,
                "A full spread over four positions steps a quarter cycle each.");
            Assert.AreEqual(
                1f / 3f,
                MfvShowEvaluator.DelayFromSpread(1f, MfvShowEvaluator.OrderSymmetric, 5, 1),
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
            var brightness = set.Add(MfvEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);

            // Two beats over a two beat cycle is the full trip, a quarter each over four.
            Assert.AreEqual(75f, Evaluate(Compile(4, set), 1, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f);

            // Doubling the speed keeps the two beats, which is now half the trip.
            set.phase.beatsPerCycle = 4f;
            Assert.AreEqual(87.5f, Evaluate(Compile(4, set), 1, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f);

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
            var brightness = set.Add(MfvEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            brightness.useOwnPhase = true;
            brightness.ownPhase.mode = MfvPhaseMode.Forward;
            brightness.ownPhase.ease = MfvEaseType.Linear;
            brightness.ownPhase.beatsPerCycle = 1f;
            brightness.ownPhase.fixtureGroupSize = 1;
            brightness.ownPhase.spread = 1f;
            var show = Compile(8, set);

            Assert.AreEqual(0f, Evaluate(show, 1, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f, "The first pair starts the cycle.");
            Assert.AreEqual(75f, Evaluate(show, 2, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(50f, Evaluate(show, 4, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(25f, Evaluate(show, 6, 0f)[MfvShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void OwnPhase_IgnoresTheSharedSettings()
        {
            var set = Set();
            set.phase.beatsPerCycle = 4f;
            var brightness = set.Add(MfvEffectKind.Brightness).brightness;
            brightness.isRange = true;
            brightness.range = new Vector2(0f, 100f);
            brightness.useOwnPhase = true;
            brightness.ownPhase.mode = MfvPhaseMode.Forward;
            brightness.ownPhase.ease = MfvEaseType.Linear;
            brightness.ownPhase.beatsPerCycle = 1f;
            var show = Compile(1, set);

            Assert.AreEqual(50f, Evaluate(show, 0, 0.5f)[MfvShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void Parity_OddCountsFromTheFirstFixture()
        {
            var set = Set();
            set.Add(MfvEffectKind.Brightness).brightness.value = 20f;
            set.Add(MfvEffectKind.Brightness).brightness.value = 80f;
            Assert.AreEqual(MfvParity.Even, set.effects[0].parity);
            Assert.AreEqual(MfvParity.Odd, set.effects[1].parity);
            var show = Compile(4, set);

            Assert.AreEqual(80f, Evaluate(show, 0, 0.1f)[MfvShowEvaluator.FrameBrightness], 0.001f, "Fixture 1 of 4 is odd.");
            Assert.AreEqual(20f, Evaluate(show, 1, 0.1f)[MfvShowEvaluator.FrameBrightness], 0.001f);
            Assert.AreEqual(80f, Evaluate(show, 2, 0.1f)[MfvShowEvaluator.FrameBrightness], 0.001f);
        }

        [Test]
        public void BlackoutOnReturn_MutesBrightnessOnTheReturnLeg()
        {
            var set = Set(MfvPhaseMode.PingPong);
            set.phase.pingPongRatio = 0.5f;
            var effect = set.Add(MfvEffectKind.Brightness);
            effect.brightness.isRange = true;
            effect.brightness.range = new Vector2(50f, 100f);
            effect.blackoutOnReturn = true;
            var show = Compile(1, set);

            Assert.AreEqual(75f, Evaluate(show, 0, 0.25f)[MfvShowEvaluator.FrameBrightness], 0.01f);
            Assert.AreEqual(0f, Evaluate(show, 0, 0.75f)[MfvShowEvaluator.FrameBrightness], 0.001f);
        }

        [Test]
        public void BlackoutOnReturn_FadesInAndOutInsideTheOutboundLeg()
        {
            // One beat per cycle, half of it outbound: the outbound leg is 0 to 0.5.
            var set = Set(MfvPhaseMode.PingPong);
            set.phase.pingPongRatio = 0.5f;
            var effect = set.Add(MfvEffectKind.Brightness);
            effect.brightness.value = 100f;
            effect.blackoutOnReturn = true;
            effect.blackoutFadeIn = 0.25f;
            effect.blackoutFadeOut = 0.5f;
            var show = Compile(1, set);

            Assert.AreEqual(50f, Evaluate(show, 0, 0.0625f)[MfvShowEvaluator.FrameBrightness], 0.01f, "Half way through the fade in.");
            Assert.AreEqual(100f, Evaluate(show, 0, 0.25f)[MfvShowEvaluator.FrameBrightness], 0.01f, "Between the two fades.");
            Assert.AreEqual(50f, Evaluate(show, 0, 0.375f)[MfvShowEvaluator.FrameBrightness], 0.01f, "Half way through the fade out.");
            Assert.AreEqual(0f, Evaluate(show, 0, 0.75f)[MfvShowEvaluator.FrameBrightness], 0.001f, "Dark on the return leg.");
        }

        [Test]
        public void BlackoutOnReturn_FollowsTheOwnPhaseOfBrightness()
        {
            // The clip ping-pongs, but brightness runs on its own forward phase with no return leg.
            var set = Set(MfvPhaseMode.PingPong);
            set.phase.pingPongRatio = 0.5f;
            var effect = set.Add(MfvEffectKind.Brightness);
            effect.brightness.isRange = true;
            effect.brightness.range = new Vector2(50f, 100f);
            effect.brightness.useOwnPhase = true;
            effect.brightness.ownPhase.mode = MfvPhaseMode.Forward;
            effect.brightness.ownPhase.ease = MfvEaseType.Linear;
            effect.brightness.ownPhase.beatsPerCycle = 1f;
            effect.blackoutOnReturn = true;
            var show = Compile(1, set);

            Assert.AreEqual(87.5f, Evaluate(show, 0, 0.75f)[MfvShowEvaluator.FrameBrightness], 0.01f);
        }

        [Test]
        public void Move_PhaseOffsetShiftsPanAgainstTilt()
        {
            var set = Set();
            var move = set.Add(MfvEffectKind.Move);
            move.tilt.isRange = true;
            move.tilt.range = new Vector2(0f, 100f);
            move.pan.isRange = true;
            move.pan.range = new Vector2(0f, 100f);
            move.panTiltPhaseOffsetDegrees = 90f;
            var show = Compile(1, set);

            var frame = Evaluate(show, 0, 0.25f);
            Assert.AreEqual(25f, frame[MfvShowEvaluator.FrameTilt], 0.01f);
            Assert.AreEqual(50f, frame[MfvShowEvaluator.FramePan], 0.01f, "90 degrees is a quarter cycle ahead.");
        }

        // ------------------------------------------------------------------ circle

        [Test]
        public void Circle_KeepsTheOpeningAngleWhereverTheCenterPoints()
        {
            foreach (var centerTilt in new[] { 0f, 30f, 60f, -25f })
            {
                var set = Set();
                var move = set.Add(MfvEffectKind.Move);
                move.moveMode = MfvMoveMode.Circle;
                move.circleCenterTilt.value = centerTilt;
                move.circleCenterPan.value = 40f;
                move.circleRadius.value = 12f;
                var show = Compile(1, set);

                for (var i = 0; i < 12; i++)
                {
                    var frame = Evaluate(show, 0, i / 12f);
                    var opening = Vector3.Angle(
                        Direction(frame[MfvShowEvaluator.FrameTilt], frame[MfvShowEvaluator.FramePan]),
                        Direction(centerTilt, 40f));
                    Assert.AreEqual(12f, opening, 0.01f, $"center tilt {centerTilt}, step {i} left the ring.");
                }
            }
        }

        [Test]
        public void Circle_WalksAllTheWayRoundOncePerCycle()
        {
            var set = Set();
            var move = set.Add(MfvEffectKind.Move);
            move.moveMode = MfvMoveMode.Circle;
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
                    Vector3.ProjectOnPlane(Direction(start[MfvShowEvaluator.FrameTilt], start[MfvShowEvaluator.FramePan]), center),
                    Vector3.ProjectOnPlane(Direction(half[MfvShowEvaluator.FrameTilt], half[MfvShowEvaluator.FramePan]), center)),
                0.05f,
                "Half a cycle is the opposite side of the ring.");
            Assert.AreEqual(
                90f,
                Vector3.Angle(
                    Vector3.ProjectOnPlane(Direction(start[MfvShowEvaluator.FrameTilt], start[MfvShowEvaluator.FramePan]), center),
                    Vector3.ProjectOnPlane(Direction(quarter[MfvShowEvaluator.FrameTilt], quarter[MfvShowEvaluator.FramePan]), center)),
                0.05f,
                "A quarter cycle is a quarter turn.");
            Assert.AreEqual(start[MfvShowEvaluator.FrameTilt], full[MfvShowEvaluator.FrameTilt], 0.01f);
            Assert.AreEqual(start[MfvShowEvaluator.FramePan], full[MfvShowEvaluator.FramePan], 0.01f);
        }

        [Test]
        public void Circle_RadiusZeroHoldsTheCenterAndAspectStretchesTheRing()
        {
            var set = Set();
            var move = set.Add(MfvEffectKind.Move);
            move.moveMode = MfvMoveMode.Circle;
            move.circleCenterTilt.value = 35f;
            move.circleCenterPan.value = -20f;
            move.circleRadius.value = 0f;
            var still = Evaluate(Compile(1, set), 0, 0.3f);
            Assert.AreEqual(35f, still[MfvShowEvaluator.FrameTilt], 0.01f);
            Assert.AreEqual(-20f, still[MfvShowEvaluator.FramePan], 0.01f);

            move.circleRadius.value = 10f;
            move.circleAspect = 2f;
            var show = Compile(1, set);
            var center = Direction(35f, -20f);
            var wide = Vector3.Angle(
                Direction(Evaluate(show, 0, 0f)[MfvShowEvaluator.FrameTilt], Evaluate(show, 0, 0f)[MfvShowEvaluator.FramePan]),
                center);
            var tall = Vector3.Angle(
                Direction(Evaluate(show, 0, 0.25f)[MfvShowEvaluator.FrameTilt], Evaluate(show, 0, 0.25f)[MfvShowEvaluator.FramePan]),
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
            var move = set.Add(MfvEffectKind.Move);
            move.moveMode = MfvMoveMode.Circle;
            move.circleCenterTilt.value = 30f;
            move.circleRadius.value = 10f;
            var show = Compile(4, set);

            var center = Direction(30f, 0f);
            Vector3 Spoke(int fixture)
            {
                var frame = Evaluate(show, fixture, 0f);
                return Vector3.ProjectOnPlane(
                    Direction(frame[MfvShowEvaluator.FrameTilt], frame[MfvShowEvaluator.FramePan]),
                    center);
            }

            Assert.AreEqual(90f, Vector3.Angle(Spoke(0), Spoke(1)), 0.05f);
            Assert.AreEqual(180f, Vector3.Angle(Spoke(0), Spoke(2)), 0.05f);
        }

        [Test]
        public void Circle_SymmetricMirrorsTheRings()
        {
            var set = Set();
            set.order = MfvOrderMode.Symmetric;
            var move = set.Add(MfvEffectKind.Move);
            move.moveMode = MfvMoveMode.Circle;
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
                Assert.AreEqual(right[MfvShowEvaluator.FrameTilt], left[MfvShowEvaluator.FrameTilt], 0.01f);
                Assert.AreEqual(-right[MfvShowEvaluator.FramePan], left[MfvShowEvaluator.FramePan], 0.01f, "The outer rings turn as mirror images.");
            }

            var edge = Evaluate(show, 3, 0.3f);
            Assert.AreEqual(
                8f,
                Vector3.Angle(Direction(edge[MfvShowEvaluator.FrameTilt], edge[MfvShowEvaluator.FramePan]), Direction(30f, 25f)),
                0.01f,
                "Spread moves the ring's centre, the ring keeps its size.");
        }

        [Test]
        public void Circle_RadiusRangeFollowsItsOwnPhase()
        {
            var set = Set();
            set.phase.beatsPerCycle = 1f;
            var move = set.Add(MfvEffectKind.Move);
            move.moveMode = MfvMoveMode.Circle;
            move.circleCenterTilt.value = 40f;
            move.circleRadius.isRange = true;
            move.circleRadius.range = new Vector2(0f, 20f);
            move.circleRadius.useOwnPhase = true;
            move.circleRadius.ownPhase.mode = MfvPhaseMode.Forward;
            move.circleRadius.ownPhase.ease = MfvEaseType.Linear;
            move.circleRadius.ownPhase.beatsPerCycle = 8f;
            var show = Compile(1, set, 10f);

            float Opening(float time)
            {
                var frame = Evaluate(show, 0, time);
                return Vector3.Angle(Direction(frame[MfvShowEvaluator.FrameTilt], frame[MfvShowEvaluator.FramePan]), Direction(40f, 0f));
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
            set.Add(MfvEffectKind.Color);
            var show = Compile(1, set);

            var frame = Evaluate(show, 0, 0.3f);
            Assert.AreEqual(1f, frame[MfvShowEvaluator.FrameRed], 0.001f, "The neutral default is white.");
        }

        [Test]
        public void ColorPalette_WithinCycleStepsThroughTheStops()
        {
            var set = Set();
            var color = set.Add(MfvEffectKind.Color);
            color.colorStops.Add(new MfvColorStop(Color.red));
            color.colorStops.Add(new MfvColorStop(Color.green));
            color.colorStops.Add(new MfvColorStop(Color.blue));
            var show = Compile(1, set);

            Assert.AreEqual(Color.red, ColorOf(Evaluate(show, 0, 0.1f)));
            Assert.AreEqual(Color.green, ColorOf(Evaluate(show, 0, 0.5f)));
            Assert.AreEqual(Color.blue, ColorOf(Evaluate(show, 0, 0.9f)));
        }

        [Test]
        public void ColorPalette_PerCycleAdvancesOnceACycle()
        {
            var set = Set();
            var color = set.Add(MfvEffectKind.Color);
            color.colorStops.Add(new MfvColorStop(Color.red));
            color.colorStops.Add(new MfvColorStop(Color.blue));
            color.colorPhasing.timing = MfvTimingMode.PerCycle;
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
            var color = set.Add(MfvEffectKind.Color);
            color.colorStops.Add(new MfvColorStop { isGradient = true, gradient = gradient });
            var show = Compile(1, set);

            Assert.AreEqual(0.5f, Evaluate(show, 0, 0.5f)[MfvShowEvaluator.FrameRed], 0.05f);
        }

        [Test]
        public void GoboPalette_PicksByPhaseAndRotatesWithStagger()
        {
            var set = Set();
            var gobo = set.Add(MfvEffectKind.Gobo);
            gobo.goboStops.Add(new MfvGoboStop());
            gobo.goboStops.Add(new MfvGoboStop(5));
            gobo.goboRotationBeats = 4f;
            gobo.goboFixtureStaggerDegrees = 30f;
            var show = Compile(2, set);

            var early = Evaluate(show, 0, 0.25f);
            Assert.AreEqual(MfvGoboStop.OffIndex, early[MfvShowEvaluator.FrameGobo], 0.001f);
            Assert.AreEqual(5f, Evaluate(show, 0, 0.75f)[MfvShowEvaluator.FrameGobo], 0.001f);

            Assert.AreEqual(22.5f, early[MfvShowEvaluator.FrameGoboRotation], 0.01f, "A quarter beat of a four beat turn.");
            Assert.AreEqual(52.5f, Evaluate(show, 1, 0.25f)[MfvShowEvaluator.FrameGoboRotation], 0.01f);
        }

        [Test]
        public void Flicker_ScalesBrightnessDeterministically()
        {
            var set = Set();
            set.Add(MfvEffectKind.Brightness).brightness.value = 100f;
            var flicker = set.Add(MfvEffectKind.Flicker);
            flicker.flickerStrength = 0.5f;
            var show = Compile(2, set, end: 4f);

            for (var i = 0; i < 20; i++)
            {
                var time = i * 0.173f;
                var scale = Evaluate(show, 1, time)[MfvShowEvaluator.FrameBrightnessScale];
                Assert.That(scale, Is.InRange(0.5f, 1f));
                Assert.AreEqual(scale, Evaluate(show, 1, time)[MfvShowEvaluator.FrameBrightnessScale]);
            }
        }

        // ------------------------------------------------------------------ composition

        [Test]
        public void Layers_OverrideOnlyTheChannelsTheyDrive()
        {
            var lower = Set();
            lower.Add(MfvEffectKind.Brightness).brightness.value = 80f;
            var color = lower.Add(MfvEffectKind.Color);
            color.colorStops.Add(new MfvColorStop(Color.red));

            var upper = Set();
            upper.Add(MfvEffectKind.Brightness).brightness.value = 20f;

            var show = MfvShowCompiler.CompileStandalone(
                1,
                Bpm,
                0f,
                new MfvStandaloneClip { set = lower, start = 0f, end = 2f, layer = 0 },
                new MfvStandaloneClip { set = upper, start = 1f, end = 2f, layer = 1 });

            Assert.AreEqual(80f, Evaluate(show, 0, 0.5f)[MfvShowEvaluator.FrameBrightness], 0.001f);
            var covered = Evaluate(show, 0, 1.5f);
            Assert.AreEqual(20f, covered[MfvShowEvaluator.FrameBrightness], 0.001f);
            Assert.AreEqual(Color.red, ColorOf(covered), "The upper layer has no color effect, so red shows through.");
        }

        [Test]
        public void Clips_OnOneLayerCrossfadeByTheirMixWeights()
        {
            var a = Set();
            a.Add(MfvEffectKind.Brightness).brightness.value = 0f;
            var b = Set();
            b.Add(MfvEffectKind.Brightness).brightness.value = 100f;

            var show = MfvShowCompiler.CompileStandalone(
                1,
                Bpm,
                0f,
                new MfvStandaloneClip { set = a, start = 0f, end = 2f, mixOut = 1f },
                new MfvStandaloneClip { set = b, start = 1f, end = 3f, mixIn = 1f });

            Assert.AreEqual(50f, Evaluate(show, 0, 1.5f)[MfvShowEvaluator.FrameBrightness], 0.5f);
            Assert.AreEqual(100f, Evaluate(show, 0, 2.5f)[MfvShowEvaluator.FrameBrightness], 0.001f);
        }

        [Test]
        public void Clips_EasingInFromNothingBlendsWithTheDefault()
        {
            var set = Set();
            set.Add(MfvEffectKind.Brightness).brightness.value = 0f;
            var show = MfvShowCompiler.CompileStandalone(
                1,
                Bpm,
                0f,
                new MfvStandaloneClip { set = set, start = 0f, end = 2f, mixIn = 1f });

            Assert.AreEqual(50f, Evaluate(show, 0, 0.5f)[MfvShowEvaluator.FrameBrightness], 0.5f, "Half way from the neutral 100 to 0.");
        }

        [Test]
        public void Compiler_CopiesParametersVerbatim()
        {
            var set = FullSet();
            var show = Compile(8, set);

            Assert.AreEqual(1, show.ClipCount);
            Assert.AreEqual(set.effects.Count, show.effects.Length / MfvShowEvaluator.EffectStride);
            Assert.AreEqual(
                set.effects.Sum(ParameterCount),
                show.parameters.Length / MfvShowEvaluator.ParamStride);

            var tiltRow = 0;
            var tilt = set.effects.First(e => e.kind == MfvEffectKind.Move).tilt;
            Assert.AreEqual(tilt.range.x, show.parameters[tiltRow + MfvShowEvaluator.ParamRangeMin]);
            Assert.AreEqual(tilt.range.y, show.parameters[tiltRow + MfvShowEvaluator.ParamRangeMax]);

            for (var fixture = 0; fixture < 8; fixture++)
            {
                var frame = Evaluate(show, fixture, 1.3f);
                Assert.IsFalse(frame.Any(float.IsNaN), "No channel may come out as NaN.");
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Every effect kind with ranges, own phase, palettes and an odd and even split.</summary>
        private static MfvClipEffectSet FullSet()
        {
            var set = Set(MfvPhaseMode.PingPong);
            set.order = MfvOrderMode.Symmetric;
            set.phase.fixtureGroupSize = 2;
            set.phase.spread = 0.5f;

            var move = set.Add(MfvEffectKind.Move);
            move.tilt.isRange = true;
            move.pan.isRange = true;
            move.pan.useOwnPhase = true;
            move.pan.ownPhase.mode = MfvPhaseMode.Random;

            var cone = set.Add(MfvEffectKind.Cone);
            cone.coneWidth.isRange = true;

            var color = set.Add(MfvEffectKind.Color);
            color.colorStops.Add(new MfvColorStop(Color.red));
            color.colorStops.Add(new MfvColorStop { isGradient = true });
            set.Add(MfvEffectKind.Color);

            var brightness = set.Add(MfvEffectKind.Brightness);
            brightness.brightness.isRange = true;
            brightness.brightness.timing = MfvTimingMode.PerCycle;
            brightness.blackoutOnReturn = true;
            brightness.blackoutFadeIn = 0.2f;
            brightness.blackoutFadeOut = 0.3f;

            set.Add(MfvEffectKind.Flicker);

            var gobo = set.Add(MfvEffectKind.Gobo);
            gobo.goboStops.Add(new MfvGoboStop());
            gobo.goboStops.Add(new MfvGoboStop(7));
            return set;
        }

        private static int ParameterCount(MfvEffect effect)
        {
            switch (effect.kind)
            {
                case MfvEffectKind.Move:
                    // Tilt and pan, then the circle's center tilt, center pan and radius.
                    return 5;
                case MfvEffectKind.Cone:
                    return 2;
                case MfvEffectKind.Flicker:
                    return 0;
                default:
                    return 1;
            }
        }

        private static MfvClipEffectSet Set(MfvPhaseMode mode = MfvPhaseMode.Forward)
        {
            var set = new MfvClipEffectSet();
            set.phase.mode = mode;
            set.phase.ease = MfvEaseType.Linear;
            set.phase.beatsPerCycle = 1f;
            set.phase.spread = 0f;
            set.phase.fixtureGroupSize = 1;
            return set;
        }

        private static MfvCompiledShow Compile(int fixtures, MfvClipEffectSet set, float end = 2f)
        {
            return MfvShowCompiler.CompileStandalone(fixtures, Bpm, 0f, new MfvStandaloneClip { set = set, start = 0f, end = end });
        }

        private static float[] Evaluate(MfvCompiledShow show, int fixture, float time)
        {
            var stride = MfvShowEvaluator.FrameStride;
            var fixtureCount = show.fixtureCount;
            var defaults = new float[fixtureCount * stride];
            for (var i = 0; i < fixtureCount; i++)
            {
                MfvShowPlayer.WriteNeutralFrame(defaults, i * stride);
            }

            var frame = new float[stride];
            MfvShowEvaluator.EvaluateFixture(
                show.clips, show.effects, show.parameters, show.colors, show.gobos, show.ClipCount,
                show.groupCount, show.groupIndex,
                fixture, time, defaults, frame,
                new float[stride], new float[stride], new float[stride], new float[stride], new float[1]);
            return frame;
        }

        private static Color ColorOf(IReadOnlyList<float> frame)
        {
            return new Color(frame[MfvShowEvaluator.FrameRed], frame[MfvShowEvaluator.FrameGreen], frame[MfvShowEvaluator.FrameBlue], 1f);
        }
    }
}
