using System;
using UnityEngine;

namespace ManeuverForVRC.Ui
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

    /// <summary>One entry of the gobo palette. A null texture is the OFF tile.</summary>
    [Serializable]
    public class MfvGoboStop
    {
        public Texture2D texture;

        public MfvGoboStop() { }

        public MfvGoboStop(Texture2D texture)
        {
            this.texture = texture;
        }

        public MfvGoboStop(MfvGoboStop other)
        {
            texture = other.texture;
        }

        public bool IsOff => texture == null;
    }
}
