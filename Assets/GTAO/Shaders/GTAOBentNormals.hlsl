// GTAOBentNormals.hlsl : consumer helpers for the GTAO bent-normal output.
//
// Usage in your own URP shader (URP Lit does NOT read bent normals):
//   #pragma multi_compile_fragment _ _GTAO_BENT_NORMALS
//   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"   // before this file
//   #include ".../GTAOBentNormals.hlsl"
//   float3 bentWS = GTAO_GetBentNormalWS(screenUV, normalWS, 1.0);   // use for ambient / GI / probe lookups
//   float  specOcc = GTAO_SpecularOcclusion(saturate(dot(normalWS, viewDirWS)), ao, perceptualRoughness);
//
// The keyword _GTAO_BENT_NORMALS is enabled globally by the GTAO feature only while bent normals are produced for
// the current camera, so without it every function here falls back to the geometric normal / plain AO.
// screenUV is the normalised screen position of the pixel (positionCS.xy / _ScaledScreenParams.xy).

#ifndef GTAO_BENT_NORMALS_INCLUDED
#define GTAO_BENT_NORMALS_INCLUDED

TEXTURE2D_X(_GTAO_BentNormalsTexture);
SAMPLER(sampler_GTAO_BentNormalsTexture); // point filtered: full-res, already upsampled

// xyz = world-space unit bent normal, w = 1 if valid (0 = no usable bent normal at this pixel).
float4 GTAO_SampleBentNormalWS(float2 screenUV)
{
#if defined(_GTAO_BENT_NORMALS)
    float4 v = SAMPLE_TEXTURE2D_X_LOD(_GTAO_BentNormalsTexture, sampler_GTAO_BentNormalsTexture, screenUV, 0);
    return float4(v.xyz * 2.0 - 1.0, v.w);
#else
    return float4(0.0, 0.0, 0.0, 0.0);
#endif
}

// Bent normal blended with the geometric normal. strength 0 = geometric normal, 1 = full bent normal.
// Falls back to normalWS where the bent normal is invalid or the feature is off.
float3 GTAO_GetBentNormalWS(float2 screenUV, float3 normalWS, float strength)
{
    float4 b = GTAO_SampleBentNormalWS(screenUV);
    float3 n = lerp(normalWS, b.xyz, saturate(strength) * b.w);
    return normalize(n);
}

// Specular occlusion from AO, view angle and roughness (Lagarde & de Rousiers, "Moving Frostbite to PBR", 2014).
// perceptualRoughness = 1 - smoothness. Returns 1 for ao == 1.
float GTAO_SpecularOcclusion(float NoV, float ao, float perceptualRoughness)
{
    return saturate(pow(max(NoV + ao, 1e-4), exp2(-16.0 * perceptualRoughness - 1.0)) - 1.0 + ao);
}

// Shader Graph Custom Function wrappers (Type: File, names without the _float suffix).
void GTAO_BentNormal_float(float2 ScreenUV, float3 NormalWS, float Strength, out float3 BentNormalWS)
{
#if defined(SHADERGRAPH_PREVIEW)
    BentNormalWS = NormalWS;
#else
    BentNormalWS = GTAO_GetBentNormalWS(ScreenUV, NormalWS, Strength);
#endif
}

void GTAO_SpecularOcclusion_float(float NoV, float AO, float PerceptualRoughness, out float SpecularOcclusion)
{
    SpecularOcclusion = GTAO_SpecularOcclusion(NoV, AO, PerceptualRoughness);
}

#endif // GTAO_BENT_NORMALS_INCLUDED
