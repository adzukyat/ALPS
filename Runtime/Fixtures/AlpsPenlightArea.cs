using System.Collections.Generic;
using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Where the audience's penlights take their colour from. The area is a row of slots the
    /// show drives like fixtures, and its shape says which slot a penlight reads from where it
    /// is: along a line, by its distance from the middle, or by its angle around it. A penlight
    /// between two slots blends them, and one past the ends reads the end.
    ///
    /// A lighting track bound here drives the slots in order, so a clip's delay runs along
    /// the shape. A show holds up to <see cref="AlpsShowPlayer.GpuMaxPenlightAreas"/> areas,
    /// and a penlight takes the brightest of them.
    ///
    /// The slots are hidden children the editor keeps at <see cref="slots"/>. Like every
    /// target the area only exists while editing: the show player keeps its transform.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ALPS/ALPS Penlight Area")]
    public class AlpsPenlightArea : AlpsTarget
    {
        public const string SlotName = "ALPS Penlight Slot";

        public AlpsPenlightShape shape = AlpsPenlightShape.Line;

        /// <summary>
        /// The line's length or the circle's radius, in the area's own units. The angle shape
        /// reads none.
        /// </summary>
        [Min(0.01f)] public float size = 10f;

        /// <summary>How many steps the shape is cut into, which is how many fixtures a clip counts.</summary>
        [Range(1, AlpsShowPlayer.GpuMaxPenlightSlots)] public int slots = 16;

        /// <summary>The slots below the area, in the order the show counts them.</summary>
        public List<AlpsPenlightSlot> Slots()
        {
            var result = new List<AlpsPenlightSlot>();
            foreach (var slot in GetComponentsInChildren<AlpsPenlightSlot>(true))
            {
                if (slot.Area == this)
                {
                    result.Add(slot);
                }
            }

            return result;
        }

        public override void CollectFixtures(List<AlpsFixture> into)
        {
            foreach (var slot in Slots())
            {
                into.Add(slot);
            }
        }

        /// <summary>
        /// Where a penlight at <paramref name="world"/> reads along the slots, 0 at the first
        /// and 1 at the last. The penlight shader works out the same from the penlight texture.
        /// </summary>
        public float PositionOf(Vector3 world)
        {
            return PositionOf(transform.worldToLocalMatrix.MultiplyPoint3x4(world), shape, size);
        }

        /// <summary>The same for a point in the area's own space.</summary>
        public static float PositionOf(Vector3 local, AlpsPenlightShape shape, float size)
        {
            size = Mathf.Max(size, 0.0001f);
            switch (shape)
            {
                case AlpsPenlightShape.Radial:
                    return new Vector2(local.x, local.z).magnitude / size;
                case AlpsPenlightShape.Angle:
                    return Mathf.Atan2(local.x, local.z) / (2f * Mathf.PI) + 0.5f;
                default:
                    return local.x / size + 0.5f;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.82f, 0.92f, 0.33f);
            var count = Mathf.Max(1, slots);
            if (shape == AlpsPenlightShape.Line)
            {
                var half = size * 0.5f;
                Gizmos.DrawLine(new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f));
                for (var i = 0; i <= count; i++)
                {
                    var x = -half + size * i / count;
                    var tick = i == 0 ? 0.5f : 0.15f;
                    Gizmos.DrawLine(new Vector3(x, 0f, -tick), new Vector3(x, 0f, tick));
                }

                return;
            }

            if (shape == AlpsPenlightShape.Radial)
            {
                for (var i = 1; i <= count; i++)
                {
                    DrawCircle(size * i / count);
                }

                return;
            }

            // The angle starts behind the middle and turns once round.
            var radius = Mathf.Max(1f, size);
            DrawCircle(radius);
            for (var i = 0; i < count; i++)
            {
                var angle = (i / (float)count - 0.5f) * 2f * Mathf.PI;
                var reach = i == 0 ? radius * 1.15f : radius;
                Gizmos.DrawLine(Vector3.zero, new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * reach);
            }
        }

        private static void DrawCircle(float radius)
        {
            const int Segments = 48;
            var previous = new Vector3(0f, 0f, radius);
            for (var i = 1; i <= Segments; i++)
            {
                var angle = i / (float)Segments * 2f * Mathf.PI;
                var next = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
