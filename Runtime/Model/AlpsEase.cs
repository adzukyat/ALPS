namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Normalized easing functions (0..1 -> 0..1). The curves live in
    /// <see cref="AlpsShowEvaluator.Ease"/>, which the Udon player also runs, so a thumbnail
    /// tile always draws the curve that will play.
    /// </summary>
    public static class AlpsEase
    {
        public static float Evaluate(AlpsEaseType type, float t)
        {
            return AlpsShowEvaluator.Ease((int)type, t);
        }

        /// <summary>
        /// The curve that plays <paramref name="type"/> backwards, 1 - ease(1 - t): In and Out
        /// trade places, and Linear and the InOut curves are their own reverse.
        /// </summary>
        public static AlpsEaseType Reversed(AlpsEaseType type)
        {
            switch (type)
            {
                case AlpsEaseType.InSine: return AlpsEaseType.OutSine;
                case AlpsEaseType.OutSine: return AlpsEaseType.InSine;
                case AlpsEaseType.InQuad: return AlpsEaseType.OutQuad;
                case AlpsEaseType.OutQuad: return AlpsEaseType.InQuad;
                case AlpsEaseType.InCubic: return AlpsEaseType.OutCubic;
                case AlpsEaseType.OutCubic: return AlpsEaseType.InCubic;
                case AlpsEaseType.InExpo: return AlpsEaseType.OutExpo;
                case AlpsEaseType.OutExpo: return AlpsEaseType.InExpo;
                case AlpsEaseType.InBack: return AlpsEaseType.OutBack;
                case AlpsEaseType.OutBack: return AlpsEaseType.InBack;
                case AlpsEaseType.InBounce: return AlpsEaseType.OutBounce;
                case AlpsEaseType.OutBounce: return AlpsEaseType.InBounce;
                default: return type;
            }
        }
    }
}
