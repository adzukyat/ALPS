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
    }
}
