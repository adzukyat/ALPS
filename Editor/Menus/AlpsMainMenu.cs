using UnityEditor;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The ALPS menu in the editor's menu bar: About first, then a Debug submenu for the
    /// tools that rebuild the demo and the test fixtures, which only the projects that
    /// develop ALPS compile.
    /// </summary>
    public static class AlpsMainMenu
    {
        public const string Root = "ALPS/";

        public const string DebugRoot = Root + "Debug/";

        public const string RepositoryUrl = "https://github.com/adzukyat/ALPS";

        private const int AboutPriority = 0;

        // The gap from About draws a separator above Debug.
        public const int DebugPriority = 100;

        [MenuItem(Root + "About ALPS", false, AboutPriority)]
        private static void About()
        {
            Application.OpenURL(RepositoryUrl);
        }
    }
}
