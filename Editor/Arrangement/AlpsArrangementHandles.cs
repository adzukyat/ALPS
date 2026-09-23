using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// What an arrangement shows and lets you drag in the Scene view: the path it places
    /// children on, each slot's number and facing, the line ends and the target as move
    /// handles, and dots that pull the radius, the width and the depth.
    ///
    /// A value with S on has no single size to pull, so its dot is left out. The math that
    /// turns a dragged point into a value is kept apart so tests can reach it.
    /// </summary>
    public static class AlpsArrangementHandles
    {
        private static readonly Color PathColor = new Color(0.85f, 0.64f, 0.26f, 0.9f);
        private static readonly Color SlotColor = new Color(0.85f, 0.64f, 0.26f, 1f);
        private static readonly Color FacingColor = new Color(0.36f, 0.62f, 0.95f, 1f);
        private static readonly Color TargetColor = new Color(0.95f, 0.45f, 0.45f, 1f);

        private const int CircleSegments = 64;

        /// <summary>The radius a dot dragged to <paramref name="local"/> sets, measured along the dot's direction.</summary>
        public static float RadiusAt(Vector3 local, float angle, Vector2 limit)
        {
            return Mathf.Clamp(Vector3.Dot(local, AlpsArrangementEvaluator.Direction(angle)), limit.x, limit.y);
        }

        /// <summary>The width or depth a dot dragged to <paramref name="coordinate"/> on its axis sets.</summary>
        public static float ExtentAt(float coordinate, Vector2 limit)
        {
            return Mathf.Clamp(Mathf.Abs(coordinate) * 2f, limit.x, limit.y);
        }

        /// <summary>
        /// The outline of the shape in local space, before offsets. Returns whether it closes
        /// back on its start.
        /// </summary>
        public static bool Outline(AlpsArrangementSettings settings, List<Vector3> points)
        {
            points.Clear();
            switch (settings.shape)
            {
                case AlpsArrangementShape.Line:
                    points.Add(settings.lineStart);
                    points.Add(settings.lineEnd);
                    return false;
                case AlpsArrangementShape.Circle:
                {
                    var sweep = Mathf.Clamp(Plain(settings.sweep), 0f, 360f);
                    var radius = Plain(settings.radius);
                    var angle = Plain(settings.angle);
                    var closed = sweep >= AlpsArrangementEvaluator.FullSweep;
                    var steps = Mathf.Max(2, Mathf.CeilToInt(CircleSegments * sweep / 360f));
                    for (var i = 0; i <= steps; i++)
                    {
                        if (closed && i == steps)
                        {
                            break;
                        }

                        points.Add(radius * AlpsArrangementEvaluator.Direction(angle + sweep * (i / (float)steps - 0.5f)));
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
                    var shape = settings.shape == AlpsArrangementShape.Grid
                        ? AlpsArrangementEvaluator.ShapeRectangle
                        : (int)settings.shape;
                    var corners = AlpsArrangementEvaluator.PolylineCorners(shape, layout[AlpsArrangementEvaluator.LayoutSides]);
                    for (var j = 0; j < corners; j++)
                    {
                        points.Add(AlpsArrangementEvaluator.PolylineCorner(shape, layout, values, j));
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
            DrawOutline(root, settings);
            DrawSlots(root);
            return DoHandles(arrangement, root, settings);
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

            using (new Handles.DrawingScope(PathColor))
            {
                Handles.DrawAAPolyLine(2f, points.ToArray());
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
                changed |= SlideDot(arrangement, root, direction * settings.radius.value, direction,
                    local => settings.radius.value = RadiusAt(local, angle, settings.radius.limit));
            }

            if (settings.shape == AlpsArrangementShape.Rectangle || settings.shape == AlpsArrangementShape.Grid)
            {
                if (!settings.width.hasSpread)
                {
                    changed |= SlideDot(arrangement, root, Vector3.right * settings.width.value * 0.5f, Vector3.right,
                        local => settings.width.value = ExtentAt(local.x, settings.width.limit));
                }

                if (!settings.depth.hasSpread)
                {
                    changed |= SlideDot(arrangement, root, Vector3.forward * settings.depth.value * 0.5f, Vector3.forward,
                        local => settings.depth.value = ExtentAt(local.z, settings.depth.limit));
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
