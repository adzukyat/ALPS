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
        private static bool _watching;
        private static readonly List<FontAsset> _built = new List<FontAsset>();

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
            if (_fontBuilt && _built.All(IsIntact))
            {
                return _font;
            }

            _fontBuilt = true;
            _font = null;
            _built.Clear();

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
            Keep(asset);
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
                        Keep(asset);
                        return asset;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The asset alone is not enough: its material and atlas are separate objects. Built
        /// in play mode (the domain reloads on entering it) without DontSave, they are
        /// destroyed on leaving it, and every text using the font then fails to render.
        /// </summary>
        private static void Keep(FontAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            asset.hideFlags = HideFlags.DontSave;
            if (asset.material != null)
            {
                asset.material.hideFlags = HideFlags.DontSave;
            }

            if (asset.atlasTextures != null)
            {
                foreach (var texture in asset.atlasTextures.Where(t => t != null))
                {
                    texture.hideFlags = HideFlags.DontSave;
                }
            }

            _built.Add(asset);
            if (!_watching)
            {
                _watching = true;
                EditorApplication.update += KeepNewAtlases;
            }
        }

        /// <summary>
        /// A dynamic asset opens another atlas page whenever the current one is full, which
        /// Japanese text at this sampling size does after a few dozen glyphs. The page is a
        /// plain new Texture2D, so it needs DontSave too before play mode ends.
        /// </summary>
        private static void KeepNewAtlases()
        {
            foreach (var asset in _built)
            {
                if (asset == null || asset.atlasTextures == null)
                {
                    continue;
                }

                foreach (var texture in asset.atlasTextures)
                {
                    if (texture != null && (texture.hideFlags & HideFlags.DontSave) != HideFlags.DontSave)
                    {
                        texture.hideFlags |= HideFlags.DontSave;
                    }
                }
            }
        }

        /// <summary>
        /// The atlas array grows by doubling, so its unused tail is really null. A page that
        /// was destroyed is only null by Unity's comparison, and its glyphs no longer render.
        /// </summary>
        private static bool IsIntact(FontAsset asset)
        {
            if (asset == null || asset.material == null)
            {
                return false;
            }

            return asset.atlasTextures == null
                || asset.atlasTextures.All(t => ReferenceEquals(t, null) || t != null);
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
