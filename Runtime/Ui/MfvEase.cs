using UnityEngine;

namespace ManeuverForVRC.Ui
{
    /// <summary>
    /// Normalized easing functions (0..1 -> 0..1). Shared by the runtime evaluator and
    /// by the editor thumbnail grid, so a tile always draws the curve that will play.
    /// </summary>
    public static class MfvEase
    {
        private const float BackC1 = 1.70158f;
        private const float BackC2 = BackC1 * 1.525f;
        private const float BackC3 = BackC1 + 1f;

        public static float Evaluate(MfvEaseType type, float t)
        {
            t = Mathf.Clamp01(t);
            switch (type)
            {
                case MfvEaseType.Linear: return t;
                case MfvEaseType.InQuad: return t * t;
                case MfvEaseType.OutQuad: return 1f - (1f - t) * (1f - t);
                case MfvEaseType.InOutQuad:
                    return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
                case MfvEaseType.InCubic: return t * t * t;
                case MfvEaseType.OutCubic: return 1f - Mathf.Pow(1f - t, 3f);
                case MfvEaseType.InOutCubic:
                    return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case MfvEaseType.InExpo:
                    return Mathf.Approximately(t, 0f) ? 0f : Mathf.Pow(2f, 10f * t - 10f);
                case MfvEaseType.OutExpo:
                    return Mathf.Approximately(t, 1f) ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case MfvEaseType.InSine: return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case MfvEaseType.OutSine: return Mathf.Sin(t * Mathf.PI * 0.5f);
                case MfvEaseType.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
                case MfvEaseType.InBack: return BackC3 * t * t * t - BackC1 * t * t;
                case MfvEaseType.OutBack:
                    return 1f + BackC3 * Mathf.Pow(t - 1f, 3f) + BackC1 * Mathf.Pow(t - 1f, 2f);
                case MfvEaseType.InOutBack:
                    return t < 0.5f
                        ? Mathf.Pow(2f * t, 2f) * ((BackC2 + 1f) * 2f * t - BackC2) * 0.5f
                        : (Mathf.Pow(2f * t - 2f, 2f) * ((BackC2 + 1f) * (t * 2f - 2f) + BackC2) + 2f) * 0.5f;
                case MfvEaseType.InBounce: return 1f - OutBounce(1f - t);
                case MfvEaseType.OutBounce: return OutBounce(t);
                case MfvEaseType.InOutBounce:
                    return t < 0.5f
                        ? (1f - OutBounce(1f - 2f * t)) * 0.5f
                        : (1f + OutBounce(2f * t - 1f)) * 0.5f;
                case MfvEaseType.Step: return t < 0.5f ? 0f : 1f;
                default: return t;
            }
        }

        private static float OutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }
}
