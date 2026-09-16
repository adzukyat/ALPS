using System;
using UnityEngine;

namespace ManeuverForVRC
{
    /// <summary>One entry of the color palette. The palette itself is the range.</summary>
    [Serializable]
    public class MfvColorStop
    {
        public bool isGradient;
        public Color color = Color.white;
        public Gradient gradient = new Gradient();

        public MfvColorStop() { }

        public MfvColorStop(Color color)
        {
            this.color = color;
        }

        public MfvColorStop(MfvColorStop other)
        {
            isGradient = other.isGradient;
            color = other.color;
            gradient = CopyGradient(other.gradient);
        }

        public Color Evaluate(float t)
        {
            return isGradient && gradient != null ? gradient.Evaluate(Mathf.Clamp01(t)) : color;
        }

        private static Gradient CopyGradient(Gradient source)
        {
            var copy = new Gradient();
            if (source != null)
            {
                copy.SetKeys(source.colorKeys, source.alphaKeys);
                copy.mode = source.mode;
            }

            return copy;
        }
    }

    /// <summary>
    /// One entry of the gobo palette, stored as the VRSL gobo number so the Udon player
    /// can play it without texture references. Number 1 is VRSL's open beam, which the
    /// palette shows as OFF.
    /// </summary>
    [Serializable]
    public class MfvGoboStop
    {
        public const int OffIndex = 1;
        public const int MaxIndex = 8;

        public int goboIndex = OffIndex;

        public MfvGoboStop() { }

        public MfvGoboStop(int goboIndex)
        {
            this.goboIndex = Mathf.Clamp(goboIndex, OffIndex, MaxIndex);
        }

        public MfvGoboStop(MfvGoboStop other)
        {
            goboIndex = other.goboIndex;
        }

        public bool IsOff => goboIndex <= OffIndex;
    }
}
