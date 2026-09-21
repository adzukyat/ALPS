using UnityEditor;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Locates the package's stylesheets. The known package path is tried first so the
    /// common case costs one load. The asset-database search covers embedded copies and
    /// projects that vendor the package under Assets/.
    /// </summary>
    internal static class AlpsStyleSheets
    {
        private const string PackagePath = "Packages/me.adzuki.live-performance-system/Editor/Inspector/Resources/";

        public static StyleSheet Load(string name)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackagePath + name + ".uss");
            if (sheet != null)
            {
                return sheet;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:StyleSheet " + name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/" + name + ".uss"))
                {
                    sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    if (sheet != null)
                    {
                        return sheet;
                    }
                }
            }

            return null;
        }
    }
}
