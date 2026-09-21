using System.Collections.Generic;

namespace AdzukiSoft.ALPS
{
    /// <summary>Display metadata for the effect catalog, shared by card headers and the add list.</summary>
    public static class AlpsEffectCatalog
    {
        public const string EvenSuffix = "（偶数）";
        public const string OddSuffix = "（奇数）";

        public static readonly AlpsEffectKind[] Order =
        {
            AlpsEffectKind.Move,
            AlpsEffectKind.Cone,
            AlpsEffectKind.Color,
            AlpsEffectKind.Brightness,
            AlpsEffectKind.Flicker,
            AlpsEffectKind.Gobo,
        };

        private static readonly Dictionary<AlpsEffectKind, string> Names =
            new Dictionary<AlpsEffectKind, string>
            {
                { AlpsEffectKind.Move, "ムーブ" },
                { AlpsEffectKind.Cone, "コーン" },
                { AlpsEffectKind.Color, "カラー" },
                { AlpsEffectKind.Brightness, "明るさ" },
                { AlpsEffectKind.Flicker, "フリッカー" },
                { AlpsEffectKind.Gobo, "ゴボ" },
            };

        private static readonly Dictionary<AlpsEffectKind, string> Descriptions =
            new Dictionary<AlpsEffectKind, string>
            {
                { AlpsEffectKind.Move, "灯体の向きを指定します。円を描いたり、ユーザーの方向に追跡させることもできます。" },
                { AlpsEffectKind.Cone, "光線の幅と長さを調整します。" },
                { AlpsEffectKind.Color, "灯体の色を設定します。複数追加すると、複雑なアニメーションも制作できます。" },
                { AlpsEffectKind.Brightness, "灯体の明るさを変更します。" },
                { AlpsEffectKind.Flicker, "光がランダムにちらつきます。" },
                { AlpsEffectKind.Gobo, "光に模様を投影します。回転させることもできます。" },
            };

        public static string GetName(AlpsEffectKind kind)
        {
            return Names.TryGetValue(kind, out var name) ? name : kind.ToString();
        }

        public static string GetDescription(AlpsEffectKind kind)
        {
            return Descriptions.TryGetValue(kind, out var description) ? description : string.Empty;
        }

        public static string GetParitySuffix(AlpsParity parity)
        {
            switch (parity)
            {
                case AlpsParity.Even: return EvenSuffix;
                case AlpsParity.Odd: return OddSuffix;
                default: return string.Empty;
            }
        }

        public static string GetTitle(AlpsEffectKind kind, AlpsParity parity)
        {
            return GetName(kind) + GetParitySuffix(parity);
        }
    }
}
