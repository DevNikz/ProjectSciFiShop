using NUnit.Framework;
using UnityEngine;

namespace SSGICore.Tests
{
    public class SSGICoreQualityTests
    {
        [Test]
        public void Presets_ScaleDownsampleAndCost()
        {
            var low = SSGICoreQuality.Preset(SSGICoreQualityPreset.Low);
            var med = SSGICoreQuality.Preset(SSGICoreQualityPreset.Medium);
            var high = SSGICoreQuality.Preset(SSGICoreQualityPreset.High);

            Assert.AreEqual(4, low.downsample);
            Assert.AreEqual(2, med.downsample);
            Assert.AreEqual(2, high.downsample);
            Assert.Less(low.maxSteps, high.maxSteps);
            Assert.LessOrEqual(low.bounces, high.bounces);
            Assert.LessOrEqual(low.rayCount, high.rayCount);
        }

        [Test]
        public void Resolve_Custom_UsesVolumeValues()
        {
            var v = ScriptableObject.CreateInstance<SSGICoreVolume>();
            v.quality.value = SSGICoreQualityPreset.Custom;
            v.resolution.value = SSGICoreResolution.Full;
            v.rayCount.value = 3;
            v.maxSteps.value = 40;
            v.bounces.value = 2;
            v.filterIterations.value = 1;
            v.temporalFrames.value = 8;

            var s = SSGICoreQuality.Resolve(v);
            Assert.AreEqual(1, s.downsample);
            Assert.AreEqual(3, s.rayCount);
            Assert.AreEqual(40, s.maxSteps);
            Assert.AreEqual(1, s.filterIterations);
            Assert.AreEqual(8, s.maxAge);
            Object.DestroyImmediate(v);
        }

        [Test]
        public void HzbLevelSize_RoundsUpAndNeverZero()
        {
            Assert.AreEqual(960, SSGICoreUtil.HzbLevelSize(1920, 1));
            Assert.AreEqual(61, SSGICoreUtil.HzbLevelSize(1921, 5));
            Assert.AreEqual(1, SSGICoreUtil.HzbLevelSize(1, 5));
        }

        [Test]
        public void NormalSource_DefaultsToAuto_AndKeepsShaderModeValues()
        {
            var v = ScriptableObject.CreateInstance<SSGICoreVolume>();
            Assert.AreEqual(SSGICoreNormalSource.Auto, v.normalSource.value);
            Assert.AreEqual(0, (int)SSGICoreNormalSource.Raw);
            Assert.AreEqual(1, (int)SSGICoreNormalSource.Octahedral);
            Assert.AreEqual(2, (int)SSGICoreNormalSource.ReconstructFromDepth);
            Assert.AreEqual(3, (int)SSGICoreNormalSource.Auto);
            Object.DestroyImmediate(v);
        }

        [Test]
        public void LowSize_RoundsUp()
        {
            Assert.AreEqual(961, SSGICoreUtil.LowSize(1921, 2));
            Assert.AreEqual(480, SSGICoreUtil.LowSize(1920, 4));
        }

        [Test]
        public void ZParams_MatchUnityLayout()
        {
            Vector4 z = SSGICoreUtil.ComputeZParams(0.1f, 100f, true);
            // eye depth of raw depth 1 (near plane, reversed Z) must equal near
            float eye = 1f / (z.z * 1f + z.w);
            Assert.AreEqual(0.1f, eye, 1e-4f);
            // raw depth 0 (far plane) must equal far
            Assert.AreEqual(100f, 1f / (z.z * 0f + z.w), 1e-2f);
        }

        [Test]
        public void HzbAtlas_LevelsFitInsideAtlasWithoutOverlap()
        {
            int[][] sizes = { new[] { 1920, 1080 }, new[] { 1921, 1081 }, new[] { 3, 3 }, new[] { 1, 1 }, new[] { 2560, 1440 } };
            foreach (int[] sz in sizes)
            {
                var rects = new Vector4[SSGICoreUtil.HzbLevels + 1];
                Vector2Int atlas = SSGICoreUtil.ComputeHzbAtlas(sz[0], sz[1], rects);
                for (int a = 1; a <= SSGICoreUtil.HzbLevels; a++)
                {
                    Assert.LessOrEqual(rects[a].x + rects[a].z, atlas.x, "level " + a + " exceeds atlas width");
                    Assert.LessOrEqual(rects[a].y + rects[a].w, atlas.y, "level " + a + " exceeds atlas height");
                    Assert.AreEqual(SSGICoreUtil.HzbLevelSize(sz[0], a), (int)rects[a].z);
                    for (int b = a + 1; b <= SSGICoreUtil.HzbLevels; b++)
                    {
                        bool overlap = rects[a].x < rects[b].x + rects[b].z && rects[b].x < rects[a].x + rects[a].z &&
                                       rects[a].y < rects[b].y + rects[b].w && rects[b].y < rects[a].y + rects[a].w;
                        Assert.IsFalse(overlap, "levels " + a + " and " + b + " overlap");
                    }
                }
            }
        }

        [Test]
        public void RayTerms_MatchInverseProjection()
        {
            Matrix4x4 proj = Matrix4x4.Perspective(60f, 16f / 9f, 0.1f, 500f);
            // sub-pixel jitter style shear (what TAA adds to the projection)
            proj.m02 += 0.0007f;
            proj.m12 -= 0.0004f;
            Matrix4x4 inv = proj.inverse;
            SSGICoreUtil.ComputeRayTerms(inv, out Vector4 a, out Vector4 b);

            float[] ndcs = { -0.93f, -0.2f, 0f, 0.41f, 0.97f };
            foreach (float nx in ndcs)
            foreach (float ny in ndcs)
            {
                const float eye = 7.5f;
                Vector4 h = inv * new Vector4(nx, ny, 0.5f, 1f);
                Vector3 r = new Vector3(h.x, h.y, h.z) / h.w;
                Vector3 expected = r * (eye / -r.z);

                float rx = a.x + nx * a.z + ny * b.x;
                float ry = a.y + nx * a.w + ny * b.y;
                Assert.AreEqual(expected.x, rx * eye, 1e-3f);
                Assert.AreEqual(expected.y, ry * eye, 1e-3f);
            }
        }

        [Test]
        public void ParityRmse_IsZeroForIdenticalAndPositiveForDifferent()
        {
            var a = new[] { new Color(1, 1, 1, 1), new Color(0.5f, 0.5f, 0.5f, 1) };
            var b = new[] { new Color(1, 1, 1, 1), new Color(0.6f, 0.6f, 0.6f, 1) };
            Assert.AreEqual(0f, SSGICoreParity.RelativeRmse(a, a), 1e-6f);
            Assert.Greater(SSGICoreParity.RelativeRmse(a, b), 0f);
            Assert.IsTrue(float.IsPositiveInfinity(SSGICoreParity.RelativeRmse(a, new Color[1])));
        }
    }
}
