using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The VRSL fixture's own fields, and for a fixture that still draws with VRSL's materials
    /// a button that gives it ALPS's (<see cref="AlpsFixtureMaterials"/>). Nothing switches a
    /// fixture on its own, so a world's fixtures keep their look until someone asks.
    /// </summary>
    [CustomEditor(typeof(AlpsVRSLFixture))]
    [CanEditMultipleObjects]
    public class AlpsVRSLFixtureEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!AnyCanApply())
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(AlpsStrings.Tr("fixture.useAlpsMaterials.info"), MessageType.Info);
            if (GUILayout.Button(AlpsStrings.Tr("fixture.useAlpsMaterials")))
            {
                foreach (var fixture in targets)
                {
                    AlpsFixtureMaterials.Apply(Root((AlpsVRSLFixture)fixture), undo: true);
                }
            }
        }

        private bool AnyCanApply()
        {
            foreach (var fixture in targets)
            {
                if (AlpsFixtureMaterials.CanApply(Root((AlpsVRSLFixture)fixture)))
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject Root(AlpsVRSLFixture fixture)
        {
            return fixture.target != null ? fixture.target.gameObject : fixture.gameObject;
        }
    }
}
