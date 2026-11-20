// BLK-01  Data + validation. Pure C#, no rendering calls. Owner of the SSRResolvedParams contract.
using System;
using UnityEngine;

namespace SSRFeature
{
    /// <summary>Custom = use <see cref="SSRSettings.custom"/> (modelled on HDRP SSR Quality: Low/Medium/High/Custom).</summary>
    public enum SSRQuality { Low = 0, Medium = 1, High = 2, Custom = 3 }

    /// <summary>Draws the field only while SSRSettings.quality is Custom (drawer lives in SSR.Editor).</summary>
    public sealed class ShowIfCustomQualityAttribute : PropertyAttribute { }

    /// <summary>Registered = only meshes with an <c>SSRSurface</c> component reflect (per-material smoothness supported). AllOpaque = every opaque pixel, uniform reflectivity (smoothness ignored).
    /// NormalsAlpha = reads smoothness (high nibble) and intensity (low nibble) packed into the alpha of _CameraNormalsTexture by StylizedLit's DepthNormalsOnly pass; no mask pass, no SSRSurface needed.</summary>
    public enum SSRSurfaceMode { Registered = 0, AllOpaque = 1, NormalsAlpha = 2 }

    /// <summary>Full-screen debug view of the decoded SSR inputs. Overwrites camera color after the SSR composite, before transparents and post-processing.</summary>
    public enum SSRDebugView { Off = 0, Smoothness = 1, Reflectivity = 2, FinalMask = 3, RawNormalsAlpha = 4 }

    /// <summary>Quality tiers. Numbers are PLACEHOLDERS, not benchmarks [GAP: profile on target hardware].</summary>
    public readonly struct SSRQualityProfile
    {
        public readonly float resolutionScale;
        public readonly int maxSteps;
        public readonly int refineSteps;

        public SSRQualityProfile(float scale, int steps, int refine)
        { resolutionScale = scale; maxSteps = steps; refineSteps = refine; }

        public static SSRQualityProfile Get(SSRQuality q)
        {
            switch (q)
            {
                case SSRQuality.Low:  return new SSRQualityProfile(0.5f, 24, 3);
                case SSRQuality.High: return new SSRQualityProfile(1.0f, 64, 5);
                default:              return new SSRQualityProfile(0.5f, 40, 4);
            }
        }

        public static SSRQualityProfile FromCustom(SSRCustomQuality c)
        { return new SSRQualityProfile(c.resolutionScale, c.maxSteps, c.refineSteps); }
    }

    /// <summary>Individual settings used when SSRSettings.quality == Custom. Ranges are PLACEHOLDERS [GAP].</summary>
    [Serializable]
    public sealed class SSRCustomQuality
    {
        [Range(0.25f, 1f), Tooltip("Trace/resolve buffer scale. Presets: Low/Medium 0.5, High 1.0.")]
        public float resolutionScale = 0.5f;
        [Range(8, 128), Tooltip("Max ray-march steps. Presets: 24 / 40 / 64.")]
        public int maxSteps = 40;
        [Range(0, 8), Tooltip("Binary-search refinement steps after a hit. Presets: 3 / 4 / 5.")]
        public int refineSteps = 4;
        [Min(0.1f), Tooltip("World units. Rays fade out toward this distance.")]
        public float maxDistance = 30f;
        [Range(0.01f, 2f), Tooltip("World-unit depth tolerance for a hit.")]
        public float thickness = 0.3f;
        [Range(0f, 0.95f), Tooltip("History weight. Higher = smoother but more ghosting.")]
        public float temporalFeedback = 0.8f;
        [Range(0f, 4f), Tooltip("Extra blur radius (texels) applied to rough surfaces.")]
        public float roughnessBlur = 2f;

        /// <summary>Copies the preset's tier values plus the current global values, so switching to Custom changes nothing visually.</summary>
        public void CopyFromPreset(SSRQuality preset, SSRSettings s)
        {
            var prof = SSRQualityProfile.Get(preset == SSRQuality.Custom ? SSRQuality.Medium : preset);
            resolutionScale = prof.resolutionScale; maxSteps = prof.maxSteps; refineSteps = prof.refineSteps;
            maxDistance = s.maxDistance; thickness = s.thickness;
            temporalFeedback = s.temporalFeedback; roughnessBlur = s.roughnessBlur;
        }

        public void Validate()
        {
            resolutionScale = Mathf.Clamp(resolutionScale, 0.25f, 1f);
            maxSteps = Mathf.Clamp(maxSteps, 8, 128);
            refineSteps = Mathf.Clamp(refineSteps, 0, 8);
            maxDistance = Mathf.Max(0.1f, maxDistance);
            thickness = Mathf.Clamp(thickness, 0.01f, 2f);
            temporalFeedback = Mathf.Clamp(temporalFeedback, 0f, 0.95f);
            roughnessBlur = Mathf.Clamp(roughnessBlur, 0f, 4f);
        }
    }

    /// <summary>Final per-camera, per-frame values after designer settings + player accessibility prefs are merged.
    /// This is the ONLY thing the render pass reads (seam between BLK-02 and BLK-05).</summary>
    public struct SSRResolvedParams
    {
        public bool  enabled;
        public float resolutionScale;
        public int   maxSteps, refineSteps;
        public float maxDistance, thickness, edgeFade, fresnelPower, minReflectance, reflectivity;
        public float intensity, maxOpacity, saturation, maxLuminance;
        public float temporalFeedback;
        public float smoothnessThreshold, smoothnessRamp, smoothnessInfluence, roughnessBlur;
        public bool  jitterPerFrame;       // false => spatial-only noise (no frame-to-frame shimmer)
        public bool  resetHistoryToZero;   // true  => after a history reset, reflections fade in (rate-limited) instead of popping
        public float maxDeltaPerFrame;     // <= 0 => limiter off
    }

    /// <summary>Designer-authored settings (lives on the Renderer Feature asset).</summary>
    [Serializable]
    public sealed class SSRSettings
    {
        [Header("General")]
        public bool enabled = true;
        public bool applyInSceneView = true;
        public SSRQuality quality = SSRQuality.Medium;
        [Tooltip("Used only when Quality is Custom. Replaces the global Max Distance, Thickness, Temporal Feedback and Roughness Blur while Custom is selected.")]
        [ShowIfCustomQuality] public SSRCustomQuality custom = new SSRCustomQuality();
        [HideInInspector] public SSRQuality lastQuality = SSRQuality.Medium;

        [Header("Which surfaces reflect")]
        [Tooltip("Registered: add an SSRSurface component to each reflective MeshRenderer. AllOpaque: everything reflects uniformly. NormalsAlpha: surfaces that write SSR data into the normals texture alpha (StylizedLit with Screen Space Reflection enabled) reflect.")]
        public SSRSurfaceMode surfaceMode = SSRSurfaceMode.Registered;
        [Range(0f, 1f), Tooltip("Base reflectivity for reflective surfaces.")]
        public float reflectivity = 1f;

        [Header("Material smoothness (reads each material's _Smoothness / smoothness map)")]
        [Tooltip("Registered and NormalsAlpha modes: read each material's smoothness (Registered also honours the SSRSurface override). AllOpaque ignores smoothness.")]
        public bool useMaterialSmoothness = true;
        [Range(0f, 1f), Tooltip("Surfaces smoother than this start reflecting.")]
        public float smoothnessThreshold = 0.35f;
        [Range(0.01f, 1f), Tooltip("Smoothness range over which reflection ramps from none to full.")]
        public float smoothnessRamp = 0.3f;
        [Range(0f, 1f), Tooltip("0 = ignore smoothness, 1 = fully driven by smoothness.")]
        public float smoothnessInfluence = 1f;
        [Range(0f, 4f), Tooltip("Extra blur radius (texels) applied to rough surfaces.")]
        public float roughnessBlur = 2f;
        [Range(0f, 1f), Tooltip("Smoothness used for materials without a _Smoothness property.")]
        public float fallbackSmoothness = 0.5f;

        [Header("Ray march")]
        [Min(0.1f), Tooltip("World units. Rays fade out toward this distance.")]
        public float maxDistance = 30f;
        [Range(0.01f, 2f), Tooltip("World-unit depth tolerance for a hit.")]
        public float thickness = 0.3f;
        [Range(0f, 0.5f), Tooltip("UV distance from screen edge over which reflections fade (cf. HDRP Screen Edge Fade Distance).")]
        public float edgeFade = 0.1f;

        [Header("Look")]
        [Range(0.5f, 8f)] public float fresnelPower = 3f;
        [Range(0f, 1f)]   public float minReflectance = 0.1f;
        [Range(0f, 1f)]   public float intensity = 1f;
        [Range(0f, 1f)]   public float maxOpacity = 1f;
        [Min(0f), Tooltip("Cap on added reflection luminance (scene-linear units).")]
        public float maxLuminance = 8f;

        [Header("Temporal")]
        [Range(0f, 0.95f)] public float temporalFeedback = 0.8f;

        [Header("Debug")]
        [Tooltip("Smoothness: grey, magenta = no SSR data. Reflectivity: grey. FinalMask: after threshold/ramp, black = off, blue to red = weak to strong. RawNormalsAlpha: normals texture alpha. Turn off post-processing for exact values. Registered mode shows nothing until an SSRSurface exists.")]
        public SSRDebugView debugView = SSRDebugView.Off;

        /// <summary>Editor hook: when the user switches a preset to Custom, seed Custom from that preset.</summary>
        public void SyncCustomFromPreset()
        {
            if (custom == null) custom = new SSRCustomQuality();
            if (quality == SSRQuality.Custom && lastQuality != SSRQuality.Custom) custom.CopyFromPreset(lastQuality, this);
            lastQuality = quality;
        }

        public void Validate()
        {
            if (custom == null) custom = new SSRCustomQuality();
            custom.Validate();
            maxDistance = Mathf.Max(0.1f, maxDistance);
            thickness = Mathf.Clamp(thickness, 0.01f, 2f);
            edgeFade = Mathf.Clamp(edgeFade, 0f, 0.5f);
            fresnelPower = Mathf.Clamp(fresnelPower, 0.5f, 8f);
            minReflectance = Mathf.Clamp01(minReflectance);
            reflectivity = Mathf.Clamp01(reflectivity);
            intensity = Mathf.Clamp01(intensity);
            maxOpacity = Mathf.Clamp01(maxOpacity);
            maxLuminance = Mathf.Max(0f, maxLuminance);
            temporalFeedback = Mathf.Clamp(temporalFeedback, 0f, 0.95f);
            smoothnessThreshold = Mathf.Clamp01(smoothnessThreshold);
            smoothnessRamp = Mathf.Clamp(smoothnessRamp, 0.01f, 1f);
            smoothnessInfluence = Mathf.Clamp01(smoothnessInfluence);
            roughnessBlur = Mathf.Clamp(roughnessBlur, 0f, 4f);
            fallbackSmoothness = Mathf.Clamp01(fallbackSmoothness);
        }
    }
}
