using System.Collections.Generic;

namespace ManeuverForVRC.Ui
{
    /// <summary>Display metadata for the effect catalog, shared by card headers and the add list.</summary>
    public static class MfvEffectCatalog
    {
        public const string EvenSuffix = "（偶数）";
        public const string OddSuffix = "（奇数）";

        public static readonly MfvEffectKind[] Order =
        {
            MfvEffectKind.Move,
            MfvEffectKind.Cone,
            MfvEffectKind.Color,
            MfvEffectKind.Brightness,
            MfvEffectKind.Flicker,
            MfvEffectKind.Gobo,
        };

        private static readonly Dictionary<MfvEffectKind, string> Names =
            new Dictionary<MfvEffectKind, string>
            {
                { MfvEffectKind.Move, "ムーブ" },
                { MfvEffectKind.Cone, "コーン" },
                { MfvEffectKind.Color, "カラー" },
                { MfvEffectKind.Brightness, "明るさ" },
                { MfvEffectKind.Flicker, "フリッカー" },
                { MfvEffectKind.Gobo, "ゴボ" },
            };

        private static readonly Dictionary<MfvEffectKind, string> Descriptions =
            new Dictionary<MfvEffectKind, string>
            {
                { MfvEffectKind.Move, "灯体の向きを指定します。ユーザーの方向に追跡させることもできます。" },
                { MfvEffectKind.Cone, "光線の幅と長さを調整します。" },
                { MfvEffectKind.Color, "灯体の色を設定します。複数追加すると、複雑なアニメーションも制作できます。" },
                { MfvEffectKind.Brightness, "灯体の明るさを変更します。" },
                { MfvEffectKind.Flicker, "光がランダムにちらつきます。" },
                { MfvEffectKind.Gobo, "光に模様を投影します。回転させることもできます。" },
            };

        public static string GetName(MfvEffectKind kind)
        {
            return Names.TryGetValue(kind, out var name) ? name : kind.ToString();
        }

        public static string GetDescription(MfvEffectKind kind)
        {
            return Descriptions.TryGetValue(kind, out var description) ? description : string.Empty;
        }

        public static string GetParitySuffix(MfvParity parity)
        {
            switch (parity)
            {
                case MfvParity.Even: return EvenSuffix;
                case MfvParity.Odd: return OddSuffix;
                default: return string.Empty;
            }
        }

        public static string GetTitle(MfvEffectKind kind, MfvParity parity)
        {
            return GetName(kind) + GetParitySuffix(parity);
        }
    }
}
