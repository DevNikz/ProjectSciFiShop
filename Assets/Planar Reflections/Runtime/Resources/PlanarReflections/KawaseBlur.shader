Shader "Hidden/PlanarReflections/KawaseBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "KawaseBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Set globally by PlanarReflectionBlur: x,y = 1 / source size.
            float4 _BlurSrcTexelSize;
            float _BlurOffset;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 o = (_BlurOffset + 0.5) * _BlurSrcTexelSize.xy;

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(-o.x, -o.y));
                color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2( o.x, -o.y));
                color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(-o.x,  o.y));
                color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2( o.x,  o.y));
                return color * 0.25h;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
