// BoomNetwork VampireSurvivors Demo — Deterministic RNG
//
// 32-bit LCG, seeded from GameState.RngState.
// All random operations go through this to guarantee cross-client determinism.

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class DeterministicRng
    {
        /// <summary>Advance RNG and return next uint.</summary>
        public static uint Next(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return state;
        }

        /// <summary>Return a float in [0, 1).</summary>
        public static float NextFloat(ref uint state)
        {
            return (Next(ref state) >> 8) / 16777216f; // 2^24
        }

        /// <summary>Return a float in [min, max).</summary>
        public static float Range(ref uint state, float min, float max)
        {
            return min + NextFloat(ref state) * (max - min);
        }

        /// <summary>Return an int in [min, max) (exclusive max).</summary>
        public static int RangeInt(ref uint state, int min, int max)
        {
            if (min >= max) return min;
            return min + (int)(Next(ref state) % (uint)(max - min));
        }
    }
}
