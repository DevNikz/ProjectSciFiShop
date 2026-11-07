#ifndef STYLIZED_DEPTH_ONLY_PASS_INCLUDED
#define STYLIZED_DEPTH_ONLY_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/StylizedLit/Runtime/HLSL/StylizedCommon.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct Attributes
{
    float4 position     : POSITION;
    float2 texcoord     : TEXCOORD0;
    #if defined(STYLIZED_VERTEX_DEFORM)
    float3 normalOS     : NORMAL;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    #if defined(STYLIZED_CLIP_REQUIRES_UV)
        float2 uv       : TEXCOORD0;
    #endif
    float4 positionCS   : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
#if defined(STYLIZED_CLIP_REQUIRES_POSITIONWS)
    float3 positionWSClip : TEXCOORD1;
#endif
};

Varyings StylizedDepthOnlyVertex(Attributes input)
{
#if defined(STYLIZED_VERTEX_DEFORM)
    input.position.xyz = ApplyStylizedVertexDeformation(input.position.xyz, input.normalOS);
#endif
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    #if defined(STYLIZED_CLIP_REQUIRES_UV)
        output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    #endif
    output.positionCS = TransformObjectToHClip(input.position.xyz);
    #if defined(STYLIZED_CLIP_REQUIRES_POSITIONWS)
    output.positionWSClip = TransformObjectToWorld(input.position.xyz);
    #endif
    return output;
}

half StylizedDepthOnlyFragment(Varyings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    #if defined(STYLIZED_CLIP_ACTIVE)
        ApplyStylizedClip(STYLIZED_CLIP_UV(input), input.positionCS, STYLIZED_CLIP_WS(input));
    #endif

    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif

    return input.positionCS.z;
}
#endif
