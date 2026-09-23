using System;
using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// How an arrangement places its children: the shape and its size, the spacing, the
    /// order spreads are counted in, and the facing and offsets every slot gets.
    ///
    /// The sizes, offsets and rotations are animatable values, so S spreads them over the
    /// slots in the order. R has nothing to move it yet and is ignored until a Timeline
    /// track drives the arrangement.
    /// </summary>
    [Serializable]
    public class AlpsArrangementSettings
    {
        public static readonly Vector2 RadiusLimit = new Vector2(0f, 30f);
        public static readonly Vector2 SizeLimit = new Vector2(0f, 60f);
        public static readonly Vector2 OffsetLimit = new Vector2(-20f, 20f);
        public static readonly Vector2 AngleLimit = new Vector2(-180f, 180f);
        public static readonly Vector2 SweepLimit = new Vector2(0f, 360f);

        /// <summary>Off by default, so a container keeps its children where they were put.</summary>
        public AlpsArrangementShape shape = AlpsArrangementShape.Off;

        public AlpsArrangementSpacing spacing = AlpsArrangementSpacing.Ends;

        /// <summary>
        /// The order spreads are counted in and the symmetric mirror is based on. It does not
        /// move where the slots are, which always follow the children's order.
        /// </summary>
        public AlpsOrderMode order = AlpsOrderMode.Normal;

        /// <summary>Shuffles the random order.</summary>
        public int seed;

        public AlpsArrangementFacing facing = AlpsArrangementFacing.Keep;

        [Min(3)] public int sides = 6;

        [Min(1)] public int columns = 4;

        /// <summary>Line start in the container's local space.</summary>
        public Vector3 lineStart = new Vector3(-2f, 0f, 0f);

        /// <summary>Line end in the container's local space.</summary>
        public Vector3 lineEnd = new Vector3(2f, 0f, 0f);

        /// <summary>The point every slot looks at while facing a target, in the container's local space.</summary>
        public Vector3 target = new Vector3(0f, 0f, 5f);

        public AlpsAnimatableValue radius = new AlpsAnimatableValue(3f, RadiusLimit);

        /// <summary>Turns a ring or a polygon, or points the middle of an arc.</summary>
        public AlpsAnimatableValue angle = new AlpsAnimatableValue(0f, AngleLimit);

        /// <summary>How much of a full turn a circle covers. Under 360 it is an arc.</summary>
        public AlpsAnimatableValue sweep = new AlpsAnimatableValue(360f, SweepLimit);

        public AlpsAnimatableValue width = new AlpsAnimatableValue(4f, SizeLimit);

        public AlpsAnimatableValue depth = new AlpsAnimatableValue(4f, SizeLimit);

        /// <summary>Offset along the container's up axis.</summary>
        public AlpsAnimatableValue height = new AlpsAnimatableValue(0f, OffsetLimit);

        /// <summary>Offset out of the shape, along each slot's outward direction.</summary>
        public AlpsAnimatableValue outward = new AlpsAnimatableValue(0f, OffsetLimit);

        public AlpsAnimatableValue rotationX = new AlpsAnimatableValue(0f, AngleLimit);

        public AlpsAnimatableValue rotationY = new AlpsAnimatableValue(0f, AngleLimit);

        public AlpsAnimatableValue rotationZ = new AlpsAnimatableValue(0f, AngleLimit);

        /// <summary>
        /// The value behind <see cref="AlpsArrangementEvaluator"/>'s value index
        /// <paramref name="value"/>.
        /// </summary>
        public AlpsAnimatableValue Parameter(int value)
        {
            switch (value)
            {
                case AlpsArrangementEvaluator.ValueRadius: return radius;
                case AlpsArrangementEvaluator.ValueAngle: return angle;
                case AlpsArrangementEvaluator.ValueSweep: return sweep;
                case AlpsArrangementEvaluator.ValueWidth: return width;
                case AlpsArrangementEvaluator.ValueDepth: return depth;
                case AlpsArrangementEvaluator.ValueHeight: return height;
                case AlpsArrangementEvaluator.ValueOutward: return outward;
                case AlpsArrangementEvaluator.ValueRotationX: return rotationX;
                case AlpsArrangementEvaluator.ValueRotationY: return rotationY;
                case AlpsArrangementEvaluator.ValueRotationZ: return rotationZ;
                default: return null;
            }
        }

        /// <summary>
        /// Puts every value's slider limits back to the ones this version uses, so limits
        /// saved by an older one do not hold a slider or a typed value in, and fills values
        /// missing from older data.
        /// </summary>
        public void EnsureLimits()
        {
            radius = Limited(radius, 3f, RadiusLimit);
            angle = Limited(angle, 0f, AngleLimit);
            sweep = Limited(sweep, 360f, SweepLimit);
            width = Limited(width, 4f, SizeLimit);
            depth = Limited(depth, 4f, SizeLimit);
            height = Limited(height, 0f, OffsetLimit);
            outward = Limited(outward, 0f, OffsetLimit);
            rotationX = Limited(rotationX, 0f, AngleLimit);
            rotationY = Limited(rotationY, 0f, AngleLimit);
            rotationZ = Limited(rotationZ, 0f, AngleLimit);
        }

        /// <summary>Writes the parts that are not values into an evaluator layout row.</summary>
        public void WriteLayout(float[] layout)
        {
            layout[AlpsArrangementEvaluator.LayoutShape] = (int)shape;
            layout[AlpsArrangementEvaluator.LayoutSpacing] = (int)spacing;
            layout[AlpsArrangementEvaluator.LayoutOrder] = (int)order;
            layout[AlpsArrangementEvaluator.LayoutSeed] = seed;
            layout[AlpsArrangementEvaluator.LayoutFacing] = (int)facing;
            layout[AlpsArrangementEvaluator.LayoutSides] = sides;
            layout[AlpsArrangementEvaluator.LayoutColumns] = columns;
            WriteVector(layout, AlpsArrangementEvaluator.LayoutStart, lineStart);
            WriteVector(layout, AlpsArrangementEvaluator.LayoutEnd, lineEnd);
            WriteVector(layout, AlpsArrangementEvaluator.LayoutTarget, target);
        }

        /// <summary>
        /// Every value for slot <paramref name="index"/> of <paramref name="count"/>: the
        /// value, or its spread at the slot's order position. The math is the one
        /// <see cref="AlpsShowEvaluator.ResolveScalar"/> uses without a range.
        /// </summary>
        public void ResolveValues(int index, int count, float[] values)
        {
            for (var value = 0; value < AlpsArrangementEvaluator.ValueCount; value++)
            {
                values[value] = Resolve(Parameter(value), index, count);
            }
        }

        private float Resolve(AlpsAnimatableValue value, int index, int count)
        {
            if (value == null)
            {
                return 0f;
            }

            if (!value.hasSpread)
            {
                return value.value;
            }

            return AlpsArrangementEvaluator.SpreadValue(value.spreadRange.x, value.spreadRange.y, (int)order, seed, index, count);
        }

        private static AlpsAnimatableValue Limited(AlpsAnimatableValue value, float fallback, Vector2 limit)
        {
            if (value == null)
            {
                return new AlpsAnimatableValue(fallback, limit);
            }

            value.limit = limit;
            return value;
        }

        private static void WriteVector(float[] data, int offset, Vector3 vector)
        {
            data[offset] = vector.x;
            data[offset + 1] = vector.y;
            data[offset + 2] = vector.z;
        }
    }
}
