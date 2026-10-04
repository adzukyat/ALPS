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
        /// Phase φ in 0..1 from unwrapped cycles. A wave walks <see cref="Wave"/>, random is a
        /// smooth seeded wander. Invert, the eases and the fire chance apply to the wave only.
        /// </summary>
        public static float Phase(int mode, int riseEase, int fallEase, float rise, float holdHigh, float fall, bool inverse, float fireChance, float cycles, int k, int seed)
        {
            return Phase(mode, riseEase, fallEase, rise, holdHigh, fall, inverse, fireChance, cycles, k, seed, out _);
        }

        /// <summary>
        /// <see cref="Phase(int,int,int,float,float,float,bool,float,float,int,int)"/>, and in
        /// <paramref name="source"/> the cycle the phase comes from, which per cycle timing and
        /// per cycle palettes step on.
        ///
        /// Every cycle that fires starts a wave of its own, and shares adding up to more than
        /// one cycle carry it on into the cycles after it. Where waves overlap the highest one
        /// wins, the latest on a tie, the way DMX merges highest takes precedence. A cycle that
        /// does not fire starts none, so with no wave under way the phase rests at the bottom
        /// of the current cycle. Invert flips the result upside down.
        /// </summary>
        public static float Phase(int mode, int riseEase, int fallEase, float rise, float holdHigh, float fall, bool inverse, float fireChance, float cycles, int k, int seed, out float source)
        {
            var current = Mathf.Floor(cycles);
            source = current;
            if (mode == AlpsShowLayout.PhaseRandom)
            {
                return Mathf.Clamp01(Noise(cycles * 2f, (k + 1) * 7.31f + seed * 0.137f));
            }

            var span = Span(rise, holdHigh, fall);
            var reach = Mathf.CeilToInt(span);
            var u = 0f;
            var found = false;
            for (var j = 0; j < reach; j++)
            {
                var start = current - j;
                var position = cycles - start;
                if (position >= span || !Fires(fireChance, start, k, seed))
                {
                    continue;
                }

                var wave = Wave(riseEase, fallEase, rise, holdHigh, fall, position);
                if (!found || wave > u)
                {
                    u = wave;
                    source = start;
                    found = true;
                }
            }

            return inverse ? 1f - u : u;
        }

        /// <summary>
        /// True when the wave cycle <paramref name="cycles"/> is in fires at order position
        /// <paramref name="k"/>. Every cycle rolls its own seeded dice, and fixtures sharing an
        /// order position roll the same ones. The hash is integer maths, so the GPU rolls
        /// exactly the same dice.
        /// </summary>
        public static bool Fires(float chance, float cycles, int k, int seed)
        {
            unchecked
            {
                var h = Pcg((uint)Mathf.FloorToInt(cycles) ^ Pcg((uint)k ^ Pcg((uint)seed)));
                return (h >> 8) * (1f / 16777216f) < chance;
            }
        }

        /// <summary>The PCG hash the GPU evaluator rolls fire chances with.</summary>
        private static uint Pcg(uint value)
        {
            unchecked
            {
                var state = value * 747796405u + 2891336453u;
                var word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
                return (word >> 22) ^ word;
            }
        }

        /// <summary>
        /// One wave at <paramref name="u"/> cycles after it starts: a rise from 0 to 1, a hold
        /// at 1, a fall back to 0, and a hold at 0 for whatever the three leave over of the
        /// cycle. A share of zero skips its part, so a rise of 0 jumps straight up and a rise
        /// of 1 is a sawtooth. The shares may add up to <see cref="AlpsShowLayout.WaveMaxCycles"/>
        /// cycles. The rise follows its ease forwards. The fall follows its own ease forwards
        /// in time on the way down, so an OutBounce fall bounces at the bottom.
        /// </summary>
        public static float Wave(int riseEase, int fallEase, float rise, float holdHigh, float fall, float u)
        {
            const float max = AlpsShowLayout.WaveMaxCycles;
            var riseEnd = Mathf.Clamp(rise, 0f, max);
            var highEnd = riseEnd + Mathf.Clamp(holdHigh, 0f, max - riseEnd);
            var fallEnd = highEnd + Mathf.Clamp(fall, 0f, max - highEnd);
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
        /// True while no wave that fired is on its way out, which is where blackout on return
        /// is dark: every wave under way is on its return leg, the fall and the low hold after
        /// it, or none is under way at all. A wave whose rise and high hold fill all of it
        /// never returns.
        /// </summary>
        public static bool IsReturnLeg(int mode, float rise, float holdHigh, float fall, float fireChance, float cycles, int k, int seed)
        {
            if (mode != AlpsShowLayout.PhaseWave || !Returns(rise, holdHigh, fall))
            {
                return false;
            }

            var outbound = OutboundLeg(rise, holdHigh);
            var current = Mathf.Floor(cycles);
            var reach = Mathf.CeilToInt(Span(rise, holdHigh, fall));
            for (var j = 0; j < reach; j++)
            {
                if (cycles - (current - j) < outbound && Fires(fireChance, current - j, k, seed))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The cycles of a wave before its return leg: the rise and the high hold.</summary>
        public static float OutboundLeg(float rise, float holdHigh)
        {
            const float max = AlpsShowLayout.WaveMaxCycles;
            var riseEnd = Mathf.Clamp(rise, 0f, max);
            return riseEnd + Mathf.Clamp(holdHigh, 0f, max - riseEnd);
        }

        /// <summary>
        /// How many cycles one wave lasts: its shares, or the whole cycle when they leave a low
        /// hold, and at most <see cref="AlpsShowLayout.WaveMaxCycles"/>.
        /// </summary>
        public static float Span(float rise, float holdHigh, float fall)
        {
            var total = Mathf.Max(0f, rise) + Mathf.Max(0f, holdHigh) + Mathf.Max(0f, fall);
            return Mathf.Clamp(total, 1f, AlpsShowLayout.WaveMaxCycles);
        }

        /// <summary>True when a wave has a return leg, that is, does not stay out until it ends.</summary>
        public static bool Returns(float rise, float holdHigh, float fall)
        {
            return OutboundLeg(rise, holdHigh) < Span(rise, holdHigh, fall);
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
