using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Level 1 for arrangements: where <see cref="AlpsArrangementEvaluator"/> puts each slot
    /// and which way it turns it, on the arrays alone.
    /// </summary>
    public class AlpsArrangementEvaluatorTests
    {
        private const float Tolerance = 1e-4f;

        // ------------------------------------------------------------------ line

        [Test]
        public void Line_EndsReachBothEndpoints()
        {
            var settings = Line(new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f));

            AssertPositions(settings, 4,
                new Vector3(-3f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(3f, 0f, 0f));
        }

        [Test]
        public void Line_CenteredKeepsHalfAStepFromTheEnds()
        {
            var settings = Line(new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
            settings.spacing = AlpsArrangementSpacing.Centered;

            AssertPositions(settings, 4,
                new Vector3(-1.5f, 0f, 0f), new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(1.5f, 0f, 0f));
        }

        [Test]
        public void Line_ALoneSlotSitsInTheMiddle()
        {
            var settings = Line(new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 2f));

            AssertPositions(settings, 1, new Vector3(2f, 1f, 1f));
        }

        [Test]
        public void Line_OutwardFacesTheFront()
        {
            var settings = Line(new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
            settings.facing = AlpsArrangementFacing.Outward;

            AssertForward(Rotation(settings, 0, 3), Vector3.forward);
            AssertForward(Rotation(settings, 2, 3), Vector3.forward);
        }

        [Test]
        public void Line_VerticalOrEmptyStaysFinite()
        {
            var vertical = Line(Vector3.zero, new Vector3(0f, 4f, 0f));
            vertical.facing = AlpsArrangementFacing.Along;
            var frame = Frame(vertical, 1, 3, out var rotation);
            AssertVector(new Vector3(0f, 2f, 0f), frame[AlpsArrangementEvaluator.SlotPosition]);
            AssertVector(Vector3.forward, frame[AlpsArrangementEvaluator.SlotNormal]);
            AssertForward(rotation, Vector3.up);
            Assert.That(Quaternion.Angle(rotation, Quaternion.Euler(-90f, 0f, 0f)), Is.LessThan(0.01f));

            var empty = Line(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, 3f));
            empty.facing = AlpsArrangementFacing.Along;
            for (var i = 0; i < 3; i++)
            {
                var slot = Frame(empty, i, 3, out var slotRotation);
                AssertVector(new Vector3(1f, 2f, 3f), slot[AlpsArrangementEvaluator.SlotPosition]);
                Assert.That(IsFinite(slotRotation), Is.True);
            }
        }

        // ---------------------------------------------------------------- circle

        [Test]
        public void Circle_ClosedRingStartsAtTheFrontAndGoesClockwise()
        {
            var settings = Circle(2f);

            AssertPositions(settings, 4,
                new Vector3(0f, 0f, 2f), new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, -2f), new Vector3(-2f, 0f, 0f));
        }

        [Test]
        public void Circle_OutwardAndInwardFaceAlongTheRadius()
        {
            var settings = Circle(2f);
            settings.facing = AlpsArrangementFacing.Outward;
            AssertForward(Rotation(settings, 1, 4), Vector3.right);
            AssertUp(Rotation(settings, 1, 4), Vector3.up);

            settings.facing = AlpsArrangementFacing.Inward;
            AssertForward(Rotation(settings, 1, 4), Vector3.left);
        }

        [Test]
        public void Circle_AlongFollowsTheRingClockwise()
        {
            var settings = Circle(2f);
            settings.facing = AlpsArrangementFacing.Along;

            AssertForward(Rotation(settings, 0, 4), Vector3.right);
            AssertForward(Rotation(settings, 1, 4), Vector3.back);
        }

        [Test]
        public void Circle_CenteredRingStaysSymmetricAboutTheFront()
        {
            var settings = Circle(1f);
            settings.spacing = AlpsArrangementSpacing.Centered;

            AssertPositions(settings, 1, new Vector3(0f, 0f, 1f));
            AssertPositions(settings, 2, new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f));
        }

        [Test]
        public void Arc_PointsItsMiddleAtTheAngle()
        {
            var settings = Circle(1f);
            settings.sweep.value = 90f;

            var positions = Positions(settings, 3);
            AssertVector(AlpsArrangementEvaluator.Direction(-45f), positions[0]);
            AssertVector(Vector3.forward, positions[1]);
            AssertVector(AlpsArrangementEvaluator.Direction(45f), positions[2]);

            settings.angle.value = 90f;
            AssertVector(Vector3.right, Positions(settings, 3)[1]);
        }

        [Test]
        public void Circle_FullSweepDoesNotPutTwoSlotsOnTheSameSpot()
        {
            var settings = Circle(1f);
            var positions = Positions(settings, 6);

            for (var i = 0; i < positions.Length; i++)
            {
                for (var j = i + 1; j < positions.Length; j++)
                {
                    Assert.That(Vector3.Distance(positions[i], positions[j]), Is.GreaterThan(0.5f), $"{i} and {j}");
                }
            }
        }

        [Test]
        public void Circle_RadiusSpreadWindsASpiral()
        {
            var settings = Circle(1f);
            settings.radius.hasSpread = true;
            settings.radius.spreadRange = new Vector2(1f, 4f);

            var radii = Positions(settings, 4).Select(p => p.magnitude).ToArray();
            CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, radii, new ToleranceComparer());
        }

        // --------------------------------------------------------------- polygon

        [Test]
        public void Polygon_SquareEndsSitOnTheCornersAndFaceBetweenTheEdges()
        {
            var settings = Polygon(4, Mathf.Sqrt(2f));
            settings.facing = AlpsArrangementFacing.Outward;

            AssertPositions(settings, 4,
                new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, -1f));
            AssertForward(Rotation(settings, 0, 4), new Vector3(-1f, 0f, 1f).normalized);
            AssertForward(Rotation(settings, 1, 4), new Vector3(1f, 0f, 1f).normalized);
        }

        [Test]
        public void Polygon_SquareCenteredSitsOnTheEdgeMiddles()
        {
            var settings = Polygon(4, Mathf.Sqrt(2f));
            settings.spacing = AlpsArrangementSpacing.Centered;
            settings.facing = AlpsArrangementFacing.Outward;

            AssertPositions(settings, 4,
                new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(-1f, 0f, 0f));
            AssertForward(Rotation(settings, 0, 4), Vector3.forward);
            AssertForward(Rotation(settings, 1, 4), Vector3.right);
        }

        [Test]
        public void Polygon_TriangleCornersFaceAwayFromTheMiddle()
        {
            var settings = Polygon(3, 2f);
            settings.facing = AlpsArrangementFacing.Outward;

            for (var i = 0; i < 6; i += 2)
            {
                var frame = Frame(settings, i, 6, out var rotation);
                var away = frame[AlpsArrangementEvaluator.SlotPosition].normalized;
                AssertForward(rotation, away);
            }
        }

        // ------------------------------------------------------------- rectangle

        [Test]
        public void Rectangle_WalksClockwiseFromTheFrontLeftByLength()
        {
            var settings = new AlpsArrangementSettings { shape = AlpsArrangementShape.Rectangle };
            settings.width.value = 4f;
            settings.depth.value = 2f;

            AssertPositions(settings, 6,
                new Vector3(-2f, 0f, 1f), new Vector3(0f, 0f, 1f), new Vector3(2f, 0f, 1f),
                new Vector3(2f, 0f, -1f), new Vector3(0f, 0f, -1f), new Vector3(-2f, 0f, -1f));
        }

        [Test]
        public void Rectangle_WithNoDepthStepsOverTheEmptyEdges()
        {
            var settings = new AlpsArrangementSettings { shape = AlpsArrangementShape.Rectangle };
            settings.width.value = 4f;
            settings.depth.value = 0f;
            settings.facing = AlpsArrangementFacing.Outward;

            AssertPositions(settings, 4,
                new Vector3(-2f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 0f));
            for (var i = 0; i < 4; i++)
            {
                Assert.That(IsFinite(Rotation(settings, i, 4)), Is.True, $"slot {i}");
            }
        }

        // ------------------------------------------------------------------ grid

        [Test]
        public void Grid_FillsRowsFromTheFrontLeftAndKeepsTheLastRowInItsColumns()
        {
            var settings = new AlpsArrangementSettings { shape = AlpsArrangementShape.Grid, columns = 3 };
            settings.width.value = 4f;
            settings.depth.value = 2f;

            AssertPositions(settings, 5,
                new Vector3(-2f, 0f, 1f), new Vector3(0f, 0f, 1f), new Vector3(2f, 0f, 1f),
                new Vector3(-2f, 0f, -1f), new Vector3(0f, 0f, -1f));
        }

        [Test]
        public void Grid_MoreColumnsThanSlotsSpreadsOneRow()
        {
            var settings = new AlpsArrangementSettings { shape = AlpsArrangementShape.Grid, columns = 8 };
            settings.width.value = 4f;

            AssertPositions(settings, 3, new Vector3(-2f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f));
        }

        // --------------------------------------------------------------- offsets

        [Test]
        public void Offsets_MoveUpAndOutOfTheShape()
        {
            var settings = Circle(2f);
            settings.height.value = 1f;
            settings.outward.value = 0.5f;

            AssertPositions(settings, 4,
                new Vector3(0f, 1f, 2.5f), new Vector3(2.5f, 1f, 0f), new Vector3(0f, 1f, -2.5f), new Vector3(-2.5f, 1f, 0f));
        }

        [Test]
        public void Target_FacesThePointFromWhereTheOffsetsLeaveTheSlot()
        {
            var settings = Line(new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
            settings.facing = AlpsArrangementFacing.Target;
            settings.target = new Vector3(0f, 0f, 4f);
            settings.height.value = 2f;

            var frame = Frame(settings, 0, 2, out var rotation);
            AssertForward(rotation, (settings.target - frame[AlpsArrangementEvaluator.SlotPosition]).normalized);
        }

        [Test]
        public void Target_StraightAboveOrOnTheSlotStaysFinite()
        {
            var settings = Line(Vector3.zero, Vector3.zero);
            settings.facing = AlpsArrangementFacing.Target;

            settings.target = new Vector3(0f, 5f, 0f);
            var above = Rotation(settings, 0, 1);
            AssertForward(above, Vector3.up);
            Assert.That(IsFinite(above), Is.True);

            settings.target = Vector3.zero;
            Assert.That(Quaternion.Angle(Rotation(settings, 0, 1), Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void Rotation_TurnsAfterTheFacing()
        {
            var settings = Circle(2f);
            settings.facing = AlpsArrangementFacing.Outward;
            settings.rotationX.value = 90f;

            // Outward at the right side faces +X, then a quarter turn about its own X tips
            // its front down.
            AssertForward(Rotation(settings, 1, 4), Vector3.down);
        }

        [Test]
        public void Rotation_SymmetricOrderMirrorsTheFirstHalf()
        {
            var settings = Line(new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
            settings.order = AlpsOrderMode.Symmetric;
            settings.rotationY.hasSpread = true;
            settings.rotationY.spreadRange = new Vector2(0f, 30f);
            settings.rotationZ.value = 10f;

            var left = Rotation(settings, 0, 5).eulerAngles;
            var middle = Rotation(settings, 2, 5).eulerAngles;
            var right = Rotation(settings, 4, 5).eulerAngles;
            Assert.That(Mathf.DeltaAngle(0f, left.y), Is.EqualTo(-30f).Within(0.01f));
            Assert.That(Mathf.DeltaAngle(0f, left.z), Is.EqualTo(-10f).Within(0.01f));
            Assert.That(Mathf.DeltaAngle(0f, middle.y), Is.EqualTo(0f).Within(0.01f));
            Assert.That(Mathf.DeltaAngle(0f, middle.z), Is.EqualTo(10f).Within(0.01f));
            Assert.That(Mathf.DeltaAngle(0f, right.y), Is.EqualTo(30f).Within(0.01f));
        }

        [Test]
        public void NoSlots_LeavesAnIdentityPose()
        {
            var frame = new Vector3[AlpsArrangementEvaluator.SlotFrameLength];
            var rotation = AlpsArrangementEvaluator.EvaluateSlot(
                new float[AlpsArrangementEvaluator.LayoutStride], new float[AlpsArrangementEvaluator.ValueCount], 0, 0, frame);

            Assert.That(rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(frame[AlpsArrangementEvaluator.SlotPosition], Is.EqualTo(Vector3.zero));
        }

        // ---------------------------------------------------------------- spread

        [TestCase(AlpsOrderMode.Normal)]
        [TestCase(AlpsOrderMode.Reverse)]
        [TestCase(AlpsOrderMode.Symmetric)]
        [TestCase(AlpsOrderMode.Random)]
        public void Spread_MatchesTheShowEvaluatorWithoutARange(AlpsOrderMode order)
        {
            const int count = 7;
            const int seed = 13;
            var first = 20f;
            var last = 140f;

            var settings = new AlpsArrangementSettings { order = order, seed = seed };
            settings.height.limit = new Vector2(0f, 200f);
            settings.height.hasSpread = true;
            settings.height.spreadRange = new Vector2(first, last);

            var set = new AlpsClipEffectSet { order = order };
            set.phase.fixtureGroupSize = 1;
            set.Add(AlpsEffectKind.Brightness);
            var brightness = set.effects[0].brightness;
            brightness.isRange = false;
            brightness.hasSpread = true;
            brightness.spreadRange = new Vector2(first, last);
            var show = AlpsShowCompiler.CompileStandalone(count, 60f, new AlpsStandaloneClip { set = set, end = 4f, seed = seed });

            var values = new float[AlpsArrangementEvaluator.ValueCount];
            using (var gpu = new AlpsGpuShow(show, count))
            {
                for (var i = 0; i < count; i++)
                {
                    settings.ResolveValues(i, count, values);
                    var expected = gpu.Evaluate(i, 0.5f)[AlpsShowLayout.FrameBrightness];
                    Assert.That(values[AlpsArrangementEvaluator.ValueHeight], Is.EqualTo(expected).Within(Tolerance), $"slot {i}");
                }
            }
        }

        [Test]
        public void Spread_IgnoresARangeSavedOnTheValue()
        {
            var settings = new AlpsArrangementSettings();
            settings.height.value = 1.5f;
            settings.height.isRange = true;
            settings.height.range = new Vector2(-3f, 3f);

            var values = new float[AlpsArrangementEvaluator.ValueCount];
            settings.ResolveValues(0, 3, values);
            Assert.That(values[AlpsArrangementEvaluator.ValueHeight], Is.EqualTo(1.5f));
        }

        // ----------------------------------------------------------------- enums

        [Test]
        public void Enums_MatchTheEvaluatorConstants()
        {
            Assert.That((int)AlpsArrangementShape.Off, Is.EqualTo(AlpsArrangementEvaluator.ShapeOff));
            Assert.That((int)AlpsArrangementShape.Line, Is.EqualTo(AlpsArrangementEvaluator.ShapeLine));
            Assert.That((int)AlpsArrangementShape.Circle, Is.EqualTo(AlpsArrangementEvaluator.ShapeCircle));
            Assert.That((int)AlpsArrangementShape.Polygon, Is.EqualTo(AlpsArrangementEvaluator.ShapePolygon));
            Assert.That((int)AlpsArrangementShape.Rectangle, Is.EqualTo(AlpsArrangementEvaluator.ShapeRectangle));
            Assert.That((int)AlpsArrangementShape.Grid, Is.EqualTo(AlpsArrangementEvaluator.ShapeGrid));
            Assert.That((int)AlpsArrangementSpacing.Ends, Is.EqualTo(AlpsArrangementEvaluator.SpacingEnds));
            Assert.That((int)AlpsArrangementSpacing.Centered, Is.EqualTo(AlpsArrangementEvaluator.SpacingCentered));
            Assert.That((int)AlpsArrangementFacing.Keep, Is.EqualTo(AlpsArrangementEvaluator.FacingKeep));
            Assert.That((int)AlpsArrangementFacing.Outward, Is.EqualTo(AlpsArrangementEvaluator.FacingOutward));
            Assert.That((int)AlpsArrangementFacing.Inward, Is.EqualTo(AlpsArrangementEvaluator.FacingInward));
            Assert.That((int)AlpsArrangementFacing.Along, Is.EqualTo(AlpsArrangementEvaluator.FacingAlong));
            Assert.That((int)AlpsArrangementFacing.Target, Is.EqualTo(AlpsArrangementEvaluator.FacingTarget));
            Assert.That(Enum.GetValues(typeof(AlpsArrangementShape)).Length, Is.EqualTo(6));
            Assert.That(Enum.GetValues(typeof(AlpsArrangementFacing)).Length, Is.EqualTo(5));

            var settings = new AlpsArrangementSettings();
            for (var value = 0; value < AlpsArrangementEvaluator.ValueCount; value++)
            {
                Assert.That(settings.Parameter(value), Is.Not.Null, $"value {value}");
            }
        }

        // --------------------------------------------------------------- helpers

        private static AlpsArrangementSettings Line(Vector3 start, Vector3 end)
        {
            return new AlpsArrangementSettings { shape = AlpsArrangementShape.Line, lineStart = start, lineEnd = end };
        }

        private static AlpsArrangementSettings Circle(float radius)
        {
            var settings = new AlpsArrangementSettings { shape = AlpsArrangementShape.Circle };
            settings.radius.value = radius;
            return settings;
        }

        private static AlpsArrangementSettings Polygon(int sides, float radius)
        {
            var settings = new AlpsArrangementSettings { shape = AlpsArrangementShape.Polygon, sides = sides };
            settings.radius.value = radius;
            return settings;
        }

        private static Vector3[] Frame(AlpsArrangementSettings settings, int index, int count, out Quaternion rotation)
        {
            var layout = new float[AlpsArrangementEvaluator.LayoutStride];
            var values = new float[AlpsArrangementEvaluator.ValueCount];
            var frame = new Vector3[AlpsArrangementEvaluator.SlotFrameLength];
            settings.WriteLayout(layout);
            settings.ResolveValues(index, count, values);
            rotation = AlpsArrangementEvaluator.EvaluateSlot(layout, values, index, count, frame);
            return frame;
        }

        private static Quaternion Rotation(AlpsArrangementSettings settings, int index, int count)
        {
            Frame(settings, index, count, out var rotation);
            return rotation;
        }

        private static Vector3[] Positions(AlpsArrangementSettings settings, int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => Frame(settings, i, count, out _)[AlpsArrangementEvaluator.SlotPosition])
                .ToArray();
        }

        private static void AssertPositions(AlpsArrangementSettings settings, int count, params Vector3[] expected)
        {
            var positions = Positions(settings, count);
            for (var i = 0; i < expected.Length; i++)
            {
                AssertVector(expected[i], positions[i], $"slot {i}");
            }
        }

        private static void AssertForward(Quaternion rotation, Vector3 expected)
        {
            AssertVector(expected, rotation * Vector3.forward, "forward");
        }

        private static void AssertUp(Quaternion rotation, Vector3 expected)
        {
            AssertVector(expected, rotation * Vector3.up, "up");
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, string message = null)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-3f), $"{message} expected {expected:F4} but was {actual:F4}");
        }

        private static bool IsFinite(Quaternion rotation)
        {
            return !float.IsNaN(rotation.x) && !float.IsNaN(rotation.y) && !float.IsNaN(rotation.z) && !float.IsNaN(rotation.w) &&
                   !float.IsInfinity(rotation.x) && !float.IsInfinity(rotation.w);
        }

        private sealed class ToleranceComparer : System.Collections.IComparer
        {
            public int Compare(object x, object y)
            {
                return Mathf.Abs((float)x - (float)y) < 1e-3f ? 0 : ((float)x).CompareTo((float)y);
            }
        }
    }
}
