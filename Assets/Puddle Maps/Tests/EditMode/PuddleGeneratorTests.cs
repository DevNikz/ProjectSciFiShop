using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PuddleMaps.Tests
{
    public class PuddleGeneratorTests
    {
        static PuddleMapSettings S(PuddlePreset p, int seed = 7)
        {
            var s = new PuddleMapSettings { seed = seed };
            s.ApplyPreset(p); s.seed = seed; s.Validate();
            return s;
        }

        static float Mean(float[] a) => a.Average();

        [Test] public void SameSeed_IsDeterministic()
        {
            var a = PuddleGenerator.Generate(S(PuddlePreset.ScatteredRain), 64);
            var b = PuddleGenerator.Generate(S(PuddlePreset.ScatteredRain), 64);
            CollectionAssert.AreEqual(a.mask, b.mask);
        }

        [Test] public void DifferentSeeds_Differ()
        {
            var a = PuddleGenerator.Generate(S(PuddlePreset.ScatteredRain, 7), 64);
            var b = PuddleGenerator.Generate(S(PuddlePreset.ScatteredRain, 8), 64);
            CollectionAssert.AreNotEqual(a.mask, b.mask);
        }

        [Test] public void AllPresets_InRange_AndNotDegenerate()
        {
            foreach (PuddlePreset p in Enum.GetValues(typeof(PuddlePreset)))
            {
                var b = PuddleGenerator.Generate(S(p), 64);
                foreach (var arr in new[] { b.mask, b.depth, b.height, b.rim })
                    foreach (float v in arr) Assert.That(v, Is.InRange(-1e-4f, 1f + 1e-4f), p.ToString());
                for (int i = 0; i < b.nx.Length; i++)
                    Assert.AreEqual(1f, b.nx[i] * b.nx[i] + b.ny[i] * b.ny[i] + b.nz[i] * b.nz[i], 1e-3f);
                Assert.That(Mean(b.mask), Is.InRange(0.05f, 0.6f), $"{p} coverage");
            }
        }

        [Test] public void Tileable_GroundIsPeriodic_ForEveryKind()
        {
            foreach (PuddleKind k in Enum.GetValues(typeof(PuddleKind)))
            {
                var s = S(PuddlePreset.ScatteredRain); s.kind = k; s.tileable = true; s.Validate();
                for (int i = 0; i < 20; i++)
                {
                    float u = 0.03f + i * 0.047f, v = 0.91f - i * 0.043f;
                    float g = PuddleGenerator.GroundAt(s, u, v);
                    Assert.AreEqual(g, PuddleGenerator.GroundAt(s, u + 1f, v), 2e-3f, $"{k} u-period");
                    Assert.AreEqual(g, PuddleGenerator.GroundAt(s, u, v + 1f), 2e-3f, $"{k} v-period");
                }
            }
        }

        [Test] public void Coverage_IncreasesWithCoverageSetting()
        {
            float Cov(float c)
            {
                var s = S(PuddlePreset.ScatteredRain); s.coverage = c; s.Validate();
                return Mean(PuddleGenerator.Generate(s, 64).mask);
            }
            Assert.Less(Cov(0.15f), Cov(0.3f));
            Assert.Less(Cov(0.3f), Cov(0.5f));
        }

        [Test] public void Normal_IsFlat_InsideFullPuddle()
        {
            var s = S(PuddlePreset.ScatteredRain); s.coverage = 0.9f; s.edgeNoise = 0f; s.rippleAmount = 0f; s.Validate();
            var b = PuddleGenerator.Generate(s, 64);
            int n = b.size, found = 0;
            for (int y = 1; y < n - 1; y++)
            for (int x = 1; x < n - 1; x++)
            {
                bool inside = true;
                for (int dy = -1; dy <= 1 && inside; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (b.mask[(y + dy) * n + x + dx] < 0.999f) { inside = false; break; }
                if (!inside) continue;
                int i = y * n + x; found++;
                Assert.AreEqual(0f, b.nx[i], 1e-4f); Assert.AreEqual(0f, b.ny[i], 1e-4f); Assert.AreEqual(1f, b.nz[i], 1e-4f);
            }
            Assert.Greater(found, 0);
        }

        [Test] public void Pack_SmoothnessAlpha_FollowsMask()
        {
            var s = S(PuddlePreset.ScatteredRain); s.dryGloss = 0.2f; s.Validate();
            var b = PuddleGenerator.Generate(s, 64);
            PuddleGenerator.Pack(b, s, out var smooth, out _, out var data);
            for (int i = 0; i < smooth.Length; i++)
            {
                Assert.AreEqual(Mathf.RoundToInt(b.mask[i] * 255f), data[i].a);
                if (b.mask[i] > 0.999f) Assert.AreEqual(255, smooth[i].a);
                if (b.mask[i] < 0.001f) Assert.AreEqual(Mathf.RoundToInt(0.2f * 255f), smooth[i].a);
            }
        }

        [Test] public void WetDarkenTexel_MatchesMultiplier()
        {
            var s = S(PuddlePreset.ScatteredRain); s.wetAlbedoMultiplier = 0.6f; s.Validate();
            Assert.AreEqual(Mathf.RoundToInt(0.3f * 255f), PuddleGenerator.WetDarkenTexel(s).r);
        }

        [Test] public void Validate_ClampsAndResolution()
        {
            var s = new PuddleMapSettings { octaves = 99, coverage = 5f, rutLanes = 0, cellCount = 1000 };
            s.Validate();
            Assert.AreEqual(8, s.octaves); Assert.AreEqual(1f, s.coverage); Assert.AreEqual(1, s.rutLanes); Assert.AreEqual(32, s.cellCount);
            Assert.AreEqual(4096, PuddleMapSettings.ClampResolution(5000));
            Assert.AreEqual(64, PuddleMapSettings.ClampResolution(10));
            Assert.AreEqual(1024, PuddleMapSettings.ClampResolution(1000));
        }
    }
}
