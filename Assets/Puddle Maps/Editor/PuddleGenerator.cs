// Generation core: ground height field per kind -> water fill -> mask/depth/rim/normal buffers -> packed Color32 outputs.
// No GPU, no asset access: unit-testable in Edit Mode. Row-parallel (Parallel.For), so it must not touch Unity objects.
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace PuddleMaps
{
    public sealed class PuddleBuffers
    {
        public readonly int size; public readonly bool tile;
        public readonly float[] mask, depth, height, rim, nx, ny, nz;
        public PuddleBuffers(int size, bool tile)
        {
            this.size = size; this.tile = tile; int n = size * size;
            mask = new float[n]; depth = new float[n]; height = new float[n]; rim = new float[n];
            nx = new float[n]; ny = new float[n]; nz = new float[n];
        }
    }

    public static class PuddleGenerator
    {
        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static float Smooth(float a, float b, float x) => Smooth01((x - a) / Mathf.Max(1e-5f, b - a));

        /// <summary>Dry ground height in ~[0,1] (high = dry). Periodic in u and v when s.tileable.</summary>
        public static float GroundAt(PuddleMapSettings s, float u, float v)
        {
            bool tile = s.tileable; int seed = s.seed;
            switch (s.kind)
            {
                case PuddleKind.Pooled:
                {
                    float low = PuddleNoise.Fbm(u, v, Mathf.Max(1, s.noiseScale / 2), Mathf.Min(3, s.octaves), s.lacunarity, s.gain, tile, seed);
                    float hi = PuddleNoise.Fbm(u, v, s.noiseScale * 2, s.octaves, s.lacunarity, s.gain, tile, seed + 7);
                    return Mathf.Lerp(low, hi, s.pooledDetail);
                }
                case PuddleKind.Cracked:
                {
                    float wu = (PuddleNoise.Fbm(u, v, s.noiseScale, 3, 2f, 0.5f, tile, seed + 11) - 0.5f) * s.crackWobble * 0.15f;
                    float wv = (PuddleNoise.Fbm(u, v, s.noiseScale, 3, 2f, 0.5f, tile, seed + 13) - 0.5f) * s.crackWobble * 0.15f;
                    PuddleNoise.Worley(u + wu, v + wv, s.cellCount, tile, seed, out float f1, out float f2, out _);
                    float crack = 1f - Smooth(0f, s.crackWidth, f2 - f1);
                    float patch = PuddleNoise.Fbm(u, v, s.noiseScale, s.octaves, s.lacunarity, s.gain, tile, seed + 21);
                    return (1f - crack * s.crackDepth) * (0.85f + 0.15f * patch);
                }
                case PuddleKind.Ruts:
                {
                    int lanes = s.rutLanes;
                    float w = (PuddleNoise.Fbm(u, 0.37f, s.noiseScale, 3, 2f, 0.5f, tile, seed + 31) - 0.5f) * s.rutWobble / lanes;
                    float lv = (v + w) * lanes; float f = lv - Mathf.Floor(lv);
                    float half = s.rutSpacing * 0.5f;
                    float d = Mathf.Min(Mathf.Abs(f - (0.5f - half)), Mathf.Abs(f - (0.5f + half)));
                    float groove = 1f - Smooth(0f, s.rutWidth, d);
                    float vary = Mathf.Lerp(1f, PuddleNoise.Fbm(u, 0.71f, s.noiseScale, 3, 2f, 0.5f, tile, seed + 47), s.rutBreakup);
                    float rough = PuddleNoise.Fbm(u, v, s.noiseScale * 2, s.octaves, s.lacunarity, s.gain, tile, seed + 53) - 0.5f;
                    return Mathf.Clamp01(1f - groove * vary * 0.9f + rough * 0.15f);
                }
                case PuddleKind.Cobblestone:
                {
                    PuddleNoise.Worley(u, v, s.cellCount, tile, seed, out float f1, out float f2, out float id);
                    float edge = f2 - f1;
                    float gap = 1f - Smooth(s.gapWidth, s.gapWidth + s.gapSoftness, edge);
                    float top = 1f - s.stoneVariation * id;
                    float rounded = 0.9f + 0.1f * Smooth(0f, 0.5f, edge);
                    return Mathf.Lerp(top * rounded, 0.15f, gap);
                }
                default:
                    return PuddleNoise.Fbm(u, v, s.noiseScale, s.octaves, s.lacunarity, s.gain, tile, seed);
            }
        }

        /// <summary>Water level at a point: the coverage level plus optional noise so edges do not follow height contours exactly.</summary>
        public static float LevelAt(PuddleMapSettings s, float u, float v, float baseLevel)
        {
            if (s.edgeNoise <= 0f) return baseLevel;
            float n = PuddleNoise.Fbm(u, v, Mathf.Max(2, s.noiseScale), 3, 2f, 0.5f, s.tileable, s.seed + 999);
            return baseLevel + (n - 0.5f) * s.edgeNoise * 0.3f;
        }

        const int kBins = 4096;

        /// <summary>Height below which `coverage` of the pixels lie, plus edgeSoftness so those pixels are fully flooded (also covers tied plateaus).</summary>
        static float CoverageLevel(float[] ground, float coverage, float edgeSoftness)
        {
            var hist = new int[kBins];
            for (int i = 0; i < ground.Length; i++) hist[Mathf.Clamp((int)(ground[i] * kBins), 0, kBins - 1)]++;
            long target = (long)(coverage * ground.Length), cum = 0; int k = 0;
            for (; k < kBins - 1; k++) { cum += hist[k]; if (cum >= target) break; }
            return (k + 1f) / kBins + edgeSoftness;
        }

        /// <summary>progress(stage, 0..1) returns true to cancel; Generate then returns null. Call Validate() on s first.</summary>
        public static PuddleBuffers Generate(PuddleMapSettings s, int size, Func<string, float, bool> progress = null)
        {
            if (progress != null && progress("Height and water", 0f)) return null;
            int n = size * size;
            var b = new PuddleBuffers(size, s.tileable);
            var hn = new float[n]; var gRaw = new float[n]; var ground = new float[n];

            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                    ground[y * size + x] = GroundAt(s, (x + 0.5f) / size, (y + 0.5f) / size);
            });
            float baseLevel = CoverageLevel(ground, s.coverage, s.edgeSoftness);

            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    float H = ground[i];
                    float lvl = LevelAt(s, u, v, baseLevel);
                    float depth = Mathf.Max(0f, lvl - H);
                    float mask = Smooth01(depth / s.edgeSoftness);
                    float ripple = 0f;
                    if (s.rippleAmount > 0f && mask > 0f)
                        ripple = (PuddleNoise.Fbm(u, v, Mathf.Max(2, s.noiseScale) * 6, 2, 2f, 0.5f, s.tileable, s.seed + 4242) - 0.5f) * s.rippleAmount * 0.05f * mask;
                    b.mask[i] = mask;
                    b.depth[i] = Mathf.Clamp01(depth / Mathf.Max(lvl, 0.05f));
                    b.rim[i] = 4f * mask * (1f - mask);
                    hn[i] = Mathf.Lerp(H, lvl, mask) + ripple;      // water surface is flat; ripples ride on it
                    gRaw[i] = Mathf.Max(H, lvl);                    // parallax height: puddles flat at the water level
                }
            });

            float gMin = float.MaxValue, gMax = float.MinValue;
            for (int i = 0; i < n; i++) { if (gRaw[i] < gMin) gMin = gRaw[i]; if (gRaw[i] > gMax) gMax = gRaw[i]; }
            float gRange = Mathf.Max(1e-4f, gMax - gMin);
            for (int i = 0; i < n; i++) b.height[i] = Mathf.Clamp01((gRaw[i] - gMin) / gRange);

            if (progress != null && progress("Normals", 0.6f)) return null;
            float bump = s.normalStrength * 0.1f;     // [GAP] tuning constant: slope per tile-width * bump
            bool tile = s.tileable;
            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    int xl = tile ? (x - 1 + size) % size : Mathf.Max(0, x - 1), xr = tile ? (x + 1) % size : Mathf.Min(size - 1, x + 1);
                    int yd = tile ? (y - 1 + size) % size : Mathf.Max(0, y - 1), yu = tile ? (y + 1) % size : Mathf.Min(size - 1, y + 1);
                    float dhdu = (hn[y * size + xr] - hn[y * size + xl]) * 0.5f * size;
                    float dhdv = (hn[yu * size + x] - hn[yd * size + x]) * 0.5f * size;
                    float nx = -dhdu * bump, ny = -dhdv * bump, nz = 1f;
                    float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    int i = y * size + x;
                    b.nx[i] = nx * inv; b.ny[i] = ny * inv; b.nz[i] = nz * inv;
                }
            });
            return b;
        }

        static byte B(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        /// <summary>smooth: A = smoothness (metallic map slot). normal: tangent-space RGB. data: R depth, G height, B rim, A mask.</summary>
        public static void Pack(PuddleBuffers b, PuddleMapSettings s, out Color32[] smooth, out Color32[] normal, out Color32[] data)
        {
            int n = b.size * b.size;
            smooth = new Color32[n]; normal = new Color32[n]; data = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                smooth[i] = new Color32(0, 0, 0, B(Mathf.Lerp(s.dryGloss, 1f, b.mask[i])));
                normal[i] = new Color32(B(b.nx[i] * 0.5f + 0.5f), B(b.ny[i] * 0.5f + 0.5f), B(b.nz[i] * 0.5f + 0.5f), 255);
                data[i] = new Color32(B(b.depth[i]), B(b.height[i]), B(b.rim[i]), B(b.mask[i]));
            }
        }

        /// <summary>Detail-albedo texel (linear) that multiplies albedo by wetAlbedoMultiplier: Stylized Lit's detail path is albedo * (2 * detail).</summary>
        public static Color32 WetDarkenTexel(PuddleMapSettings s)
        {
            byte g = B(s.wetAlbedoMultiplier * 0.5f);
            return new Color32(g, g, g, 255);
        }
    }
}
