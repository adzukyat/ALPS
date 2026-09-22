using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Renders the clip inspector's text with the same fonts IMGUI uses around it.
    /// </summary>
    internal static class AlpsInspectorFont
    {
        private static FontAsset _font;
        private static bool _fontBuilt;

        /// <summary>Sets the font on the root. Without a usable font the theme's default stays.</summary>
        public static void Apply(VisualElement root)
        {
            var font = ImguiFont();
            if (font != null)
            {
                root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(font));
            }
        }

        /// <summary>
        /// Inter with the OS font IMGUI falls back to for everything Inter lacks, Japanese
        /// included. IMGUI takes the first of the editor font's fallback names that is
        /// installed, so on a Japanese Windows "Yu Gothic UI Regular" misses and Meiryo UI is
        /// used. UI Toolkit's own fallback picks Yu Gothic UI instead, and mixes its weights.
        /// </summary>
        private static FontAsset ImguiFont()
        {
            if (_fontBuilt)
            {
                return _font;
            }

            _fontBuilt = true;

            var regular = FromFont(EditorStyles.standardFont);
            if (regular == null)
            {
                return null;
            }

            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            var fallback = FromOs(EditorStyles.standardFont, installed, bold: false);
            if (fallback != null)
            {
                regular.fallbackFontAssetTable = new List<FontAsset> { fallback };
            }

            var bold = FromFont(EditorStyles.boldFont);
            if (bold != null)
            {
                var boldFallback = FromOs(EditorStyles.boldFont, installed, bold: true);
                if (boldFallback != null)
                {
                    bold.fallbackFontAssetTable = new List<FontAsset> { boldFallback };
                    if (fallback != null)
                    {
                        SetBold(fallback, boldFallback);
                    }
                }

                SetBold(regular, bold);
            }

            _font = regular;
            return _font;
        }

        private static FontAsset FromFont(Font font)
        {
            if (font == null)
            {
                return null;
            }

            var asset = FontAsset.CreateFontAsset(font);
            if (asset != null)
            {
                asset.hideFlags = HideFlags.DontSave;
            }

            return asset;
        }

        /// <summary>
        /// The editor fills the font's fallback names in lazily, and until it has they hold
        /// only "Inter", which the editor also registers as an installed font. So a candidate
        /// only counts when it really has kana, and the platform's own UI font backs the list.
        /// </summary>
        private static FontAsset FromOs(Font editorFont, HashSet<string> installed, bool bold)
        {
            var defaults = Application.platform == RuntimePlatform.OSXEditor
                ? new[] { "Hiragino Sans" }
                : new[] { bold ? "Meiryo UI Bold" : "Meiryo UI", bold ? "Yu Gothic UI Bold" : "Yu Gothic UI" };
            var names = (editorFont?.fontNames ?? Array.Empty<string>()).Concat(defaults);

            foreach (var name in names.Where(n => installed.Contains(n) || defaults.Contains(n)))
            {
                // Installed names carry the style ("Meiryo UI Bold"), the family lookup does not.
                var family = name;
                if (bold && family.EndsWith(" Bold", StringComparison.Ordinal))
                {
                    family = family.Substring(0, family.Length - " Bold".Length);
                }

                var styles = bold ? new[] { "Bold", "W6", "Semibold" } : new[] { "Regular", "W3", "Normal" };
                foreach (var style in styles)
                {
                    var asset = FontAsset.CreateFontAsset(family, style, 90);
                    if (asset != null && asset.HasCharacter('あ', false, true))
                    {
                        asset.hideFlags = HideFlags.DontSave;
                        return asset;
                    }
                }
            }

            return null;
        }

        /// <summary>`-unity-font-style: bold` takes weight 700 from this table rather than synthesising it.</summary>
        private static void SetBold(FontAsset regular, FontAsset bold)
        {
            var table = regular.fontWeightTable;
            if (table != null && table.Length > 7)
            {
                table[7].regularTypeface = bold;
            }
        }
    }
}
