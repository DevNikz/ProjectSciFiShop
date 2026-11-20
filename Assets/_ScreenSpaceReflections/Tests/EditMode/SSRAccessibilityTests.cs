using NUnit.Framework;

namespace SSRFeature.Tests
{
    public class SSRAccessibilityTests
    {
        const float dt = 1f / 60f;
        static SSRSettings S() { var s = new SSRSettings(); s.Validate(); return s; }

        [Test] public void Defaults_PassThrough_NoLimiter()
        {
            var r = SSRAccessibility.Resolve(S(), new SSRAccessibilityPrefs(), dt);
            Assert.IsTrue(r.enabled);
            Assert.IsTrue(r.jitterPerFrame);
            Assert.IsFalse(r.resetHistoryToZero);
            Assert.AreEqual(0f, r.maxDeltaPerFrame);
            Assert.AreEqual(S().intensity, r.intensity, 1e-5f);
        }

        [Test] public void NullPrefs_ActLikeDefaults()
        {
            var r = SSRAccessibility.Resolve(S(), null, dt);
            Assert.IsTrue(r.enabled);
            Assert.AreEqual(0f, r.maxDeltaPerFrame);
        }

        [Test] public void MasterOff_DisablesFeature()
        {
            var p = new SSRAccessibilityPrefs { reflectionsEnabled = false };
            Assert.IsFalse(SSRAccessibility.Resolve(S(), p, dt).enabled);
        }

        [Test] public void PhotosensitiveSafe_AppliesAllProtections()
        {
            var p = new SSRAccessibilityPrefs { photosensitiveSafe = true };
            var r = SSRAccessibility.Resolve(S(), p, dt);
            Assert.IsFalse(r.jitterPerFrame);
            Assert.IsTrue(r.resetHistoryToZero);
            Assert.Greater(r.maxDeltaPerFrame, 0f);
            Assert.LessOrEqual(r.maxLuminance, SSRAccessibility.kSafeMaxLuminance);
            Assert.GreaterOrEqual(r.temporalFeedback, SSRAccessibility.kCalmFeedback);
        }

        [Test] public void SafeMode_NeverLoosensDesignerCap()
        {
            var s = S(); s.maxLuminance = 0.5f;
            var p = new SSRAccessibilityPrefs { photosensitiveSafe = true };
            Assert.LessOrEqual(SSRAccessibility.Resolve(s, p, dt).maxLuminance, 0.5f);
        }

        [Test] public void ReduceShimmer_RemovesJitter_NoLimiter()
        {
            var r = SSRAccessibility.Resolve(S(), new SSRAccessibilityPrefs { reduceShimmer = true }, dt);
            Assert.IsFalse(r.jitterPerFrame);
            Assert.AreEqual(0f, r.maxDeltaPerFrame);
        }

        [Test] public void ReduceMotion_WidensEdgeFade_AndRateLimits()
        {
            var s = S(); s.edgeFade = 0.05f;
            var r = SSRAccessibility.Resolve(s, new SSRAccessibilityPrefs { reduceMotion = true }, dt);
            Assert.GreaterOrEqual(r.edgeFade, SSRAccessibility.kReducedEdgeFade);
            Assert.Greater(r.maxDeltaPerFrame, 0f);
        }

        [Test] public void PerformanceMode_ForcesLowTier()
        {
            var s = S(); s.quality = SSRQuality.High;
            var r = SSRAccessibility.Resolve(s, new SSRAccessibilityPrefs { performanceMode = true }, dt);
            Assert.AreEqual(SSRQualityProfile.Get(SSRQuality.Low).maxSteps, r.maxSteps);
        }

        [Test] public void DeltaTime_IsClamped()
        {
            var p = new SSRAccessibilityPrefs { photosensitiveSafe = true };
            var slow = SSRAccessibility.Resolve(S(), p, 5f).maxDeltaPerFrame;
            Assert.LessOrEqual(slow, SSRAccessibility.kSafeRatePerSecond / 15f + 1e-5f);
        }

        [Test] public void SmoothnessOff_ZeroesInfluence()
        {
            var s = S(); s.useMaterialSmoothness = false;
            Assert.AreEqual(0f, SSRAccessibility.Resolve(s, null, dt).smoothnessInfluence);
        }

        [Test] public void AllOpaqueMode_IgnoresSmoothness()
        {
            var s = S(); s.surfaceMode = SSRSurfaceMode.AllOpaque;
            Assert.AreEqual(0f, SSRAccessibility.Resolve(s, null, dt).smoothnessInfluence);
        }

        [Test] public void ReduceShimmer_RaisesMinimumBlur()
        {
            var s = S(); s.roughnessBlur = 0f;
            var r = SSRAccessibility.Resolve(s, new SSRAccessibilityPrefs { reduceShimmer = true }, dt);
            Assert.GreaterOrEqual(r.roughnessBlur, SSRAccessibility.kCalmMinBlur);
        }

        [Test] public void Json_MissingKeys_FallBackToSafeDefaults()
        {
            var p = SSRAccessibilityPrefs.FromJson("{}");
            Assert.IsTrue(p.reflectionsEnabled);
            Assert.AreEqual(1f, p.intensity);
        }

        [Test] public void Json_Garbage_FallsBackToDefaults()
        {
            Assert.IsTrue(SSRAccessibilityPrefs.FromJson("not json").reflectionsEnabled);
        }

        [Test] public void Json_RoundTrip()
        {
            var a = new SSRAccessibilityPrefs { photosensitiveSafe = true, saturation = 0.3f };
            var b = SSRAccessibilityPrefs.FromJson(a.ToJson());
            Assert.IsTrue(b.photosensitiveSafe);
            Assert.AreEqual(0.3f, b.saturation, 1e-5f);
        }
    }
}
