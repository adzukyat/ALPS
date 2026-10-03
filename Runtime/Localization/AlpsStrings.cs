using System.Collections.Generic;

namespace AdzukiSoft.ALPS
{
    /// <summary>The languages the authoring UI is translated into. English is the base.</summary>
    public enum AlpsLanguage
    {
        English = 0,
        Japanese = 1,
        Korean = 2,
    }

    /// <summary>
    /// The authoring UI's text, keyed so every label, tooltip and option resolves through one
    /// place. English is the base and the fallback; Japanese and Korean are translations. The
    /// tables are split into partial files per language.
    ///
    /// They live in Runtime because the effect catalog, which the clip model shares, reads them
    /// too. Nothing resolves text while a show plays, so this is authoring only, and the editor
    /// owns the language choice (AlpsLanguageSettings) and sets <see cref="Language"/>.
    /// </summary>
    public static partial class AlpsStrings
    {
        /// <summary>The active language. The editor sets it from the user's preference.</summary>
        public static AlpsLanguage Language = AlpsLanguage.English;

        private static readonly Dictionary<string, string> English = BuildEnglish();
        private static readonly Dictionary<string, string> Japanese = BuildJapanese();
        private static readonly Dictionary<string, string> Korean = BuildKorean();

        /// <summary>
        /// The string for <paramref name="key"/> in the active language, the English string when
        /// it is untranslated, and the key itself when it is unknown.
        /// </summary>
        public static string Tr(string key)
        {
            var table = TableFor(Language);
            string value;
            if (table != null && table.TryGetValue(key, out value))
            {
                return value;
            }

            if (English.TryGetValue(key, out value))
            {
                return value;
            }

            return key;
        }

        /// <summary>As <see cref="Tr(string)"/>, then fills the {0}, {1}... placeholders.</summary>
        public static string Tr(string key, params object[] args)
        {
            return string.Format(Tr(key), args);
        }

        /// <summary>The keys the English table defines, for the test that holds every table to it.</summary>
        public static IEnumerable<string> Keys()
        {
            return English.Keys;
        }

        /// <summary>The keys <paramref name="language"/>'s table defines, for the key-parity test.</summary>
        public static IEnumerable<string> Keys(AlpsLanguage language)
        {
            var table = TableFor(language);
            return table != null ? (IEnumerable<string>)table.Keys : new string[0];
        }

        /// <summary>True when <paramref name="language"/> has its own string for <paramref name="key"/>.</summary>
        public static bool Has(AlpsLanguage language, string key)
        {
            var table = TableFor(language);
            return table != null && table.ContainsKey(key);
        }

        private static Dictionary<string, string> TableFor(AlpsLanguage language)
        {
            switch (language)
            {
                case AlpsLanguage.Japanese: return Japanese;
                case AlpsLanguage.Korean: return Korean;
                default: return English;
            }
        }
    }
}
