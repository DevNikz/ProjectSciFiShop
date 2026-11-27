#ifndef PLANAR_REFLECTION_INCLUDED
#define PLANAR_REFLECTION_INCLUDED

// Bound per reflector through a MaterialPropertyBlock (see PlanarReflector.cs).
TEXTURE2D(_PlanarReflectionTex);
SAMPLER(sampler_PlanarReflectionTex);
TEXTURE2D(_PlanarReflectionBlurTex);
SAMPLER(sampler_PlanarReflectionBlurTex);

// x: fade start distance, y: 1 / fade range, z: max roughness LOD (Mips mode), w: blur blend (Blurred mode).
// All zero (unbound) means: no fade, sharp, mip 0.
float4 _PlanarReflectionParams;

// Screen UV of the pixel without the TAA/STP projection jitter, so the sample lines up with the
// unjittered reflection render. Falls back to fallbackUV when the matrix is not set.
float2 GetPlanarReflectionScreenUV(float3 positionWS, float2 fallbackUV)
{
    float4 clip = mul(_NonJitteredViewProjMatrix, float4(positionWS, 1.0));
    if (clip.w <= 1e-4)
        return fallbackUV;

    float2 uv = clip.xy / clip.w * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
    uv.y = 1.0 - uv.y;
#endif
    return uv;
}

// screenUV: normalized screen UV of the shaded pixel. distortion: UV-space offset. roughness: 0..1.
half3 SamplePlanarReflection(float2 screenUV, float2 distortion, half roughness)
{
    float2 uv = screenUV + distortion;

    half lod = roughness * _PlanarReflectionParams.z;
    half3 sharp = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv, lod).rgb;
    half3 blurred = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionBlurTex, sampler_PlanarReflectionBlurTex, uv, 0).rgb;

    half blurWeight = saturate(roughness * _PlanarReflectionParams.w);
    return lerp(sharp, blurred, blurWeight);
}

// 1 near the camera, 0 past the fade end distance. Always 1 when fade is disabled.
half GetPlanarReflectionFade(float3 positionWS)
{
    float distanceToCamera = length(positionWS - _WorldSpaceCameraPos);
    return saturate(1.0 - (distanceToCamera - _PlanarReflectionParams.x) * _PlanarReflectionParams.y);
}

#endif
