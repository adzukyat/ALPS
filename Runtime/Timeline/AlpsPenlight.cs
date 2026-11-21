using System.Collections.Generic;
using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Lays the penlight areas of a compiled show out as the penlight section of the show
    /// data (<see cref="AlpsShowPlayer.GpuPenlightAreaStride"/>): which of the show's fixtures
    /// sits on each slot of each area, and how the area turns a place into a slot.
    /// </summary>
    public static class AlpsPenlight
    {
        public const string GridShader = "Hidden/ALPS/Penlight Grid";

        /// <summary>
        /// Finds the areas of the slots the show drives, in the order it meets them, and fills
        /// <see cref="AlpsCompiledShow.penlight"/> and <see cref="AlpsCompiledShow.penlightAreas"/>.
        /// </summary>
        public static void Compile(AlpsCompiledShow show)
        {
            show.penlightAreas.Clear();
            foreach (var fixture in show.fixtures)
            {
                var slot = fixture as AlpsPenlightSlot;
                var area = slot != null ? slot.Area : null;
                if (area != null && !show.penlightAreas.Contains(area))
                {
                    show.penlightAreas.Add(area);
                }
            }

            if (show.penlightAreas.Count > AlpsShowPlayer.GpuMaxPenlightAreas)
            {
                show.errors.Add($"A show holds {AlpsShowPlayer.GpuMaxPenlightAreas} penlight areas at most, this one has {show.penlightAreas.Count}.");
                show.penlightAreas.RemoveRange(AlpsShowPlayer.GpuMaxPenlightAreas, show.penlightAreas.Count - AlpsShowPlayer.GpuMaxPenlightAreas);
            }

            var stride = AlpsShowPlayer.GpuPenlightAreaStride;
            show.penlight = new float[show.penlightAreas.Count * stride];
            for (var a = 0; a < show.penlightAreas.Count; a++)
            {
                var area = show.penlightAreas[a];
                var slots = area.Slots();
                if (slots.Count > AlpsShowPlayer.GpuMaxPenlightSlots)
                {
                    show.errors.Add($"A penlight area holds {AlpsShowPlayer.GpuMaxPenlightSlots} slots at most, '{area.name}' has {slots.Count}.");
                }

                var fixtures = new int[Mathf.Min(slots.Count, AlpsShowPlayer.GpuMaxPenlightSlots)];
                for (var s = 0; s < fixtures.Length; s++)
                {
                    fixtures[s] = show.fixtures.IndexOf(slots[s]);
                }

                WriteArea(show.penlight, a * stride, fixtures, area.shape, area.size, area.transform.worldToLocalMatrix);
            }
        }

        /// <summary>One area of the penlight section, with <paramref name="fixtures"/> on its slots in order.</summary>
        public static void WriteArea(float[] penlight, int offset, int[] fixtures, AlpsPenlightShape shape, float size, Matrix4x4 worldToArea)
        {
            penlight[offset + AlpsShowPlayer.PenlightSlotCount] = fixtures.Length;
            penlight[offset + AlpsShowPlayer.PenlightShape] = (int)shape;
            penlight[offset + AlpsShowPlayer.PenlightSize] = size;
            AlpsShowPlayer.WriteAim(worldToArea, penlight, offset + AlpsShowPlayer.PenlightMatrix);
            for (var s = 0; s < AlpsShowPlayer.GpuMaxPenlightSlots; s++)
            {
                penlight[offset + AlpsShowPlayer.PenlightSlots + s] = s < fixtures.Length ? fixtures[s] : -1;
            }
        }

        /// <summary>The areas' transforms, which the show player takes their matrices from when it starts.</summary>
        public static Transform[] Transforms(AlpsCompiledShow show)
        {
            var transforms = new Transform[show.penlightAreas.Count];
            for (var a = 0; a < transforms.Length; a++)
            {
                transforms[a] = show.penlightAreas[a] != null ? show.penlightAreas[a].transform : null;
            }

            return transforms;
        }

        /// <summary>
        /// Whether an area moved, or changed its shape or size, since the show was compiled.
        /// Nothing rebuilds a preview for that on its own.
        /// </summary>
        public static bool Moved(AlpsCompiledShow show)
        {
            var stride = AlpsShowPlayer.GpuPenlightAreaStride;
            var current = new float[stride];
            for (var a = 0; a < show.penlightAreas.Count; a++)
            {
                var area = show.penlightAreas[a];
                if (area == null)
                {
                    return true;
                }

                WriteArea(current, 0, new int[0], area.shape, area.size, area.transform.worldToLocalMatrix);
                for (var i = AlpsShowPlayer.PenlightShape; i < AlpsShowPlayer.PenlightSlots; i++)
                {
                    if (current[i] != show.penlight[a * stride + i])
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
