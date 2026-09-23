using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// The shape of a phase over one cycle, for the inspector's graphs and easing tiles.
    /// The show itself is evaluated on the GPU (<c>Runtime/Gpu/AlpsEvaluator.hlsl</c>), which
    /// runs the same maths, so what these draw is what plays.
    /// </summary>
    public static class AlpsPhaseCurve
    {
        /// <summary>Normalized easing, 0..1 in and out (overshooting curves may leave the range).</summary>
        public static float Ease(int type, float t)
        {
            t = Mathf.Clamp01(t);
            if (type == 1) return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
            if (type == 2) return Mathf.Sin(t * Mathf.PI * 0.5f);
            if (type == 3) return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
            if (type == 4) return t * t;
            if (type == 5) return 1f - (1f - t) * (1f - t);
            if (type == 6) return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
            if (type == 7) return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
            if (type == 8) return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
            if (type == 9) return 1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f);
            if (type == 10) return OutBounce(t);
            if (type == 11) return t * t * t;
            if (type == 12) return 1f - Mathf.Pow(1f - t, 3f);
            if (type == 13) return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
            if (type == 14) return 2.70158f * t * t * t - 1.70158f * t * t;
            if (type == 15)
            {
                var c2 = 1.70158f * 1.525f;
                return t < 0.5f
                    ? Mathf.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2) * 0.5f
                    : (Mathf.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) * 0.5f;
            }

            if (type == 16) return 1f - OutBounce(1f - t);
            if (type == 17)
            {
                return t < 0.5f ? (1f - OutBounce(1f - 2f * t)) * 0.5f : (1f + OutBounce(2f * t - 1f)) * 0.5f;
            }

            return t;
        }

        private static float OutBounce(float t)
        {
            if (t < 1f / 2.75f) return 7.5625f * t * t;
            if (t < 2f / 2.75f) { t -= 1.5f / 2.75f; return 7.5625f * t * t + 0.75f; }
            if (t < 2.5f / 2.75f) { t -= 2.25f / 2.75f; return 7.5625f * t * t + 0.9375f; }
            t -= 2.625f / 2.75f;
            return 7.5625f * t * t + 0.984375f;
        }

        /// <summary>Cycles elapsed for a fixture at order position k, before wrapping.</summary>
        public static float FixtureCycles(float beats, float beatsPerCycle, float delay, int k, float extraCycles)
        {
            var cycles = beatsPerCycle > 0f ? beats / beatsPerCycle : 0f;
            return cycles - delay * k + extraCycles;
        }

        /// <summary>
        /// Phase φ in 0..1 from unwrapped cycles. A wave walks one cycle of
        /// <see cref="Wave"/>, random is a smooth seeded wander. Invert and the eases apply to
        /// the wave only. Invert flips the eased wave upside down.
        /// </summary>
        public static float Phase(int mode, int riseEase, int fallEase, float rise, float holdHigh, float fall, bool inverse, float cycles, int k, int seed)
        {
            if (mode == AlpsShowLayout.PhaseRandom)
            {
                return Mathf.Clamp01(Noise(cycles * 2f, (k + 1) * 7.31f + seed * 0.137f));
            }

            var u = Wave(riseEase, fallEase, rise, holdHigh, fall, cycles - Mathf.Floor(cycles));
            return inverse ? 1f - u : u;
        }

        /// <summary>
        /// One wave cycle at <paramref name="u"/> in 0..1: a rise from 0 to 1, a hold at 1, a
        /// fall back to 0, and a hold at 0 for whatever the three leave over. A share of zero
        /// skips its part, so a rise of 0 jumps straight up and a rise of 1 is a sawtooth.
        /// The rise follows its ease forwards. The fall follows its own ease forwards in time
        /// on the way down, so an OutBounce fall bounces at the bottom.
        /// </summary>
        public static float Wave(int riseEase, int fallEase, float rise, float holdHigh, float fall, float u)
        {
            var riseEnd = Mathf.Clamp01(rise);
            var highEnd = riseEnd + Mathf.Clamp(holdHigh, 0f, 1f - riseEnd);
            var fallEnd = highEnd + Mathf.Clamp(fall, 0f, 1f - highEnd);
            if (u < riseEnd)
            {
                return Ease(riseEase, u / riseEnd);
            }

            if (u < highEnd)
            {
                return 1f;
            }

            if (u < fallEnd)
            {
                return 1f - Ease(fallEase, (u - highEnd) / (fallEnd - highEnd));
            }

            return 0f;
        }

        /// <summary>
        /// True while a wave is on its return leg: the fall and the low hold after it. A wave
        /// whose rise and high hold fill the whole cycle never returns.
        /// </summary>
        public static bool IsReturnLeg(int mode, float rise, float holdHigh, float cycles)
        {
            if (mode != AlpsShowLayout.PhaseWave)
            {
                return false;
            }

            var outbound = OutboundLeg(rise, holdHigh);
            var u = cycles - Mathf.Floor(cycles);
            return outbound < 1f && u >= outbound;
        }

        /// <summary>The share of a wave cycle before the return leg: the rise and the high hold.</summary>
        public static float OutboundLeg(float rise, float holdHigh)
        {
            var riseEnd = Mathf.Clamp01(rise);
            return riseEnd + Mathf.Clamp(holdHigh, 0f, 1f - riseEnd);
        }

        /// <summary>Smooth 2D value noise in 0..1, the one the GPU evaluator wanders a random phase with.</summary>
        public static float Noise(float x, float y)
        {
            var ix = Mathf.Floor(x);
            var iy = Mathf.Floor(y);
            var fx = x - ix;
            var fy = y - iy;
            var ux = fx * fx * (3f - 2f * fx);
            var uy = fy * fy * (3f - 2f * fy);
            var a = Hash(ix, iy);
            var b = Hash(ix + 1f, iy);
            var c = Hash(ix, iy + 1f);
            var d = Hash(ix + 1f, iy + 1f);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        private static float Hash(float x, float y)
        {
            x = Frac(x * 123.34f);
            y = Frac(y * 456.21f);
            var d = x * (x + 45.32f) + y * (y + 45.32f);
            return Frac((x + d) * (y + d));
        }

        private static float Frac(float value)
        {
            return value - Mathf.Floor(value);
        }
    }
}
