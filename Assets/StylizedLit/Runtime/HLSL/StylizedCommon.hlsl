// StylizedCommon.hlsl  (Phase 5)
// Shared helpers for all passes. Include AFTER StylizedLitInput.hlsl.
// Clip features (alpha test, dither, dissolve) are evaluated through ApplyStylizedClip in every depth-like pass
// and through ApplyStylizedFeatureClip in ForwardLit (which already alpha-clips inside surface data setup).
#ifndef STYLIZED_COMMON_INCLUDED
#define STYLIZED_COMMON_INCLUDED

// ---- Which interpolators the clip passes need (keep every condition in this one place) ----
#if defined(_ALPHATEST_ON) || defined(_DISSOLVE_ON)
    #define STYLIZED_CLIP_REQUIRES_UV 1
#endif
#if defined(_DISSOLVE_ON) && defined(_DISSOLVE_DIRECTIONAL)
    #define STYLIZED_CLIP_REQUIRES_POSITIONWS 1
#endif
#if defined(_ALPHATEST_ON) || defined(_DISSOLVE_ON) || defined(_DITHER_ON)
    #define STYLIZED_CLIP_ACTIVE 1
#endif

// Accessors so pass code does not need #if blocks. Passes name their world-position varying "positionWSClip".
#if defined(STYLIZED_CLIP_REQUIRES_UV)
    #define STYLIZED_CLIP_UV(i) ((i).uv)
#else
    #define STYLIZED_CLIP_UV(i) float2(0.0, 0.0)
#endif
#if defined(STYLIZED_CLIP_REQUIRES_POSITIONWS)
    #define STYLIZED_CLIP_WS(i) ((i).positionWSClip)
#else
    #define STYLIZED_CLIP_WS(i) float3(0.0, 0.0, 0.0)
#endif

// ---- Dither ----
static const half kBayer4x4[16] = { 0, 8, 2, 10,  12, 4, 14, 6,  3, 11, 1, 9,  15, 7, 13, 5 };

// Ordered 4x4 Bayer threshold in (0,1) for a pixel position (SV_POSITION.xy).
half BayerThreshold(float2 pixelPos)
{
    uint2 p = (uint2)floor(pixelPos / max(_DitherScale, 1.0h)) & 3u;
    return (kBayer4x4[p.y * 4u + p.x] + 0.5h) / 16.0h;
}

// Quantizes color to _DitherColorLevels levels with a Bayer offset. 0 = off. Output is clamped to 0..1.
half3 ApplyDitherPosterize(half3 color, float4 positionCS)
{
#if defined(_DITHER_ON)
    if (_DitherColorLevels >= 2.0h)
    {
        half levels = _DitherColorLevels - 1.0h;
        color = floor(saturate(color) * levels + BayerThreshold(positionCS.xy)) / levels;
    }
#endif
    return color;
}

// ---- Dissolve ----
float StylizedHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float StylizedValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = StylizedHash21(i);
    float b = StylizedHash21(i + float2(1, 0));
    float c = StylizedHash21(i + float2(0, 1));
    float d = StylizedHash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

#if defined(_DISSOLVE_ON)
// > 0: surface remains. 0..edgeWidth: glowing edge band. < 0: dissolved away.
half ComputeDissolveValue(float2 uv, float3 positionWS)
{
    float2 noiseUV = ComputeStylizedUV(uv) * _DissolveNoiseScale;
#if defined(_DISSOLVE_NOISEMAP)
    half n = SAMPLE_TEXTURE2D_LOD(_DissolveNoiseMap, sampler_DissolveNoiseMap, noiseUV, 0).r;
#else
    half n = (half)StylizedValueNoise(noiseUV);
#endif

#if defined(_DISSOLVE_DIRECTIONAL)
    // Dissolve front travels along _DissolveAxis (world space); noise only roughens the front.
    half gradient = (half)saturate((dot(positionWS, _DissolveAxis.xyz) - _DissolveStart) / max(_DissolveLength, 1e-4));
    n = saturate(gradient + (n - 0.5h) * _DissolveNoiseStrength);
#endif

    // Threshold runs from just below -edge (nothing dissolved) to just above 1 (everything dissolved).
    half threshold = lerp(-(_DissolveEdgeWidth + 0.01h), 1.01h, _DissolveAmount);
    return n - threshold;
}

half DissolveEdgeFactor(half dissolveValue)
{
    return 1.0h - smoothstep(0.0h, max(_DissolveEdgeWidth, 1e-4h), dissolveValue);
}
#endif

// ---- Clip entry points ----
// Dither + dissolve only. Returns the dissolve edge factor (0 when dissolve is off) for emission in ForwardLit.
half ApplyStylizedFeatureClip(float2 uv, float4 positionCS, float3 positionWS)
{
    half edge = 0.0h;

#if defined(_DITHER_ON)
    half coverage = _DitherAmount;
#if !defined(STYLIZED_IS_SHADOW_PASS)
    // Camera distance fade (perspective only): hidden closer than Near, fully visible beyond Far.
    if (_DitherFadeFar > _DitherFadeNear && unity_OrthoParams.w < 0.5)
    {
        float eyeDepth = rcp(positionCS.w);
        coverage *= (half)saturate((eyeDepth - _DitherFadeNear) / (_DitherFadeFar - _DitherFadeNear));
    }
#endif
    clip(coverage - BayerThreshold(positionCS.xy));
#endif

#if defined(_DISSOLVE_ON)
    half dissolveValue = ComputeDissolveValue(uv, positionWS);
    clip(dissolveValue);
    edge = DissolveEdgeFactor(dissolveValue);
#endif

    return edge;
}

// Full clip for depth-like passes (ShadowCaster, DepthOnly, DepthNormals, MotionVectors, Outline).
void ApplyStylizedClip(float2 uv, float4 positionCS, float3 positionWS)
{
#if defined(_ALPHATEST_ON)
    Alpha(SampleAlbedoAlpha(ComputeStylizedUV(uv), TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
#endif
    ApplyStylizedFeatureClip(uv, positionCS, positionWS);
}

#endif // STYLIZED_COMMON_INCLUDED
