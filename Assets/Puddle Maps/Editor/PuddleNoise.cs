// Tileable CPU noise for the puddle generator. Every lattice lookup wraps modulo the period, so output tiles exactly
// when `tile` is true and cell/period counts are integers. Pure math (thread-safe, no UnityEngine objects).
using UnityEngine;

namespace PuddleMaps
{
    public static class PuddleNoise
    {
        static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        public static float Hash01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        static int Wrap(int i, int period)
        {
            if (period <= 0) return i;
            int m = i % period;
            return m < 0 ? m + period : m;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Value noise in [0,1]. period &lt;= 0 disables wrapping.</summary>
        public static float Value(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float u = Fade(x - x0), v = Fade(y - y0);
            int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period), ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
            float a = Hash01(xa, ya, seed), b = Hash01(xb, ya, seed), c = Hash01(xa, yb, seed), d = Hash01(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>fBm in [0,1] over u,v in [0,1). baseCells = integer cells across the tile. Tiling forces lacunarity 2.</summary>
        public static float Fbm(float u, float v, int baseCells, int octaves, float lacunarity, float gain, bool tile, int seed)
        {
            if (tile) lacunarity = 2f;
            float sum = 0f, amp = 1f, norm = 0f, cells = Mathf.Max(1, baseCells);
            for (int o = 0; o < octaves; o++)
            {
                int period = tile ? Mathf.Max(1, Mathf.RoundToInt(cells)) : 0;
                float f = tile ? period : cells;
                sum += amp * Value(u * f, v * f, period, seed + o * 131);
                norm += amp;
                amp *= gain;
                cells *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Worley/cellular noise. Distances are in cell units. cellId is a per-cell random in [0,1).</summary>
        public static void Worley(float u, float v, int cells, bool tile, int seed, out float f1, out float f2, out float cellId)
        {
            cells = Mathf.Max(1, cells);
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = f2 = float.MaxValue; cellId = 0f;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                int hx = tile ? Wrap(nx, cells) : nx, hy = tile ? Wrap(ny, cells) : ny;
                float px = nx + Hash01(hx, hy, seed), py = ny + Hash01(hx, hy, seed + 17);
                float ddx = px - x, ddy = py - y;
                float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                if (d < f1) { f2 = f1; f1 = d; cellId = Hash01(hx, hy, seed + 53); }
                else if (d < f2) f2 = d;
            }
        }
    }
}
