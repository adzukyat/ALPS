using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// Finds the IMGUI container that is drawing right now.
    ///
    /// The timeline clip inspector is IMGUI only: it calls OnInspectorGUI on the editor of
    /// the clip asset and never builds a UI Toolkit tree for it. Knowing which container is
    /// running lets an editor hang its own element next to that container, so a UI Toolkit
    /// inspector still shows up under the clip.
    /// </summary>
    internal static class MfvImguiHost
    {
        private static FieldInfo _containerStack;
        private static bool _resolved;

        /// <summary>False when this Unity version no longer exposes the container stack.</summary>
        internal static bool IsAvailable => Resolve() != null;

        /// <summary>The innermost container currently running its handler, or null outside IMGUI.</summary>
        internal static IMGUIContainer Current()
        {
            var stack = Resolve() != null ? Resolve().GetValue(null) as Stack<IMGUIContainer> : null;
            return stack != null && stack.Count > 0 ? stack.Peek() : null;
        }

        private static FieldInfo Resolve()
        {
            if (_resolved)
            {
                return _containerStack;
            }

            _resolved = true;
            var utility = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsUtility");
            var field = utility != null
                ? utility.GetField("s_ContainerStack", BindingFlags.Static | BindingFlags.NonPublic)
                : null;
            if (field != null && typeof(Stack<IMGUIContainer>).IsAssignableFrom(field.FieldType))
            {
                _containerStack = field;
            }

            return _containerStack;
        }
    }
}
