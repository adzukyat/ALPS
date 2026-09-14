using System.Collections.Generic;
using UnityEngine;

namespace ManeuverForVRC.Ui
{
    /// <summary>
    /// The gobo textures that ship with VRSL, mirrored into this package's Resources
    /// folder as Gobo1..Gobo8 so they can be loaded without a hard dependency on the
    /// VR Stage Lighting package's asset paths.
    /// </summary>
    public static class MfvGoboLibrary
    {
        private const string ResourcePath = "SLMAssets/GoboTextures/Gobo";

        /// <summary>How many built-in gobos VRSL provides.</summary>
        public const int BuiltInCount = 8;

        /// <summary>Every built-in gobo, in VRSL's own order. Missing files are skipped.</summary>
        public static List<Texture2D> LoadBuiltIn()
        {
            var textures = new List<Texture2D>(BuiltInCount);
            for (var i = 1; i <= BuiltInCount; i++)
            {
                var texture = Resources.Load<Texture2D>(ResourcePath + i);
                if (texture != null)
                {
                    textures.Add(texture);
                }
            }

            return textures;
        }
    }
}
