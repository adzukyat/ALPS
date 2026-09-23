using UnityEngine;

#if UDONSHARP
using UdonSharp;
#endif

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Where an arrangement puts each of its children. The editor lays children out with it
    /// today, and a Timeline driven arrangement can call the same static methods from Udon
    /// later, so the geometry stays inside what UdonSharp compiles: flat arrays, constants
    /// and Mathf, no structs, generics, ref or out.
    ///
    /// Everything is in the container's local space. Planar shapes lie on the XZ plane with
    /// Y up. Angle 0 is +Z and angles grow clockwise seen from above, the same way
    /// Quaternion.Euler(0, angle, 0) turns +Z.
    ///
    /// Values arrive already resolved for the slot, so the spread (and later a range) is
    /// worked out by the caller and this class only turns numbers into a pose.
    /// </summary>
#if UDONSHARP
    public class AlpsArrangementEvaluator : UdonSharpBehaviour
#else
    public class AlpsArrangementEvaluator : MonoBehaviour
#endif
    {
        // --- Enum values, mirrored from the serialized model ------------------------------

        /// <summary>Leaves the children where they are. Nothing is laid out, so no slot is ever evaluated.</summary>
        public const int ShapeOff = 0;
        public const int ShapeLine = 1;
        public const int ShapeCircle = 2;
        public const int ShapePolygon = 3;
        public const int ShapeRectangle = 4;
        public const int ShapeGrid = 5;

        public const int SpacingEnds = 0;
        public const int SpacingCentered = 1;

        public const int FacingKeep = 0;
        public const int FacingOutward = 1;
        public const int FacingInward = 2;
        public const int FacingAlong = 3;
        public const int FacingTarget = 4;

        // --- Layout row: the parts of an arrangement that are not values -------------------

        public const int LayoutShape = 0;
        public const int LayoutSpacing = 1;
        public const int LayoutOrder = 2;
        public const int LayoutSeed = 3;
        public const int LayoutFacing = 4;
        public const int LayoutSides = 5;
        public const int LayoutColumns = 6;
        /// <summary>Line start, three floats.</summary>
        public const int LayoutStart = 7;
        /// <summary>Line end, three floats.</summary>
        public const int LayoutEnd = 10;
        /// <summary>The point every slot looks at while facing a target, three floats.</summary>
        public const int LayoutTarget = 13;
        public const int LayoutStride = 16;

        // --- Values: one resolved number per animatable value of the arrangement ----------

        public const int ValueRadius = 0;
        /// <summary>Turns a ring or polygon, or points the middle of an arc.</summary>
        public const int ValueAngle = 1;
        /// <summary>How much of a full turn a circle covers. Under a full turn it is an arc.</summary>
        public const int ValueSweep = 2;
        public const int ValueWidth = 3;
        public const int ValueDepth = 4;
        /// <summary>Offset along the container's up axis.</summary>
        public const int ValueHeight = 5;
        /// <summary>Offset along the slot's outward normal.</summary>
        public const int ValueOutward = 6;
        public const int ValueRotationX = 7;
        public const int ValueRotationY = 8;
        public const int ValueRotationZ = 9;
        public const int ValueCount = 10;

        // --- Slot frame, written by EvaluateSlot -------------------------------------------

        /// <summary>The slot's position, offsets included.</summary>
        public const int SlotPosition = 0;
        /// <summary>The direction the path runs through the slot.</summary>
        public const int SlotTangent = 1;
        /// <summary>The horizontal direction pointing out of the shape at the slot.</summary>
        public const int SlotNormal = 2;
        public const int SlotFrameLength = 3;

        /// <summary>A sweep at or above this is a closed ring rather than an arc.</summary>
        public const float FullSweep = 359.999f;

        public const int MinSides = 3;
        public const int MaxSides = 64;

        /// <summary>
        /// Where a slot sits along the path, 0..1. An open path puts its ends on the first and
        /// last slot, and a lone slot in the middle. A closed path has no end to meet, so its
        /// last slot stops one step before the first. Centered gives every slot an equal cell
        /// and sits it in the middle of that cell either way.
        /// </summary>
        public static float SlotParameter(bool closed, int spacing, int index, int count)
        {
            if (count <= 0)
            {
                return 0f;
            }

            if (spacing == SpacingCentered)
            {
                return (index + 0.5f) / count;
            }

            if (closed)
            {
                return index / (float)count;
            }

            return count == 1 ? 0.5f : index / (float)(count - 1);
        }

        /// <summary>True for a shape whose path comes back to where it started.</summary>
        public static bool IsClosed(int shape, float sweep)
        {
            if (shape == ShapeCircle)
            {
                return sweep >= FullSweep;
            }

            return shape == ShapePolygon || shape == ShapeRectangle;
        }

        /// <summary>The value at a slot for a spread that runs from first to last over the order.</summary>
        public static float SpreadValue(float first, float last, int order, int seed, int index, int count)
        {
            var step = AlpsShowEvaluator.StepFromSpread(first, last, order, count, 1);
            return first + step * AlpsShowEvaluator.OrderPosition(order, seed, index, count, 1);
        }

        /// <summary>
        /// Places slot <paramref name="index"/> of <paramref name="count"/>. Writes the
        /// position, tangent and normal into <paramref name="frame"/> and returns the local
        /// rotation. <paramref name="values"/> holds this slot's resolved values.
        /// </summary>
        public static Quaternion EvaluateSlot(float[] layout, float[] values, int index, int count, Vector3[] frame)
        {
            if (count <= 0)
            {
                frame[SlotPosition] = Vector3.zero;
                frame[SlotTangent] = Vector3.right;
                frame[SlotNormal] = Vector3.forward;
                return Quaternion.identity;
            }

            var shape = AlpsShowEvaluator.ToInt(layout[LayoutShape]);
            var spacing = AlpsShowEvaluator.ToInt(layout[LayoutSpacing]);
            var sweep = Mathf.Clamp(values[ValueSweep], 0f, 360f);
            var closed = IsClosed(shape, sweep);

            if (shape == ShapeGrid)
            {
                PlaceOnGrid(layout, values, spacing, index, count, frame);
            }
            else
            {
                var u = SlotParameter(closed, spacing, index, count);
                if (shape == ShapeCircle)
                {
                    PlaceOnCircle(values, closed && spacing == SpacingEnds, sweep, u, frame);
                }
                else if (shape == ShapePolygon || shape == ShapeRectangle)
                {
                    PlaceOnPolyline(shape, layout, values, u, frame);
                }
                else
                {
                    PlaceOnLine(layout, u, frame);
                }
            }

            var normal = frame[SlotNormal];
            var position = frame[SlotPosition] + Vector3.up * values[ValueHeight] + normal * values[ValueOutward];
            frame[SlotPosition] = position;

            var facing = AlpsShowEvaluator.ToInt(layout[LayoutFacing]);
            var aim = Quaternion.identity;
            if (facing == FacingOutward)
            {
                aim = Look(normal);
            }
            else if (facing == FacingInward)
            {
                aim = Look(-normal);
            }
            else if (facing == FacingAlong)
            {
                aim = Look(frame[SlotTangent]);
            }
            else if (facing == FacingTarget)
            {
                aim = Look(ReadVector(layout, LayoutTarget) - position);
            }

            // A symmetric order mirrors the first half like it mirrors clip pan, so a
            // rotation spread opens into a fan that turns away from the middle on both sides.
            var order = AlpsShowEvaluator.ToInt(layout[LayoutOrder]);
            var rotationY = values[ValueRotationY];
            var rotationZ = values[ValueRotationZ];
            if (AlpsShowEvaluator.IsMirrored(order, index, count, 1))
            {
                rotationY = -rotationY;
                rotationZ = -rotationZ;
            }

            return aim * Quaternion.Euler(values[ValueRotationX], rotationY, rotationZ);
        }

        /// <summary>
        /// A rotation whose +Z points along <paramref name="direction"/> and whose up stays
        /// as close to the container's up as it can. Straight up or down there is no such
        /// up, so the back or the front takes its place, the way a nearly vertical direction
        /// already leans. No direction keeps the container's rotation.
        /// </summary>
        public static Quaternion Look(Vector3 direction)
        {
            var lengthSquared = direction.sqrMagnitude;
            if (lengthSquared < 1e-12f)
            {
                return Quaternion.identity;
            }

            if (Vector3.Cross(direction, Vector3.up).sqrMagnitude < 1e-8f * lengthSquared)
            {
                return Quaternion.LookRotation(direction, direction.y > 0f ? Vector3.back : Vector3.forward);
            }

            return Quaternion.LookRotation(direction, Vector3.up);
        }

        /// <summary>The horizontal unit direction at <paramref name="degrees"/>.</summary>
        public static Vector3 Direction(float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        /// <summary>
        /// The horizontal direction a quarter turn anticlockwise from the tangent, seen from
        /// above. A path walked clockwise has it pointing outward. A vertical tangent has no
        /// such direction, so it falls back to the front.
        /// </summary>
        public static Vector3 Perpendicular(Vector3 tangent)
        {
            var flat = new Vector3(-tangent.z, 0f, tangent.x);
            var length = flat.magnitude;
            return length < 1e-4f ? Vector3.forward : flat / length;
        }

        /// <summary>
        /// One grid axis: where cell <paramref name="cell"/> of <paramref name="cells"/> sits
        /// across <paramref name="extent"/>, centered on the container.
        /// </summary>
        public static float GridAxis(int spacing, int cell, int cells, float extent)
        {
            if (spacing == SpacingCentered)
            {
                return -extent * 0.5f + extent * (cell + 0.5f) / Mathf.Max(1, cells);
            }

            return cells <= 1 ? 0f : -extent * 0.5f + extent * cell / (cells - 1);
        }

        /// <summary>How many columns a grid of <paramref name="count"/> slots uses.</summary>
        public static int GridColumns(float columns, int count)
        {
            return Mathf.Clamp(AlpsShowEvaluator.ToInt(columns), 1, Mathf.Max(1, count));
        }

        /// <summary>How many corners the closed path of a polygon or rectangle has.</summary>
        public static int PolylineCorners(int shape, float sides)
        {
            return shape == ShapeRectangle ? 4 : Mathf.Clamp(AlpsShowEvaluator.ToInt(sides), MinSides, MaxSides);
        }

        /// <summary>
        /// Corner <paramref name="corner"/> of a polygon or rectangle. Both start at the front
        /// left and go round clockwise, and a polygon turned by 0 has an edge across the front.
        /// </summary>
        public static Vector3 PolylineCorner(int shape, float[] layout, float[] values, int corner)
        {
            var corners = PolylineCorners(shape, layout[LayoutSides]);
            var j = corner % corners;
            if (j < 0)
            {
                j += corners;
            }

            if (shape == ShapeRectangle)
            {
                var halfWidth = values[ValueWidth] * 0.5f;
                var halfDepth = values[ValueDepth] * 0.5f;
                if (j == 0)
                {
                    return new Vector3(-halfWidth, 0f, halfDepth);
                }

                if (j == 1)
                {
                    return new Vector3(halfWidth, 0f, halfDepth);
                }

                if (j == 2)
                {
                    return new Vector3(halfWidth, 0f, -halfDepth);
                }

                return new Vector3(-halfWidth, 0f, -halfDepth);
            }

            return values[ValueRadius] * Direction(values[ValueAngle] - 180f / corners + 360f * j / corners);
        }

        public static Vector3 ReadVector(float[] data, int offset)
        {
            return new Vector3(data[offset], data[offset + 1], data[offset + 2]);
        }

        private static void PlaceOnLine(float[] layout, float u, Vector3[] frame)
        {
            var start = ReadVector(layout, LayoutStart);
            var delta = ReadVector(layout, LayoutEnd) - start;
            var length = delta.magnitude;
            var tangent = length > 1e-6f ? delta / length : Vector3.right;
            frame[SlotPosition] = start + delta * u;
            frame[SlotTangent] = tangent;
            frame[SlotNormal] = Perpendicular(tangent);
        }

        /// <summary>
        /// A closed ring with its ends spacing starts at the angle and goes all the way round.
        /// Every other circle is laid out around the angle, so an arc points its middle there
        /// and a centered ring stays symmetric about it.
        /// </summary>
        private static void PlaceOnCircle(float[] values, bool fromAngle, float sweep, float u, Vector3[] frame)
        {
            var angle = values[ValueAngle];
            var theta = fromAngle ? angle + 360f * u : angle + sweep * (u - 0.5f);
            var radians = theta * Mathf.Deg2Rad;
            var outward = Direction(theta);
            frame[SlotPosition] = outward * values[ValueRadius];
            frame[SlotTangent] = new Vector3(Mathf.Cos(radians), 0f, -Mathf.Sin(radians));
            frame[SlotNormal] = outward;
        }

        /// <summary>
        /// Walks the closed path of a polygon or rectangle by length, so slots are evenly
        /// spaced along it whatever the edges are. Edges of no length are stepped over. A
        /// slot on a corner faces between its two edges, since a rounding error would
        /// otherwise pick either one.
        /// </summary>
        private static void PlaceOnPolyline(int shape, float[] layout, float[] values, float u, Vector3[] frame)
        {
            var corners = PolylineCorners(shape, layout[LayoutSides]);
            var total = 0f;
            for (var j = 0; j < corners; j++)
            {
                total += (PolylineCorner(shape, layout, values, j + 1) - PolylineCorner(shape, layout, values, j)).magnitude;
            }

            if (total < 1e-6f)
            {
                frame[SlotPosition] = Vector3.zero;
                frame[SlotTangent] = Vector3.right;
                frame[SlotNormal] = Vector3.forward;
                return;
            }

            var epsilon = total * 1e-5f;
            var distance = Mathf.Clamp01(u) * total;
            var edge = 0;
            var corner = PolylineCorner(shape, layout, values, 0);
            var edgeVector = PolylineCorner(shape, layout, values, 1) - corner;
            var edgeLength = edgeVector.magnitude;
            while (edge < corners - 1 && distance >= edgeLength - epsilon)
            {
                distance -= edgeLength;
                edge++;
                corner = PolylineCorner(shape, layout, values, edge);
                edgeVector = PolylineCorner(shape, layout, values, edge + 1) - corner;
                edgeLength = edgeVector.magnitude;
            }

            // The last edge can still be empty when the walk runs out on it. Its end is the
            // first corner, so the walk carries on round to the next edge with a length.
            var skipped = 0;
            while (edgeLength < 1e-6f && skipped < corners)
            {
                skipped++;
                edge++;
                distance = 0f;
                corner = PolylineCorner(shape, layout, values, edge);
                edgeVector = PolylineCorner(shape, layout, values, edge + 1) - corner;
                edgeLength = edgeVector.magnitude;
            }

            var tangent = edgeLength > 1e-6f ? edgeVector / edgeLength : Vector3.right;
            var normal = Perpendicular(tangent);
            distance = Mathf.Clamp(distance, 0f, edgeLength);
            frame[SlotPosition] = corner + tangent * distance;

            if (distance <= epsilon)
            {
                // The edge that ends at this corner, skipping empty ones.
                var previousVector = Vector3.zero;
                for (var step = 1; step <= corners; step++)
                {
                    previousVector = PolylineCorner(shape, layout, values, edge - step + 1) -
                                     PolylineCorner(shape, layout, values, edge - step);
                    if (previousVector.sqrMagnitude > 1e-12f)
                    {
                        break;
                    }
                }

                if (previousVector.sqrMagnitude > 1e-12f)
                {
                    var previousTangent = previousVector.normalized;
                    var tangentSum = previousTangent + tangent;
                    var normalSum = Perpendicular(previousTangent) + normal;
                    if (tangentSum.sqrMagnitude > 1e-8f)
                    {
                        tangent = tangentSum.normalized;
                    }

                    if (normalSum.sqrMagnitude > 1e-8f)
                    {
                        normal = normalSum.normalized;
                    }
                    else
                    {
                        // A path that doubles back on itself has no outside at the turn, so
                        // the corner faces away from the middle instead.
                        var away = new Vector3(corner.x, 0f, corner.z);
                        normal = away.sqrMagnitude > 1e-12f ? away.normalized : Vector3.forward;
                    }
                }
            }

            frame[SlotTangent] = tangent;
            frame[SlotNormal] = normal;
        }

        /// <summary>
        /// Rows run from the front to the back and fill left to right, so a last row with
        /// fewer slots keeps their columns lined up with the rows before it.
        /// </summary>
        private static void PlaceOnGrid(float[] layout, float[] values, int spacing, int index, int count, Vector3[] frame)
        {
            var columns = GridColumns(layout[LayoutColumns], count);
            var rows = (Mathf.Max(1, count) + columns - 1) / columns;
            var x = GridAxis(spacing, index % columns, columns, values[ValueWidth]);
            var z = -GridAxis(spacing, index / columns, rows, values[ValueDepth]);
            frame[SlotPosition] = new Vector3(x, 0f, z);
            frame[SlotTangent] = Vector3.right;
            frame[SlotNormal] = Vector3.forward;
        }
    }
}
