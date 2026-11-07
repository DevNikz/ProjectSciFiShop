#ifndef STYLIZED_SHADOW_CASTER_PASS_INCLUDED
#define STYLIZED_SHADOW_CASTER_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
// Shadow maps have no camera: camera-distance dither fade is skipped.
#define STYLIZED_IS_SHADOW_PASS 1
#include "Assets/StylizedLit/Runtime/HLSL/StylizedCommon.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

// Shadow Casting Light geometric parameters. These variables are used when applying the shadow Normal Bias and are set by UnityEngine.Rendering.Universal.ShadowUtils.SetupShadowCasterConstantBuffer in com.unity.render-pipelines.universal/Runtime/ShadowUtils.cs
// For Directional lights, _LightDirection is used when applying shadow Normal Bias.
// For Spot lights and Point lights, _LightPosition is used to compute the actual light direction because it is different at each shadow caster geometry vertex.
float3 _LightDirection;
float3 _LightPosition;

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float2 texcoord     : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    #if defined(STYLIZED_CLIP_REQUIRES_UV)
        float2 uv       : TEXCOORD0;
    #endif
    float4 positionCS   : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
#if defined(STYLIZED_CLIP_REQUIRES_POSITIONWS)
    float3 positionWSClip : TEXCOORD1;
#endif
};

float4 GetShadowPositionHClip(Attributes input)
{
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    positionCS = ApplyShadowClamping(positionCS);
    return positionCS;
}

Varyings StylizedShadowPassVertex(Attributes input)
{
#if defined(STYLIZED_VERTEX_DEFORM)
    input.positionOS.xyz = ApplyStylizedVertexDeformation(input.positionOS.xyz, input.normalOS);
#endif
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    #if defined(STYLIZED_CLIP_REQUIRES_UV)
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    #endif

    output.positionCS = GetShadowPositionHClip(input);
    #if defined(STYLIZED_CLIP_REQUIRES_POSITIONWS)
    output.positionWSClip = TransformObjectToWorld(input.positionOS.xyz);
    #endif
    return output;
}

half4 StylizedShadowPassFragment(Varyings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);

    #if defined(STYLIZED_CLIP_ACTIVE)
        ApplyStylizedClip(STYLIZED_CLIP_UV(input), input.positionCS, STYLIZED_CLIP_WS(input));
    #endif

    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif

    return 0;
}

#endif
