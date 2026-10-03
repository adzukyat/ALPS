using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Owns the authoring UI's language. The choice is stored per user in EditorPrefs and pushed
    /// into <see cref="AlpsStrings.Language"/> on load and whenever it changes; Auto follows the
    /// editor's system language. Changing it rebuilds the open inspectors so their text updates.
    /// </summary>
    [InitializeOnLoad]
    internal static class AlpsLanguageSettings
    {
        private const string PrefKey = "AdzukiSoft.ALPS.Language";
        private const int Auto = -1;

        // The order the popup shows. Auto first, then the languages in AlpsLanguage order. The
        // language names are endonyms, shown the same whatever the active language.
        private static readonly int[] Options = { Auto, (int)AlpsLanguage.English, (int)AlpsLanguage.Japanese, (int)AlpsLanguage.Korean };
        private static readonly string[] Endonyms = { null, "English", "日本語", "한국어" };

        static AlpsLanguageSettings()
        {
            Apply();
        }

        private static int Stored
        {
            get { return EditorPrefs.GetInt(PrefKey, Auto); }
            set { EditorPrefs.SetInt(PrefKey, value); }
        }

        /// <summary>Sets <see cref="AlpsStrings.Language"/> from the stored preference.</summary>
        public static void Apply()
        {
            AlpsStrings.Language = Resolve(Stored);
        }

        private static AlpsLanguage Resolve(int stored)
        {
            return stored < 0 ? FromSystem() : (AlpsLanguage)stored;
        }

        private static AlpsLanguage FromSystem()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Japanese: return AlpsLanguage.Japanese;
                case SystemLanguage.Korean: return AlpsLanguage.Korean;
                default: return AlpsLanguage.English;
            }
        }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Preferences/ALPS", SettingsScope.User)
            {
                label = "ALPS",
                guiHandler = _ => OnGui(),
                keywords = new HashSet<string>(new[] { "ALPS", "Language", "言語", "언어" }),
            };
        }

        private static void OnGui()
        {
            var labels = new string[Options.Length];
            for (var i = 0; i < Options.Length; i++)
            {
                labels[i] = Endonyms[i] ?? AlpsStrings.Tr("settings.language.auto");
            }

            var current = System.Array.IndexOf(Options, Stored);
            if (current < 0)
            {
                current = 0;
            }

            EditorGUI.BeginChangeCheck();
            var picked = EditorGUILayout.Popup(AlpsStrings.Tr("settings.language"), current, labels);
            if (EditorGUI.EndChangeCheck())
            {
                Stored = Options[picked];
                Apply();
                RebuildInspectors();
            }
        }

        /// <summary>Recreates the open inspector editors so their UI Toolkit views pick up the new text.</summary>
        private static void RebuildInspectors()
        {
            AlpsInspectorFont.Invalidate();
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            InternalEditorUtility.RepaintAllViews();
        }
    }
}
