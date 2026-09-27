using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
#endif

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// What the DMX grid has to allow for in the VRSL version installed. VRSL before 2.7 reads
    /// every DMX value through LinearToGammaSpaceExact, and turns the gobo spin phase into an
    /// angle without the factor of 4 later versions apply. The grid material carries which of
    /// the two it writes for, found in the editor from VRSL's own shader code and saved into
    /// the material a build uses.
    /// </summary>
    public static class AlpsVrslVersion
    {
        /// <summary>The grid material's switch, 1 for VRSL before 2.7.</summary>
        public const string LegacyProperty = "_AlpsVrslLegacy";

        /// <summary>
        /// Whether the VRSL in the project is older than 2.7, read once per domain. Outside the
        /// editor the player carries what the build found instead.
        /// </summary>
        public static bool IsLegacy
        {
            get
            {
#if UNITY_EDITOR
                if (_legacy == null)
                {
                    _legacy = FindLegacy();
                }

                return _legacy.Value;
#else
                return false;
#endif
            }
        }

#if UNITY_EDITOR
        private const string DmxFunctionsFile = "VRSL-DMXFunctions.cginc";
        private static readonly Regex GammaRead = new Regex(@"^\s*value\s*=\s*LinearToGammaSpaceExact\s*\(\s*value\s*\)", RegexOptions.Multiline);
        private static bool? _legacy;

        private static bool FindLegacy()
        {
            foreach (var guid in AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(DmxFunctionsFile)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(DmxFunctionsFile))
                {
                    continue;
                }

                var full = Path.GetFullPath(path);
                if (File.Exists(full))
                {
                    return GammaRead.IsMatch(File.ReadAllText(full));
                }
            }

            return false;
        }
#endif

        /// <summary>
        /// Sets the grid material up for the VRSL in the project and returns whether it
        /// changed. Outside the editor the material keeps what the build saved into it.
        /// </summary>
        public static bool Apply(Material gridMaterial)
        {
#if UNITY_EDITOR
            if (gridMaterial == null)
            {
                return false;
            }

            var value = IsLegacy ? 1f : 0f;
            if (gridMaterial.GetFloat(LegacyProperty) == value)
            {
                return false;
            }

            gridMaterial.SetFloat(LegacyProperty, value);
            return true;
#else
            return false;
#endif
        }
    }
}
