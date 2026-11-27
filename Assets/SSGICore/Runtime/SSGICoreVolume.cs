using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGICore
{
    public enum SSGICoreQualityPreset
    {
        Low,
        Medium,
        High,
        Custom,
    }

    public enum SSGICoreResolution
    {
        Full = 1,
        Half = 2,
        Thirds = 3,
        Quarter = 4,
    }

    public enum SSGICoreLightingSource
    {
        CurrentFrame,
        PreviousFrame,
        Blended,
    }

    public enum SSGICoreNormalSource
    {
        Raw = 0,
        Octahedral = 1,
        ReconstructFromDepth = 2,
        Auto = 3,
    }

    public enum SSGICoreDenoiserMode
    {
        Legacy = 0,
        Enhanced = 1,
    }

    public enum SSGICoreDebugView
    {
        None = 0,
        GI = 1,
        RawTrace = 2,
        HistoryAge = 3,
        Normals = 4,
        NormalSource = 5,
    }

    [Serializable]
    public sealed class SSGICoreQualityParameter : VolumeParameter<SSGICoreQualityPreset>
    {
        public SSGICoreQualityParameter(SSGICoreQualityPreset value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class SSGICoreResolutionParameter : VolumeParameter<SSGICoreResolution>
    {
        public SSGICoreResolutionParameter(SSGICoreResolution value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class SSGICoreLightingSourceParameter : VolumeParameter<SSGICoreLightingSource>
    {
        public SSGICoreLightingSourceParameter(
            SSGICoreLightingSource value,
            bool overrideState = false
        )
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class SSGICoreNormalSourceParameter : VolumeParameter<SSGICoreNormalSource>
    {
        public SSGICoreNormalSourceParameter(SSGICoreNormalSource value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class SSGICoreDebugViewParameter : VolumeParameter<SSGICoreDebugView>
    {
        public SSGICoreDebugViewParameter(SSGICoreDebugView value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class SSGICoreDenoiserModeParameter : VolumeParameter<SSGICoreDenoiserMode>
    {
        public SSGICoreDenoiserModeParameter(SSGICoreDenoiserMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    /// <summary>Volume override holding every SSGICore setting.</summary>
    [Serializable]
    [VolumeComponentMenu("Lighting/SSGICore")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class SSGICoreVolume : VolumeComponent
    {
        [Header("General")]
        public BoolParameter enable = new BoolParameter(false);
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 8f);
        public SSGICoreQualityParameter quality = new SSGICoreQualityParameter(
            SSGICoreQualityPreset.Medium
        );

        [Header("Custom Quality (used when Quality = Custom)")]
        public SSGICoreResolutionParameter resolution = new SSGICoreResolutionParameter(
            SSGICoreResolution.Half
        );
        public ClampedIntParameter rayCount = new ClampedIntParameter(1, 1, 4);
        public ClampedIntParameter maxSteps = new ClampedIntParameter(24, 8, 96);
        public ClampedIntParameter bounces = new ClampedIntParameter(2, 1, 3);
        public BoolParameter temporal = new BoolParameter(true);
        public ClampedIntParameter temporalFrames = new ClampedIntParameter(16, 2, 64);
        public ClampedIntParameter filterIterations = new ClampedIntParameter(3, 0, 4);

        [Tooltip(
            "Legacy = original temporal + a-trous. Enhanced = neighbourhood firefly rejection, luminance-confidence history, disocclusion fill, age-aware filter tolerance and filtered-history feedback."
        )]
        public SSGICoreDenoiserModeParameter denoiserMode = new SSGICoreDenoiserModeParameter(
            SSGICoreDenoiserMode.Legacy
        );

        [Header("Radiance cache (experimental)")]
        [Tooltip(
            "World-space cache of recently seen lit surfaces, fed from the screen. Rays that leave the screen read it instead of only the ambient cube, so off-screen geometry still bounces light. Range follows Max Distance."
        )]
        public BoolParameter radianceCache = new BoolParameter(false);

        [Header("ReSTIR (experimental)")]
        [Tooltip(
            "Resample first-hit samples across frames and neighbouring pixels (ReSTIR-lite) instead of averaging the rays of each pixel. Costs extra bandwidth; best with fewer rays/filter iterations."
        )]
        public BoolParameter restir = new BoolParameter(false);

        [Tooltip(
            "Also reuse one neighbouring pixel's sample per frame. No visibility test, so can leak light across thin occluders."
        )]
        public BoolParameter restirSpatial = new BoolParameter(true);

        [Tooltip(
            "History confidence cap as a multiple of the per-frame candidate count. Higher = smoother and slower to react."
        )]
        public ClampedIntParameter restirHistoryCap = new ClampedIntParameter(20, 1, 64);

        [Header("Ray March")]
        [Tooltip("Maximum ray length in world units.")]
        public ClampedFloatParameter maxDistance = new ClampedFloatParameter(20f, 1f, 100f);

        [Tooltip("Depth thickness in world units (grows slightly with distance).")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(0.2f, 0.01f, 2f);

        [Tooltip("Screen-edge fade. Higher = narrower fade zone.")]
        public ClampedFloatParameter edgeFade = new ClampedFloatParameter(8f, 0f, 32f);

        [Header("Lighting")]
        public SSGICoreLightingSourceParameter lightingSource = new SSGICoreLightingSourceParameter(
            SSGICoreLightingSource.Blended
        );

        [Tooltip("Weight of the previous frame colour when Lighting Source = Blended.")]
        public ClampedFloatParameter previousFrameWeight = new ClampedFloatParameter(0.5f, 0f, 1f);

        [Tooltip(
            "Multiplier on the ambient (sky / probe) fallback used when a ray leaves the screen."
        )]
        public ClampedFloatParameter fallbackIntensity = new ClampedFloatParameter(1f, 0f, 4f);

        [Tooltip(
            "Approximate albedo level. Forward+ has no albedo buffer, so albedo is estimated from scene colour chroma."
        )]
        public ClampedFloatParameter albedoLevel = new ClampedFloatParameter(0.5f, 0f, 1f);

        [Tooltip("Per-sample luminance clamp that suppresses fireflies.")]
        public ClampedFloatParameter fireflyClamp = new ClampedFloatParameter(20f, 1f, 100f);

        [Header("Temporal")]
        [Tooltip("Variance clipping strength of the history. 0 disables it.")]
        public ClampedFloatParameter temporalClamp = new ClampedFloatParameter(2f, 0f, 4f);

        [Tooltip(
            "Pixels of object motion (beyond camera motion) at which history is mostly rejected. 0 disables."
        )]
        public ClampedFloatParameter motionRejection = new ClampedFloatParameter(8f, 0f, 64f);

        [Header("Light response")]
        [Tooltip(
            "Per-light GI control and dynamic lights. Lights with an SSGICore Light component scale their bounce, and lights that move, change, toggle, spawn or despawn flush the affected temporal history so GI follows them without ghosting. Needs Forward+. Costs nothing while every light has scale 1 and nothing changes."
        )]
        public BoolParameter lightResponse = new BoolParameter(true);

        [Tooltip(
            "Share of the bounce light that ignores per-light GI scale (emissive surfaces, lightmaps, anything not from a realtime light). 0 = all of it follows the lights."
        )]
        public ClampedFloatParameter lightGiFloor = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip(
            "How strongly a light change resets temporal history in its range. 0 = keep history (ghosting), 1 = reset fully."
        )]
        public ClampedFloatParameter lightChangeFlush = new ClampedFloatParameter(0.5f, 0f, 1f);

        [Header("Quality")]
        [Tooltip(
            "Darkens contact areas using the distance to the first hit, so corners and creases are not lit by bounce light that could not reach them. 0 = off."
        )]
        public ClampedFloatParameter contactOcclusion = new ClampedFloatParameter(0.5f, 0f, 1f);

        [Tooltip("Distance in world units over which a nearby hit darkens the surface. Not used by ReSTIR.")]
        public ClampedFloatParameter contactRadius = new ClampedFloatParameter(0.5f, 0.05f, 2f);

        [Tooltip(
            "Rejects grazing and thick-depth hits that let light leak through thin geometry."
        )]
        public BoolParameter leakReduction = new BoolParameter(true);

        [Tooltip(
            "Pixels with long, stable history trace one ray instead of Ray Count. Saves time when Ray Count is above 1."
        )]
        public BoolParameter adaptiveRays = new BoolParameter(true);

        [Header("Advanced")]
        [Tooltip(
            "Auto picks raw / octahedral / depth-reconstructed normals per pixel, so objects whose shaders pack normals differently all work."
        )]
        public SSGICoreNormalSourceParameter normalSource = new SSGICoreNormalSourceParameter(
            SSGICoreNormalSource.Auto
        );
        public SSGICoreDebugViewParameter debugView = new SSGICoreDebugViewParameter(
            SSGICoreDebugView.None
        );

        public bool IsActive() => enable.value && intensity.value > 0f;
    }
}
