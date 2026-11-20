// BLK-03  Regular (non-override) mask shader, drawn by the pass via cmd.DrawMesh with one material per source material.
// Output: R = reflectivity (_SSRMaskValue), G = smoothness (_SSRSmoothOverride >= 0 ? override : _Smoothness * map alpha).
// Depth test is manual against _CameraDepthTexture (MSAA safe). Not batched through BRG, so no DOTS variant is needed.
Shader "Hidden/SSR/Mask"
{
    Properties
    {
        _BaseMap("", 2D) = "white" {}
        _MetallicGlossMap("", 2D) = "white" {}
        _Smoothness("", Float) = 0.5
        _SmoothnessTextureChannel("", Float) = 0
        _SSRMaskValue("", Float) = 1
        _SSRSmoothOverride("", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SSRMask"
            ZWrite Off ZTest Always Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap);          SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Smoothness;
                float _SmoothnessTextureChannel;   // 0 = metallic-map alpha, 1 = albedo alpha
                float _SSRMaskValue;
                float _SSRSmoothOverride;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            float2 frag(Varyings i) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float fragEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                clip(0.01 + 0.002 * sceneEye - abs(fragEye - sceneEye));    // keep only the visible surface

                float a = (_SmoothnessTextureChannel > 0.5)
                    ? SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a
                    : SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv).a;
                float sm = (_SSRSmoothOverride >= 0.0) ? _SSRSmoothOverride : saturate(_Smoothness * a);
                return float2(_SSRMaskValue, sm);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
