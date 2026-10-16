using System;

namespace TooManyLagsStandalone
{
    // Client-only, allocation-free prediction at the existing camera cadence.
    // Retains actual coverage and adds at most one adjacent direction.
    internal sealed class CameraPrediction
    {
        private bool sampled;
        private float previousYaw, previousTime, previousOverlap, previousSeconds, heldUntil;
        private int held = -1;
        internal void Reset() { sampled = false; held = -1; heldUntil = 0f; }
        internal int Sample(float x, float z, float now, float overlap, float seconds)
        {
            if (Single.IsNaN(x) || Single.IsNaN(z) || Single.IsInfinity(x) || Single.IsInfinity(z) ||
                (double)x * x + (double)z * z < .000001d) { Reset(); return -1; }
            return Sample((float)(Math.Atan2(x, z) * (180d / Math.PI)), now, overlap, seconds);
        }
        internal int Sample(float yaw, float now, float overlap, float seconds)
        {
            int actual = AuthorityGrid.HeadingCoverage(yaw, overlap);
            if (actual < 0 || Single.IsNaN(now) || Single.IsInfinity(now) ||
                Single.IsNaN(seconds) || Single.IsInfinity(seconds) || seconds <= 0f)
            { Reset(); return actual; }
            seconds = Math.Min(.5f, seconds);
            overlap = Single.IsNaN(overlap) || Single.IsInfinity(overlap) ? 30f : Math.Max(0f, Math.Min(45f, overlap));
            yaw %= 360f;
            float elapsed = now - previousTime;
            bool fresh = !sampled || elapsed <= 0f || elapsed > .5f || overlap != previousOverlap || seconds != previousSeconds;
            float delta = yaw - previousYaw;
            if (delta > 180f) delta -= 360f; else if (delta < -180f) delta += 360f;
            previousYaw = yaw; previousTime = now; previousOverlap = overlap; previousSeconds = seconds; sampled = true;
            if (fresh) { held = -1; return actual; }
            int proposed = -1;
            // An exact half-turn cannot reveal its direction from two samples.
            if (Math.Abs(delta) > .001f && Math.Abs(delta) < 179.999f)
            {
                int first = actual < 8 ? actual : actual - 8;
                int ahead = delta > 0f ? (first + (actual < 8 ? 1 : 2)) & 7 : (first + 7) & 7;
                float lead = Math.Min(45f, Math.Abs(delta / elapsed * seconds));
                int predicted = AuthorityGrid.HeadingCoverage(yaw + (delta > 0f ? lead : -lead), overlap);
                if ((Bits(predicted) & (1 << ahead)) != 0) proposed = ahead;
            }
            // Small direction reversals cannot flap the predictive fringe.
            // Actual coverage always wins, including abrupt turns and resets.
            if (held >= 0 && (Join(actual, held) == actual || now >= heldUntil)) held = -1;
            if (proposed >= 0 && (held < 0 || held == proposed))
            { held = proposed; heldUntil = now + Math.Min(seconds, .1f); }
            return held < 0 ? actual : Join(actual, held);
        }
        internal static int Bits(int coverage)
        {
            if (coverage < 0 || coverage >= AuthorityGrid.CoverageCount) return 0;
            if (coverage >= 24)
            {
                int actual = (coverage - 24) & 15, start = actual & 7;
                int extra = coverage < 40 ? (start + (actual < 8 ? 1 : 2)) & 7 : (start + 7) & 7;
                return Bits(actual) | (1 << extra);
            }
            int first = coverage & 7, count = coverage / 8 + 1, bits = 0;
            for (int i = 0; i < count; i++) bits |= 1 << ((first + i) & 7);
            return bits;
        }
        internal static int Join(int actual, int extra)
        {
            if (actual < 0 || actual >= 16 || extra < 0 || extra >= 8) return actual;
            int first = actual & 7;
            if ((Bits(actual) & (1 << extra)) != 0) return actual;
            if (extra == ((first + 7) & 7)) return 40 + actual;
            if (extra == ((first + (actual < 8 ? 1 : 2)) & 7)) return 24 + actual;
            return actual;
        }
    }
}
