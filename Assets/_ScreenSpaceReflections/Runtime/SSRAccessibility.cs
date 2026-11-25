// BLK-02  Accessibility resolver. Pure logic (no GPU) => Edit Mode unit-testable.
// The feature does NOT persist prefs; the game's settings system owns storage (ToJson/FromJson provided).
using System;
using UnityEngine;

namespace SSRFeature
{
    /// <summary>Player-facing options. Defaults = "no accessibility changes".</summary>
    [Serializable]
    public sealed class SSRAccessibilityPrefs
    {
        [Tooltip("Master off. Falls back to reflection probes / skybox.")]
        public bool reflectionsEnabled = true;

        [Tooltip("Rate-limits frame-to-frame brightness change, caps brightness, removes per-frame noise, fades in after resets.")]
        public bool photosensitiveSafe = false;

        [Tooltip("Removes per-frame noise jitter and raises temporal smoothing. Reduces shimmer/sparkle.")]
        public bool reduceShimmer = false;

        [Tooltip("Wider screen-edge fade and slower reflection transitions while the camera moves.")]
        public bool reduceMotion = false;

        [Tooltip("Forces the Low quality tier (also helps low-end hardware).")]
        public bool performanceMode = false;

        [Range(0f, 1f)] public float intensity = 1f;
        [Range(0f, 1f), Tooltip("0 = greyscale reflections, 1 = unchanged.")] public float saturation = 1f;
        [Range(0f, 1f), Tooltip("1 = designer cap, 0 = darkest allowed cap.")] public float brightnessLimit = 1f;

        public SSRAccessibilityPrefs Clone() => (SSRAccessibilityPrefs)MemberwiseClone();
        public string ToJson() => JsonUtility.ToJson(this);

        public static SSRAccessibilityPrefs FromJson(string json)
        {
            var p = new SSRAccessibilityPrefs();               // field initializers = safe defaults for missing keys
            if (!string.IsNullOrEmpty(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, p); } catch (ArgumentException) { p = new SSRAccessibilityPrefs(); }
            }
            return p;
        }
    }

    public static class SSRAccessibility
    {
        // ---- PLACEHOLDER constants. [GAP] must be validated by QA / photosensitivity analysis before any compliance claim. ----
        public const float kSafeMaxLuminance   = 1.0f;   // scene-linear
        public const float kMinBrightnessCap   = 0.25f;
        public const float kSafeRatePerSecond  = 1.5f;   // max change of (luma | opacity) per second
        public const float kCalmRatePerSecond  = 3.0f;
        public const float kCalmFeedback       = 0.9f;
        public const float kReducedEdgeFade    = 0.2f;
        public const float kPerformanceMaxScale = 0.5f;  // performance mode caps the resolution scale
        public const float kCalmMinBlur        = 1.5f;   // reduce-shimmer: rougher kernel hides noise

        static readonly SSRAccessibilityPrefs s_Default = new SSRAccessibilityPrefs();
        static SSRAccessibilityPrefs s_Current = new SSRAccessibilityPrefs();

        public static SSRAccessibilityPrefs Current => s_Current;          // treat as read-only; use Set()
        public static event Action<SSRAccessibilityPrefs> Changed;
        public static int CutGeneration { get; private set; }

        public static void Set(SSRAccessibilityPrefs prefs)
        {
            s_Current = (prefs ?? new SSRAccessibilityPrefs()).Clone();
            Changed?.Invoke(s_Current);
        }

        /// <summary>Call on hard camera cuts/teleports so temporal history is discarded (and faded back in when safe mode is on).</summary>
        public static void NotifyCameraCut() => CutGeneration++;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_Current = new SSRAccessibilityPrefs(); Changed = null; CutGeneration = 0; }

        /// <summary>Merge designer settings + player prefs. Allocation-free; safe to call per camera per frame.</summary>
        public static SSRResolvedParams Resolve(SSRSettings s, SSRAccessibilityPrefs p, float unscaledDeltaTime)
        {
            if (p == null) p = s_Default;

            bool safe = p.photosensitiveSafe;
            bool calm = safe || p.reduceShimmer;
            float dt = Mathf.Clamp(unscaledDeltaTime, 1f / 240f, 1f / 15f);

            // performanceMode forces the Low preset and ignores Custom.
            bool useCustom = !p.performanceMode && s.quality == SSRQuality.Custom && s.custom != null;
            var prof = useCustom ? SSRQualityProfile.FromCustom(s.custom)
                                 : SSRQualityProfile.Get(p.performanceMode ? SSRQuality.Low : s.quality);
            float maxDistance = useCustom ? s.custom.maxDistance : s.maxDistance;
            float thickness = useCustom ? s.custom.thickness : s.thickness;
            float feedback = useCustom ? s.custom.temporalFeedback : s.temporalFeedback;
            float blur = useCustom ? s.custom.roughnessBlur : s.roughnessBlur;
            bool smoothnessDriven = s.useMaterialSmoothness && s.surfaceMode != SSRSurfaceMode.AllOpaque;

            float lum = Mathf.Lerp(kMinBrightnessCap, s.maxLuminance, Mathf.Clamp01(p.brightnessLimit));
            if (safe) lum = Mathf.Min(lum, kSafeMaxLuminance);

            float rate = float.PositiveInfinity;
            if (safe) rate = kSafeRatePerSecond;
            if (p.reduceMotion) rate = Mathf.Min(rate, kCalmRatePerSecond);

            return new SSRResolvedParams
            {
                enabled          = s.enabled && p.reflectionsEnabled,
                resolutionScale  = p.performanceMode ? Mathf.Min(s.resolutionScale, kPerformanceMaxScale) : s.resolutionScale,
                maxSteps         = prof.maxSteps,
                refineSteps      = prof.refineSteps,
                denoiseIterations = prof.denoiseIterations,      // performanceMode already forces the Low profile (1 iteration)
                distanceFadePower = s.distanceFadePower,
                normalSharpness  = s.normalSharpness,
                hitReprojection  = s.hitReprojection,
                maxDistance      = maxDistance,
                thickness        = thickness,
                edgeFade         = p.reduceMotion ? Mathf.Max(s.edgeFade, kReducedEdgeFade) : s.edgeFade,
                fresnelPower     = s.fresnelPower,
                minReflectance   = s.minReflectance,
                reflectivity     = s.reflectivity,
                intensity        = s.intensity * Mathf.Clamp01(p.intensity),
                maxOpacity       = s.maxOpacity,
                saturation       = Mathf.Clamp01(p.saturation),
                maxLuminance     = lum,
                temporalFeedback = calm ? Mathf.Max(feedback, kCalmFeedback) : feedback,
                smoothnessThreshold = s.smoothnessThreshold,
                smoothnessRamp   = s.smoothnessRamp,
                smoothnessInfluence = smoothnessDriven ? s.smoothnessInfluence : 0f,   // NormalsAlpha + Registered (was Registered only, so NormalsAlpha smoothness was ignored)
                roughnessBlur    = calm ? Mathf.Max(blur, kCalmMinBlur) : blur,
                jitterPerFrame   = !calm,
                resetHistoryToZero = safe || p.reduceMotion,
                maxDeltaPerFrame = float.IsInfinity(rate) ? 0f : rate * dt,
            };
        }
    }
}
