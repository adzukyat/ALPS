using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Keeps the hidden segment objects below a multi light bar at its twelve segments. A
    /// segment is a fixture the show drives, and fixtures are components, so each needs an
    /// object, but there is nothing on one to look at or move.
    /// </summary>
    public static class AlpsMultiLightBarSegments
    {
        private const string UndoName = "Set Up Multi Light Bar";

        /// <summary>
        /// Adds the segments the bar is missing, numbers them in order and removes the ones
        /// past <see cref="AlpsVRSLMultiLightBar.SegmentCount"/>. Returns whether anything
        /// changed.
        /// </summary>
        public static bool Sync(AlpsVRSLMultiLightBar bar, bool recordUndo)
        {
            if (bar == null)
            {
                return false;
            }

            var segments = bar.Segments();
            var changed = false;
            for (var i = segments.Count; i < AlpsVRSLMultiLightBar.SegmentCount; i++)
            {
                var segment = new GameObject(AlpsVRSLMultiLightBar.SegmentName) { hideFlags = HideFlags.HideInHierarchy };
                segment.transform.SetParent(bar.transform, false);
                segments.Add(segment.AddComponent<AlpsVRSLMultiLightBarSegment>());
                // Past the others for now, the numbering below puts it in its place.
                segments[i].index = int.MaxValue;
                if (recordUndo)
                {
                    Undo.RegisterCreatedObjectUndo(segment, UndoName);
                }

                changed = true;
            }

            for (var i = segments.Count - 1; i >= AlpsVRSLMultiLightBar.SegmentCount; i--)
            {
                if (recordUndo)
                {
                    Undo.DestroyObjectImmediate(segments[i].gameObject);
                }
                else
                {
                    Object.DestroyImmediate(segments[i].gameObject);
                }

                segments.RemoveAt(i);
                changed = true;
            }

            for (var i = 0; i < segments.Count; i++)
            {
                if (segments[i].index == i)
                {
                    continue;
                }

                if (recordUndo)
                {
                    Undo.RecordObject(segments[i], UndoName);
                }

                segments[i].index = i;
                EditorUtility.SetDirty(segments[i]);
                changed = true;
            }

            if (changed)
            {
                // The segments are the fixtures a track bound here drives.
                AlpsPreviewDriver.MarkDirty();
            }

            return changed;
        }
    }

    /// <summary>Gives a bar added by Add Component or a script its segments as soon as it is looked at.</summary>
    [CustomEditor(typeof(AlpsVRSLMultiLightBar))]
    [CanEditMultipleObjects]
    public class AlpsVRSLMultiLightBarEditor : UnityEditor.Editor
    {
        private void OnEnable()
        {
            foreach (var selected in targets)
            {
                AlpsMultiLightBarSegments.Sync(selected as AlpsVRSLMultiLightBar, recordUndo: true);
            }
        }
    }
}
