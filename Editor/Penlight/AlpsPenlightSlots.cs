using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Keeps the hidden slot objects below a penlight area at the count the area asks for.
    /// A slot is a fixture the show drives, and fixtures are components, so each needs an
    /// object, but there is nothing on one to look at or move.
    /// </summary>
    public static class AlpsPenlightSlots
    {
        /// <summary>
        /// Adds or removes slots at the end until the area has <see cref="AlpsPenlightArea.slots"/>
        /// of them. Returns whether anything changed.
        /// </summary>
        public static bool Sync(AlpsPenlightArea area, bool recordUndo)
        {
            if (area == null)
            {
                return false;
            }

            var slots = area.Slots();
            var wanted = Mathf.Clamp(area.slots, 1, AlpsShowPlayer.GpuMaxPenlightSlots);
            if (slots.Count == wanted)
            {
                return false;
            }

            for (var i = slots.Count; i < wanted; i++)
            {
                var slot = new GameObject(AlpsPenlightArea.SlotName) { hideFlags = HideFlags.HideInHierarchy };
                slot.transform.SetParent(area.transform, false);
                slot.AddComponent<AlpsPenlightSlot>();
                if (recordUndo)
                {
                    Undo.RegisterCreatedObjectUndo(slot, "Edit Penlight Area");
                }
            }

            for (var i = slots.Count - 1; i >= wanted; i--)
            {
                if (recordUndo)
                {
                    Undo.DestroyObjectImmediate(slots[i].gameObject);
                }
                else
                {
                    Object.DestroyImmediate(slots[i].gameObject);
                }
            }

            // The slots are the fixtures a track bound here drives.
            AlpsPreviewDriver.MarkDirty();
            return true;
        }
    }
}
