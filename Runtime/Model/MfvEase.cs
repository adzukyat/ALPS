namespace ManeuverForVRC
{
    /// <summary>
    /// Normalized easing functions (0..1 -> 0..1). The curves live in
    /// <see cref="MfvShowEvaluator.Ease"/>, which the Udon player also runs, so a thumbnail
    /// tile always draws the curve that will play.
    /// </summary>
    public static class MfvEase
    {
        public static float Evaluate(MfvEaseType type, float t)
        {
            return MfvShowEvaluator.Ease((int)type, t);
        }
    }
}
