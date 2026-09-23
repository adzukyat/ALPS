using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VRC.Udon;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Points everything else in the scene that names an authoring timeline at its build
    /// copy, such as a player that assigns the timeline to a director itself. Without this
    /// the scene would still play the authoring timeline, ALPS tracks included.
    /// </summary>
    public static class AlpsBuildReferences
    {
        /// <summary>Returns how many references were switched to a build timeline.</summary>
        public static int Retarget(UnityEngine.SceneManagement.Scene scene, Dictionary<TimelineAsset, TimelineAsset> builds)
        {
            if (builds.Count == 0)
            {
                return 0;
            }

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    switch (component)
                    {
                        case null:
                        case AlpsTarget _:
                            break;
                        case PlayableDirector director:
                            foreach (var pair in builds)
                            {
                                AlpsBuildTimeline.CarryBindings(director, pair.Key, pair.Value);
                            }

                            break;
                        case UdonBehaviour udon:
                            count += RetargetUdon(udon, builds);
                            break;
                        default:
                            count += RetargetSerialized(component, builds);
                            break;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Udon keeps its public variables in its own table, serialized as one blob, so they
        /// are switched through the table rather than as serialized properties.
        /// </summary>
        private static int RetargetUdon(UdonBehaviour udon, Dictionary<TimelineAsset, TimelineAsset> builds)
        {
            var variables = udon.publicVariables;
            if (variables == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var symbol in variables.VariableSymbols.ToArray())
            {
                if (!variables.TryGetVariableValue(symbol, out object value))
                {
                    continue;
                }

                if (value is TimelineAsset source && builds.TryGetValue(source, out var build))
                {
                    if (variables.TrySetVariableValue(symbol, build))
                    {
                        count++;
                    }
                }
                else if (value is Object[] array)
                {
                    count += RetargetArray(array, builds);
                }
            }

            if (count > 0)
            {
                EditorUtility.SetDirty(udon);
            }

            return count;
        }

        private static int RetargetArray(Object[] array, Dictionary<TimelineAsset, TimelineAsset> builds)
        {
            var count = 0;
            for (var i = 0; i < array.Length; i++)
            {
                if (array[i] is TimelineAsset source && builds.TryGetValue(source, out var build)
                    && array.GetType().GetElementType().IsInstanceOfType(build))
                {
                    array[i] = build;
                    count++;
                }
            }

            return count;
        }

        private static int RetargetSerialized(Component component, Dictionary<TimelineAsset, TimelineAsset> builds)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            var count = 0;
            var enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = MayHoldReferences(property);
                if (property.propertyType == SerializedPropertyType.ObjectReference
                    && property.objectReferenceValue is TimelineAsset source
                    && builds.TryGetValue(source, out var build))
                {
                    property.objectReferenceValue = build;
                    count++;
                }
            }

            if (count > 0)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return count;
        }

        /// <summary>
        /// Arrays of plain values are skipped, since a filled show player carries large ones
        /// and none of their elements can name a timeline.
        /// </summary>
        private static bool MayHoldReferences(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.Generic)
            {
                return false;
            }

            if (!property.isArray)
            {
                return true;
            }

            if (property.arraySize == 0)
            {
                return false;
            }

            var element = property.GetArrayElementAtIndex(0).propertyType;
            return element == SerializedPropertyType.Generic || element == SerializedPropertyType.ObjectReference;
        }
    }
}
