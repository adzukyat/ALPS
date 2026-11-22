using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Lists the fixtures of the VR Stage Lighting package in the open scenes, by what they
    /// are, and converts the kinds that are ticked (<see cref="AlpsVrslMigration"/>).
    /// </summary>
    public sealed class AlpsVrslMigrationWindow : EditorWindow
    {
        private const int MenuPriority = 20;

        private static readonly string[] SourceKeys =
        {
            "migrate.source.alps", "migrate.source.dmx", "migrate.source.audioLink",
        };

        private readonly bool[] _include = { true, true, true };
        private List<AlpsVrslMigrationItem> _items = new List<AlpsVrslMigrationItem>();
        private Vector2 _scroll;

        [MenuItem(AlpsMainMenu.Root + "Migrate VRSL Fixtures", false, MenuPriority)]
        private static void Open()
        {
            var window = GetWindow<AlpsVrslMigrationWindow>(true);
            window.titleContent = new GUIContent(AlpsStrings.Tr("migrate.title"));
            window.minSize = new Vector2(420f, 320f);
            window.Refresh();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnHierarchyChange()
        {
            Refresh();
            Repaint();
        }

        private void Refresh()
        {
            _items = AlpsVrslMigration.Find();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(AlpsStrings.Tr("migrate.help"), MessageType.Info);

            var selected = 0;
            for (var source = 0; source < SourceKeys.Length; source++)
            {
                var count = _items.FindAll(item => (int)item.Source == source).Count;
                using (new EditorGUI.DisabledScope(count == 0))
                {
                    _include[source] = EditorGUILayout.ToggleLeft($"{AlpsStrings.Tr(SourceKeys[source])} ({count})", _include[source]);
                }

                if (_include[source])
                {
                    selected += count;
                }
            }

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var item in _items)
            {
                if (item.Root == null || !_include[(int)item.Source])
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(item.Root, typeof(GameObject), true);
                    }

                    GUILayout.Label("→ " + AlpsStrings.Tr(item.LabelKey), GUILayout.Width(150f));
                }
            }

            if (_items.Count == 0)
            {
                GUILayout.Label(AlpsStrings.Tr("migrate.none"), EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(selected == 0))
            {
                if (GUILayout.Button(AlpsStrings.Tr("migrate.convert", selected), GUILayout.Height(28f)))
                {
                    AlpsVrslMigration.Convert(_items.FindAll(item => _include[(int)item.Source]));
                    Refresh();
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
