using System;
using UnityEngine;

namespace GTAO_URP
{
    public enum GTAOApply { Lighting, AfterOpaque }
    public enum GTAOQuality { Low, Medium, High, Ultra, Custom }
    public enum GTAONormals { DepthNormals, ReconstructFromDepth }
    public enum GTAOBlur { Off, Low, Medium, High }
    public enum GTAONoise { Interleaved, Hilbert }

    [Serializable]
    public class GTAOSettings
    {
        [Header("Apply")]
        [Tooltip("Lighting: occludes ambient (+ some direct) in URP lit shaders, same path as URP's built-in SSAO. AfterOpaque: multiplies final camera color.")]
        public GTAOApply apply = GTAOApply.Lighting;
        public GTAONormals normals = GTAONormals.DepthNormals;

        [Header("Shape")]
        [Range(0.05f, 10f)] public float radius = 0.5f;
        [Range(0.1f, 6f)] public float intensity = 1.5f;
        [Range(0.05f, 1f)] public float falloffRange = 0.615f;
        [Range(0f, 0.95f)] public float multiBounce = 0f;
        [Range(0f, 1f)] public float directLightingStrength = 0.25f;

        [Header("Performance")]
        public GTAOQuality quality = GTAOQuality.Medium;
        [Range(1, 6)] public int customSlices = 2;
        [Range(1, 16)] public int customSteps = 4;
        [Range(8f, 512f)] public float maxPixelRadius = 128f;
        public bool downsample = false;
        [Tooltip("AO fades out between start and end. End = 0 disables fading (and skips the far-pixel shortcut).")]
        [Min(0f)] public float fadeStart = 60f;
        [Min(0f)] public float fadeEnd = 100f;

        [Tooltip("Sample AO taps from a prefiltered linear-depth MIP chain (far less memory bandwidth at large radii) instead of full-res camera depth.")]
        public bool useDepthMips = true;
        [Tooltip("Higher = switch to coarser depth MIPs later (sharper, more bandwidth). XeGTAO uses 3.3.")]
        [Range(0f, 6f)] public float depthMipBias = 3.3f;
        [Tooltip("Store the depth MIPs as 32-bit float instead of 16-bit. Costs bandwidth; helps at very large view distances.")]
        public bool highPrecisionDepthMips = false;

        [Header("Sampling")]
        [Tooltip("Exponent of the tap distribution along each slice. 2 = quadratic (denser near the pixel), 1 = uniform. XeGTAO uses 2.")]
        [Range(1f, 3f)] public float sampleDistributionPower = 2f;
        [Tooltip("Lets the horizon fall back behind thin occluders instead of always keeping the highest one. 0 = off (classic height-field). XeGTAO suggests up to 0.7.")]
        [Range(0f, 0.7f)] public float thinOccluderCompensation = 0f;
        [Tooltip("Interleaved: IGN + R1 (original). Hilbert: XeGTAO's Hilbert-curve + R2 spatio-temporal noise, no texture needed.")]
        public GTAONoise noise = GTAONoise.Interleaved;

        [Header("Temporal")]
        public bool temporal = true;
        [Range(0f, 0.98f)] public float temporalWeight = 0.9f;
        [Tooltip("Motion (in pixels) at which history fades out completely.")]
        [Min(1f)] public float velocityFadePixels = 20f;
        [Range(0.001f, 0.2f)] public float depthTolerance = 0.03f;
        [Range(0.5f, 3f)] public float clampStrength = 1.25f;
        public bool useMotionVectors = true;

        [Header("Spatial")]
        public GTAOBlur blur = GTAOBlur.Medium;
        [Range(1f, 500f)] public float blurDepthSharpness = 100f;

        [Header("Debug")]
        public bool debugView = false;

        public void GetSampleCounts(out int slices, out int steps)
        {
            switch (quality)
            {
                case GTAOQuality.Low:    slices = 1; steps = 3; break;
                case GTAOQuality.Medium: slices = 2; steps = 4; break;
                case GTAOQuality.High:   slices = 3; steps = 6; break;
                case GTAOQuality.Ultra:  slices = 4; steps = 8; break;
                default:                 slices = customSlices; steps = customSteps; break;
            }
            slices = Mathf.Clamp(slices, 1, 6);
            steps = Mathf.Clamp(steps, 1, 16);
        }

        public int GetBlurRadius()
        {
            int r = blur == GTAOBlur.Low ? 1 : blur == GTAOBlur.Medium ? 2 : blur == GTAOBlur.High ? 3 : 0;
            return downsample ? Mathf.Max(r, 1) : r; // bilateral upsample needs at least one tap
        }

        // Clamped copy; guards against bad serialized/inspector values reaching the shader.
        public GTAOSettings Sanitized()
        {
            var s = new GTAOSettings();
            SanitizeInto(s);
            return s;
        }

        // Allocation-free variant of Sanitized(): copies every field into dst, clamping as it goes.
        // Every serialized field must be listed here, so add new settings in this method too.
        public void SanitizeInto(GTAOSettings dst)
        {
            dst.apply = apply;
            dst.normals = normals;
            dst.quality = quality;
            dst.blur = blur;
            dst.customSlices = customSlices;
            dst.customSteps = customSteps;
            dst.downsample = downsample;
            dst.temporal = temporal;
            dst.useMotionVectors = useMotionVectors;
            dst.debugView = debugView;
            dst.useDepthMips = useDepthMips;
            dst.noise = noise;
            dst.highPrecisionDepthMips = highPrecisionDepthMips;

            dst.radius = Mathf.Clamp(NanSafe(radius, 0.5f), 0.05f, 10f);
            dst.intensity = Mathf.Clamp(NanSafe(intensity, 1.5f), 0.1f, 6f);
            dst.falloffRange = Mathf.Clamp(NanSafe(falloffRange, 0.615f), 0.05f, 1f);
            dst.multiBounce = Mathf.Clamp(NanSafe(multiBounce, 0f), 0f, 0.95f);
            dst.directLightingStrength = Mathf.Clamp01(NanSafe(directLightingStrength, 0.25f));
            dst.maxPixelRadius = Mathf.Clamp(NanSafe(maxPixelRadius, 128f), 8f, 512f);
            dst.fadeStart = Mathf.Max(0f, NanSafe(fadeStart, 0f));
            dst.fadeEnd = Mathf.Max(0f, NanSafe(fadeEnd, 0f));
            if (dst.fadeEnd > 0f && dst.fadeStart >= dst.fadeEnd) dst.fadeStart = dst.fadeEnd * 0.5f;
            dst.sampleDistributionPower = Mathf.Clamp(NanSafe(sampleDistributionPower, 2f), 1f, 3f);
            dst.thinOccluderCompensation = Mathf.Clamp(NanSafe(thinOccluderCompensation, 0f), 0f, 0.7f);
            dst.depthMipBias = Mathf.Clamp(NanSafe(depthMipBias, 3.3f), 0f, 6f);
            dst.temporalWeight = Mathf.Clamp(NanSafe(temporalWeight, 0.9f), 0f, 0.98f);
            dst.velocityFadePixels = Mathf.Max(1f, NanSafe(velocityFadePixels, 20f));
            dst.depthTolerance = Mathf.Clamp(NanSafe(depthTolerance, 0.03f), 0.001f, 0.2f);
            dst.clampStrength = Mathf.Clamp(NanSafe(clampStrength, 1.25f), 0.5f, 3f);
            dst.blurDepthSharpness = Mathf.Clamp(NanSafe(blurDepthSharpness, 100f), 1f, 500f);
        }

        static float NanSafe(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;
    }
}
