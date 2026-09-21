using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Thumbnails for the gobos VRSL projects, read straight from the VR Stage Lighting
    /// package so the palette always shows what the fixture will draw.
    /// </summary>
    public static class AlpsGoboLibrary
    {
        private const string Folder =
            "Packages/com.acchosen.vr-stage-lighting/Runtime/Textures/MoverLightTextures/GOBO/IndividualGobos/";

        /// <summary>File name suffixes in VRSL's own gobo order, starting at gobo 1.</summary>
        private static readonly string[] Names =
        {
            "Default",
            "Swooshes",
            "Sticks",
            "5PointStar",
            "Triangles",
            "8PointStar",
            "Spiral",
            "ApolloOrb",
        };

        private static readonly Texture2D[] Cache = new Texture2D[Names.Length];

        /// <summary>How many gobos VRSL provides. Gobo 1 is the open beam.</summary>
        public const int GoboCount = AlpsGoboStop.MaxIndex;

        /// <summary>Gobo numbers the picker offers after OFF.</summary>
        public const int FirstPatternIndex = AlpsGoboStop.OffIndex + 1;

        public static string GetName(int goboIndex)
        {
            return goboIndex <= AlpsGoboStop.OffIndex || goboIndex > GoboCount ? "OFF" : Names[goboIndex - 1];
        }

        /// <summary>The thumbnail for a gobo number, or null when VRSL is missing.</summary>
        public static Texture2D Load(int goboIndex)
        {
            if (goboIndex < 1 || goboIndex > GoboCount)
            {
                return null;
            }

            var slot = goboIndex - 1;
            if (Cache[slot] == null)
            {
                Cache[slot] = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "VRSL-SpotLightGOBO-" + Names[slot] + ".png");
            }

            return Cache[slot];
        }
    }
}
