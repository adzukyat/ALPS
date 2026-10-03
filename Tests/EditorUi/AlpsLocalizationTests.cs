using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Holds the translation tables to the English base: every language defines exactly the
    /// same keys, with no missing translations and no orphans, and no value is left blank.
    /// </summary>
    public class AlpsLocalizationTests
    {
        private static readonly AlpsLanguage[] Translations = { AlpsLanguage.Japanese, AlpsLanguage.Korean };

        [Test]
        public void EveryLanguageDefinesTheSameKeysAsEnglish()
        {
            var baseKeys = new HashSet<string>(AlpsStrings.Keys());

            foreach (var language in Translations)
            {
                var keys = new HashSet<string>(AlpsStrings.Keys(language));

                var missing = baseKeys.Except(keys).OrderBy(k => k).ToArray();
                Assert.IsEmpty(missing, $"{language} is missing: {string.Join(", ", missing)}");

                var orphans = keys.Except(baseKeys).OrderBy(k => k).ToArray();
                Assert.IsEmpty(orphans, $"{language} has keys English lacks: {string.Join(", ", orphans)}");
            }
        }

        [Test]
        public void EveryValueIsNonEmpty()
        {
            foreach (var language in new[] { AlpsLanguage.English }.Concat(Translations))
            {
                var saved = AlpsStrings.Language;
                try
                {
                    AlpsStrings.Language = language;
                    foreach (var key in AlpsStrings.Keys(language))
                    {
                        Assert.IsFalse(string.IsNullOrEmpty(AlpsStrings.Tr(key)), $"{language}:{key} is empty.");
                    }
                }
                finally
                {
                    AlpsStrings.Language = saved;
                }
            }
        }

        [Test]
        public void UnknownKeyFallsBackToTheKeyItself()
        {
            var saved = AlpsStrings.Language;
            try
            {
                AlpsStrings.Language = AlpsLanguage.Korean;
                Assert.AreEqual("this.key.does.not.exist", AlpsStrings.Tr("this.key.does.not.exist"));
            }
            finally
            {
                AlpsStrings.Language = saved;
            }
        }

        [Test]
        public void MultiEditMessageKeepsBothPlaceholders()
        {
            var saved = AlpsStrings.Language;
            try
            {
                foreach (var language in new[] { AlpsLanguage.English }.Concat(Translations))
                {
                    AlpsStrings.Language = language;
                    var text = AlpsStrings.Tr("clip.multiEdit", 3, "—");
                    StringAssert.Contains("3", text);
                    StringAssert.Contains("—", text);
                }
            }
            finally
            {
                AlpsStrings.Language = saved;
            }
        }
    }
}
