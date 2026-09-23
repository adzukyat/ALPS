using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// What an arrangement shows and lets you drag in the Scene view: the path its children
    /// sit on, each slot's number and facing, the line ends and the target as move handles,
    /// and dots that pull the radius, the width and the depth.
    ///
    /// The path is drawn where the children are, lifted by the height and pushed out by the
    /// outward offset, so it runs through them rather than along the container's plane.
    /// That plane is often a floor, which a line drawn on it would fight for depth, showing
    /// as dashes that change as the view moves. Everything is drawn over the scene.
    ///
    /// A value with S on has no single size to pull, so its dot is left out, and the path
    /// follows the first slot's value. The math that turns a dragged point into a value is
    /// kept apart so tests can reach it.
    /// </summary>
    public static class AlpsArrangementHandles
    {
        private static readonly Color PathColor = new Color(0.85f, 0.64f, 0.26f, 0.9f);
        private static readonly Color SlotColor = new Color(0.85f, 0.64f, 0.26f, 1f);
        private static readonly Color FacingColor = new Color(0.36f, 0.62f, 0.95f, 1f);
        private static readonly Color TargetColor = new Color(0.95f, 0.45f, 0.45f, 1f);

        private const int CircleSegments = 64;

        private const float PathThickness = 2f;

        /// <summary>
        /// The radius a dot dragged to <paramref name="local"/> sets, measured along the dot's
        /// direction on the path, which sits <paramref name="offset"/> further out than the
        /// radius itself.
        /// </summary>
        public static float RadiusAt(Vector3 local, float angle, float offset, Vector2 limit)
        {
            var along = Vector3.Dot(local, AlpsArrangementEvaluator.Direction(angle));
            return Mathf.Clamp(along - offset, limit.x, limit.y);
        }

        /// <summary>
        /// The width or depth a dot dragged to <paramref name="coordinate"/> on its axis sets,
        /// where the path's edge sits <paramref name="offset"/> beyond half of it.
        /// </summary>
        public static float ExtentAt(float coordinate, float offset, Vector2 limit)
        {
            return Mathf.Clamp((coordinate - offset) * 2f, limit.x, limit.y);
        }

        /// <summary>
        /// How much further out than the radius the path reaches along the radius dot: the
        /// outward offset on a circle, and on a polygon the corner of the shape whose edges
        /// the offset moved out.
        /// </summary>
        public static float RadiusOffset(AlpsArrangementSettings settings)
        {
            var outward = Plain(settings.outward);
            if (settings.shape != AlpsArrangementShape.Polygon)
            {
                return outward;
            }

            var corners = AlpsArrangementEvaluator.PolylineCorners(AlpsArrangementEvaluator.ShapePolygon, settings.sides);
            return outward / Mathf.Cos(Mathf.PI / corners);
        }

        /// <summary>
        /// The path the children sit on, in local space: the shape lifted by the height and
        /// pushed out by the outward offset, as the first slot has them. Returns whether it
        /// closes back on its start.
        /// </summary>
        public static bool Outline(AlpsArrangementSettings settings, List<Vector3> points)
        {
            points.Clear();
            var lift = Vector3.up * Plain(settings.height);
            var outward = Plain(settings.outward);
            switch (settings.shape)
            {
                case AlpsArrangementShape.Line:
                {
                    var along = settings.lineEnd - settings.lineStart;
                    var tangent = along.sqrMagnitude > 1e-12f ? along.normalized : Vector3.right;
                    var shift = lift + AlpsArrangementEvaluator.Perpendicular(tangent) * outward;
                    points.Add(settings.lineStart + shift);
                    points.Add(settings.lineEnd + shift);
                    return false;
                }
                case AlpsArrangementShape.Circle:
                {
                    var sweep = Mathf.Clamp(Plain(settings.sweep), 0f, 360f);
                    var radius = Plain(settings.radius) + outward;
                    var angle = Plain(settings.angle);
                    var closed = sweep >= AlpsArrangementEvaluator.FullSweep;
                    var steps = Mathf.Max(2, Mathf.CeilToInt(CircleSegments * sweep / 360f));
                    for (var i = 0; i <= steps; i++)
                    {
                        if (closed && i == steps)
                        {
                            break;
                        }

                        points.Add(radius * AlpsArrangementEvaluator.Direction(angle + sweep * (i / (float)steps - 0.5f)) + lift);
                    }

                    return closed;
                }
                case AlpsArrangementShape.Polygon:
                case AlpsArrangementShape.Rectangle:
                case AlpsArrangementShape.Grid:
                {
                    var layout = new float[AlpsArrangementEvaluator.LayoutStride];
                    var values = PlainValues(settings);
                    settings.WriteLayout(layout);

                    // Moving every edge out by the offset grows a polygon's radius to its new
                    // corners and a rectangle by the offset on each side. A grid's slots all
                    // face the front, so the offset moves its whole box forward.
                    var shift = lift;
                    var shape = (int)settings.shape;
                    if (settings.shape == AlpsArrangementShape.Polygon)
                    {
                        values[AlpsArrangementEvaluator.ValueRadius] += RadiusOffset(settings);
                    }
                    else if (settings.shape == AlpsArrangementShape.Rectangle)
                    {
                        values[AlpsArrangementEvaluator.ValueWidth] += outward * 2f;
                        values[AlpsArrangementEvaluator.ValueDepth] += outward * 2f;
                    }
                    else
                    {
                        shape = AlpsArrangementEvaluator.ShapeRectangle;
                        shift += Vector3.forward * outward;
                    }

                    var corners = AlpsArrangementEvaluator.PolylineCorners(shape, layout[AlpsArrangementEvaluator.LayoutSides]);
                    for (var j = 0; j < corners; j++)
                    {
                        points.Add(AlpsArrangementEvaluator.PolylineCorner(shape, layout, values, j) + shift);
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>Draws the arrangement and runs its handles. Returns whether a handle changed a value.</summary>
        public static bool OnSceneGUI(AlpsArrangement arrangement)
        {
            var settings = arrangement.settings;
            if (settings == null)
            {
                return false;
            }

            settings.EnsureLimits();
            var root = arrangement.transform;
            var zTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            try
            {
                DrawOutline(root, settings);
                DrawSlots(root);
                return DoHandles(arrangement, root, settings);
            }
            finally
            {
                Handles.zTest = zTest;
            }
        }

        private static void DrawOutline(Transform root, AlpsArrangementSettings settings)
        {
            var points = new List<Vector3>();
            var closed = Outline(settings, points);
            if (points.Count < 2)
            {
                return;
            }

            if (closed)
            {
                points.Add(points[0]);
            }

            for (var i = 0; i < points.Count; i++)
            {
                points[i] = root.TransformPoint(points[i]);
            }

            // A thick line hands Handles.zTest to its material. The anti-aliased polyline is drawn
            // natively and was seen fighting a floor for depth anyway.
            using (new Handles.DrawingScope(PathColor))
            {
                for (var i = 1; i < points.Count; i++)
                {
                    Handles.DrawLine(points[i - 1], points[i], PathThickness);
                }
            }
        }

        private static void DrawSlots(Transform root)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var position = child.position;
                var size = HandleUtility.GetHandleSize(position);
                using (new Handles.DrawingScope(SlotColor))
                {
                    Handles.DotHandleCap(0, position, Quaternion.identity, size * 0.03f, EventType.Repaint);
                    Handles.Label(position + root.up * size * 0.15f, (i + 1).ToString());
                }

                using (new Handles.DrawingScope(FacingColor))
                {
                    Handles.DrawLine(position, position + child.forward * size * 0.5f);
                }
            }
        }

        private static bool DoHandles(AlpsArrangement arrangement, Transform root, AlpsArrangementSettings settings)
        {
            var handleRotation = Tools.pivotRotation == PivotRotation.Local ? root.rotation : Quaternion.identity;
            var changed = false;

            if (settings.shape == AlpsArrangementShape.Line)
            {
                changed |= MovePoint(arrangement, root, handleRotation, settings.lineStart, value => settings.lineStart = value);
                changed |= MovePoint(arrangement, root, handleRotation, settings.lineEnd, value => settings.lineEnd = value);
            }

            if (settings.facing == AlpsArrangementFacing.Target)
            {
                using (new Handles.DrawingScope(TargetColor))
                {
                    var world = root.TransformPoint(settings.target);
                    Handles.SphereHandleCap(0, world, Quaternion.identity, HandleUtility.GetHandleSize(world) * 0.12f, EventType.Repaint);
                }

                changed |= MovePoint(arrangement, root, handleRotation, settings.target, value => settings.target = value);
            }

            // The dots sit on the drawn path, so they carry its lift and outward offset.
            var lift = Vector3.up * Plain(settings.height);
            var outward = Plain(settings.outward);

            if ((settings.shape == AlpsArrangementShape.Circle || settings.shape == AlpsArrangementShape.Polygon) &&
                !settings.radius.hasSpread)
            {
                // A polygon's radius reaches its corners, so the dot sits on the first one.
                var angle = Plain(settings.angle);
                if (settings.shape == AlpsArrangementShape.Polygon)
                {
                    var corners = AlpsArrangementEvaluator.PolylineCorners(AlpsArrangementEvaluator.ShapePolygon, settings.sides);
                    angle -= 180f / corners;
                }

                var direction = AlpsArrangementEvaluator.Direction(angle);
                var offset = RadiusOffset(settings);
                changed |= SlideDot(arrangement, root, direction * (settings.radius.value + offset) + lift, direction,
                    local => settings.radius.value = RadiusAt(local, angle, offset, settings.radius.limit));
            }

            if (settings.shape == AlpsArrangementShape.Rectangle || settings.shape == AlpsArrangementShape.Grid)
            {
                // A rectangle's edges moved out by the offset on every side. A grid's box only
                // moved forward, so its side edges stay where the width puts them.
                var sideOffset = settings.shape == AlpsArrangementShape.Rectangle ? outward : 0f;
                var forward = settings.shape == AlpsArrangementShape.Grid ? Vector3.forward * outward : Vector3.zero;

                if (!settings.width.hasSpread)
                {
                    var edge = Vector3.right * (settings.width.value * 0.5f + sideOffset) + lift + forward;
                    changed |= SlideDot(arrangement, root, edge, Vector3.right,
                        local => settings.width.value = ExtentAt(local.x, sideOffset, settings.width.limit));
                }

                if (!settings.depth.hasSpread)
                {
                    var edge = Vector3.forward * (settings.depth.value * 0.5f + outward) + lift;
                    changed |= SlideDot(arrangement, root, edge, Vector3.forward,
                        local => settings.depth.value = ExtentAt(local.z, outward, settings.depth.limit));
                }
            }

            return changed;
        }

        private static bool MovePoint(
            AlpsArrangement arrangement,
            Transform root,
            Quaternion handleRotation,
            Vector3 local,
            System.Action<Vector3> write)
        {
            EditorGUI.BeginChangeCheck();
            var moved = Handles.PositionHandle(root.TransformPoint(local), handleRotation);
            if (!EditorGUI.EndChangeCheck())
            {
                return false;
            }

            Commit(arrangement, () => write(root.InverseTransformPoint(moved)));
            return true;
        }

        private static bool SlideDot(
            AlpsArrangement arrangement,
            Transform root,
            Vector3 local,
            Vector3 localDirection,
            System.Action<Vector3> write)
        {
            var world = root.TransformPoint(local);
            var size = HandleUtility.GetHandleSize(world) * 0.08f;
            EditorGUI.BeginChangeCheck();
            Vector3 moved;
            using (new Handles.DrawingScope(SlotColor))
            {
                moved = Handles.Slider(world, root.TransformDirection(localDirection), size, Handles.DotHandleCap, 0f);
            }

            if (!EditorGUI.EndChangeCheck())
            {
                return false;
            }

            Commit(arrangement, () => write(root.InverseTransformPoint(moved)));
            return true;
        }

        private static void Commit(AlpsArrangement arrangement, System.Action edit)
        {
            Undo.RecordObjects(AlpsArrangementLayout.UndoTargets(arrangement), "Move Arrangement Handle");
            edit();
            AlpsArrangementLayout.CommitEdit(arrangement);
        }

        /// <summary>A value as the outline reads it: the value, or the first slot's while S is on.</summary>
        private static float Plain(AlpsAnimatableValue value)
        {
            return value.hasSpread ? value.spreadRange.x : value.value;
        }

        private static float[] PlainValues(AlpsArrangementSettings settings)
        {
            var values = new float[AlpsArrangementEvaluator.ValueCount];
            for (var i = 0; i < values.Length; i++)
            {
                var parameter = settings.Parameter(i);
                values[i] = parameter != null ? Plain(parameter) : 0f;
            }

            return values;
        }
    }
}
