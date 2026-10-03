using System.Collections.Generic;

namespace AdzukiSoft.ALPS
{
    /// <summary>Display metadata for the effect catalog, shared by card headers and the add list.</summary>
    public static class AlpsEffectCatalog
    {
        /// <summary>
        /// The fixed order cards appear in on every clip, and the order of the add list.
        /// It is independent of the <see cref="AlpsEffectKind"/> values, which are serialized
        /// and mirrored by the evaluator. A new kind is appended to the enum but can be
        /// placed anywhere here.
        /// </summary>
        public static readonly AlpsEffectKind[] Order =
        {
            AlpsEffectKind.Move,
            AlpsEffectKind.Color,
            AlpsEffectKind.Brightness,
            AlpsEffectKind.Cone,
            AlpsEffectKind.Flicker,
            AlpsEffectKind.Gobo,
        };

        // The names and descriptions are localized, so the catalog holds the string keys and
        // resolves them through AlpsStrings each time the active language could differ.
        private static readonly Dictionary<AlpsEffectKind, string> NameKeys =
            new Dictionary<AlpsEffectKind, string>
            {
                { AlpsEffectKind.Move, "effect.move.name" },
                { AlpsEffectKind.Cone, "effect.cone.name" },
                { AlpsEffectKind.Color, "effect.color.name" },
                { AlpsEffectKind.Brightness, "effect.brightness.name" },
                { AlpsEffectKind.Flicker, "effect.flicker.name" },
                { AlpsEffectKind.Gobo, "effect.gobo.name" },
            };

        private static readonly Dictionary<AlpsEffectKind, string> DescriptionKeys =
            new Dictionary<AlpsEffectKind, string>
            {
                { AlpsEffectKind.Move, "effect.move.desc" },
                { AlpsEffectKind.Cone, "effect.cone.desc" },
                { AlpsEffectKind.Color, "effect.color.desc" },
                { AlpsEffectKind.Brightness, "effect.brightness.desc" },
                { AlpsEffectKind.Flicker, "effect.flicker.desc" },
                { AlpsEffectKind.Gobo, "effect.gobo.desc" },
            };

        /// <summary>Position of <paramref name="kind"/> in <see cref="Order"/>. Unknown kinds sort last.</summary>
        public static int GetSortIndex(AlpsEffectKind kind)
        {
            var index = System.Array.IndexOf(Order, kind);
            return index >= 0 ? index : Order.Length;
        }

        public static string GetName(AlpsEffectKind kind)
        {
            return NameKeys.TryGetValue(kind, out var key) ? AlpsStrings.Tr(key) : kind.ToString();
        }

        public static string GetDescription(AlpsEffectKind kind)
        {
            return DescriptionKeys.TryGetValue(kind, out var key) ? AlpsStrings.Tr(key) : string.Empty;
        }

        public static string GetParitySuffix(AlpsParity parity)
        {
            switch (parity)
            {
                case AlpsParity.Even: return AlpsStrings.Tr("effect.parity.even");
                case AlpsParity.Odd: return AlpsStrings.Tr("effect.parity.odd");
                default: return string.Empty;
            }
        }

        public static string GetTitle(AlpsEffectKind kind, AlpsParity parity)
        {
            return GetName(kind) + GetParitySuffix(parity);
        }
    }
}
