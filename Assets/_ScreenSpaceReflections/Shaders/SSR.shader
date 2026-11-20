// BLK-03  SSR shader. Pass 0 Trace | Pass 1 Resolve | Pass 2 Composite | Pass 3 Debug (SSRDebugView).
// Property names are the seam contract with SSRRenderPass.Ids (owner: BLK-03).
Shader "Hidden/SSR/Reflections"
{
    Properties
    {
        [HideInInspector] _SSRHistory("", 2D) = "black" {}
        [HideInInspector] _SSRTexel("", Vector) = (1,1,1,1)
        [HideInInspector] _SSRMaxDistance("", Float) = 30
        [HideInInspector] _SSRThickness("", Float) = 0.3
        [HideInInspector] _SSRMaxSteps("", Float) = 40
        [HideInInspector] _SSRRefineSteps("", Float) = 4
        [HideInInspector] _SSREdgeFade("", Float) = 0.1
        [HideInInspector] _SSRFresnelPower("", Float) = 3
        [HideInInspector] _SSRMinReflectance("", Float) = 0.1
        [HideInInspector] _SSRReflectivity("", Float) = 1
        [HideInInspector] _SSRFrame("", Float) = 0
        [HideInInspector] _SSRFeedback("", Float) = 0.8
        [HideInInspector] _SSRHistoryValid("", Float) = 0
        [HideInInspector] _SSRSafeReset("", Float) = 0
        [HideInInspector] _SSRMaxDelta("", Float) = 0
        [HideInInspector] _SSRMaxLuma("", Float) = 8
        [HideInInspector] _SSRIntensity("", Float) = 1
        [HideInInspector] _SSRMaxOpacity("", Float) = 1
        [HideInInspector] _SSRSaturation("", Float) = 1
        [HideInInspector] _SSRSmoothMin("", Float) = 0.35
        [HideInInspector] _SSRSmoothRamp("", Float) = 0.3
        [HideInInspector] _SSRSmoothInfluence("", Float) = 1
        [HideInInspector] _SSRBlur("", Float) = 2
        [HideInInspector] _SSRDebugView("", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_SSRMask);
        TEXTURE2D_X(_SSRHistory);
        TEXTURE2D_X(_MotionVectorTexture);

        CBUFFER_START(UnityPerMaterial)
            float4 _SSRTexel;           // w, h, 1/w, 1/h of the trace/history targets
            float _SSRMaxDistance, _SSRThickness, _SSRMaxSteps, _SSRRefineSteps, _SSREdgeFade;
            float _SSRFresnelPower, _SSRMinReflectance, _SSRReflectivity, _SSRFrame;
            float _SSRFeedback, _SSRHistoryValid, _SSRSafeReset, _SSRMaxDelta, _SSRMaxLuma;
            float _SSRIntensity, _SSRMaxOpacity, _SSRSaturation;
            float _SSRSmoothMin, _SSRSmoothRamp, _SSRSmoothInfluence, _SSRBlur;
            float _SSRDebugView;
        CBUFFER_END

        bool IsSky(float raw)
        {
        #if UNITY_REVERSED_Z
            return raw <= 0.0;
        #else
            return raw >= 1.0;
        #endif
        }

        // Same y convention as ComputeNormalizedDeviceCoordinatesWithZ / ComputeWorldSpacePosition in URP core. [GAP] verify GL/Vulkan RT flip.
        float4 ProjectFlipped(float3 posWS)
        {
            float4 c = mul(UNITY_MATRIX_VP, float4(posWS, 1.0));
        #if UNITY_UV_STARTS_AT_TOP
            c.y = -c.y;
        #endif
            return c;
        }

        float EyeDepthAt(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }

        float3 PosAt(float2 uv) { return ComputeWorldSpacePosition(uv, SampleSceneDepth(uv), UNITY_MATRIX_I_VP); }

        // Uses the normals texture when available, else reconstructs from depth (picks the closer neighbour per axis to survive edges).
        float3 GetNormal(float2 uv, float3 p)
        {
        #if defined(_SSR_NO_NORMALS)
            float2 px = rcp(_ScaledScreenParams.xy);
            float3 pR = PosAt(uv + float2(px.x, 0)), pL = PosAt(uv - float2(px.x, 0));
            float3 pU = PosAt(uv + float2(0, px.y)), pD = PosAt(uv - float2(0, px.y));
            float3 dx = (dot(pR - p, pR - p) < dot(p - pL, p - pL)) ? (pR - p) : (p - pL);
            float3 dy = (dot(pU - p, pU - p) < dot(p - pD, p - pD)) ? (pU - p) : (p - pD);
            float3 n = normalize(cross(dx, dy));
            return dot(n, GetCameraPositionWS() - p) < 0.0 ? -n : n;
        #else
            return SampleSceneNormals(uv);
        #endif
        }

        // Per-pixel SSR inputs. mask = reflectivity (0 = does not reflect), smoothness 0..1. The source is chosen by keyword:
        // Registered (_SSRMask RT), NormalsAlpha (StylizedLit packs smoothness*15 << 4 | intensity*15 into _CameraNormalsTexture.a;
        // keep in sync with StylizedLitDepthNormalsPass.hlsl), or AllOpaque (uniform).
        void DecodeSSRMask(float2 uv, out float mask, out float smoothness)
        {
            mask = _SSRReflectivity;
            smoothness = 1.0;
        #if defined(_SSR_USE_MASK)
            float2 mg = SAMPLE_TEXTURE2D_X_LOD(_SSRMask, sampler_PointClamp, uv, 0).rg;   // R layer reflectivity, G material smoothness
            mask = mg.r; smoothness = mg.g;
        #elif defined(_SSR_MASK_NORMALS_A)
            float packedByte = round(SAMPLE_TEXTURE2D_X_LOD(_CameraNormalsTexture, sampler_PointClamp, uv, 0).a * 255.0);
            float smoothHigh = floor(packedByte / 16.0);
            smoothness = smoothHigh / 15.0;
            mask *= (packedByte - smoothHigh * 16.0) / 15.0;
        #endif
        }
        ENDHLSL

        // ------------------------------------------------------------------ Pass 0: Trace
        Pass
        {
            Name "SSRTrace"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTrace
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_local_fragment _ _SSR_USE_MASK
            #pragma multi_compile_local_fragment _ _SSR_MASK_NORMALS_A
            #pragma multi_compile_local_fragment _ _SSR_NO_NORMALS

            // _BlitTexture = camera opaque color copy (pre-SSR). Output is PREMULTIPLIED: rgb = color*conf, a = conf.
            float4 FragTrace(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = SampleSceneDepth(uv);
                if (IsSky(raw)) return 0;

                float mask, smoothness;
                DecodeSSRMask(uv, mask, smoothness);
                float sw = smoothstep(_SSRSmoothMin, _SSRSmoothMin + _SSRSmoothRamp, smoothness);
                mask *= lerp(1.0, sw, _SSRSmoothInfluence);
                if (mask <= 0.0) return 0;

                float3 posWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 N = GetNormal(uv, posWS);
                float3 viewDir = normalize(posWS - GetCameraPositionWS());     // camera -> surface
                float3 R = reflect(viewDir, N);

                float NdotV = saturate(dot(N, -viewDir));
                float fres = lerp(_SSRMinReflectance, 1.0, pow(1.0 - NdotV, _SSRFresnelPower));
                float facing = smoothstep(0.0, 0.2, dot(R, viewDir));          // rays heading back at the camera can't hit anything visible

                float eyeD = LinearEyeDepth(raw, _ZBufferParams);
                float bias = max(0.01, 0.002 * eyeD);
                float3 startWS = posWS + N * bias;
                float3 endWS = startWS + R * _SSRMaxDistance;

                float4 h0 = ProjectFlipped(startWS);
                float4 h1 = ProjectFlipped(endWS);
                float nearW = _ProjectionParams.y;
                if (h1.w < nearW) h1 = lerp(h0, h1, (nearW - h0.w) / (h1.w - h0.w));

                float k0 = rcp(h0.w), k1 = rcp(h1.w);
                float2 p0 = h0.xy * k0 * 0.5 + 0.5;
                float2 p1 = h1.xy * k1 * 0.5 + 0.5;
                float2 d = p1 - p0;

                // clip to screen rect
                float2 distToEdge = lerp(p0, 1.0 - p0, step(0.0, d)) * rcp(max(abs(d), 1e-6));
                float tMax = min(1.0, min(distToEdge.x, distToEdge.y));

                float pixLen = length(d * tMax * _ScaledScreenParams.xy);
                int steps = (int)clamp(ceil(pixLen), 1.0, _SSRMaxSteps);
                float stepT = tMax / steps;

                float2 pix = uv * _ScaledScreenParams.xy;
                float jitter = InterleavedGradientNoise(pix, (int)_SSRFrame);   // frame = 0 when shimmer reduction is on

                bool hit = false;
                float tPrev = 0.0, tHit = 0.0;
                [loop] for (int s = 0; s < steps; s++)
                {
                    float t = (s + 1.0 + jitter - 0.5) * stepT;
                    t = min(t, tMax);
                    float2 uvS = lerp(p0, p1, t);
                    float rayEye = rcp(lerp(k0, k1, t));
                    float diff = rayEye - EyeDepthAt(uvS);
                    float thick = _SSRThickness * (1.0 + rayEye * 0.02);
                    if (diff > 0.0 && diff < thick) { hit = true; tHit = t; break; }
                    tPrev = t;
                }
                if (!hit) return 0;

                // binary refinement between last miss and first hit
                float lo = tPrev, hi = tHit;
                [loop] for (int r = 0; r < (int)_SSRRefineSteps; r++)
                {
                    float mid = 0.5 * (lo + hi);
                    float rayEyeM = rcp(lerp(k0, k1, mid));
                    float dm = rayEyeM - EyeDepthAt(lerp(p0, p1, mid));
                    if (dm > 0.0) hi = mid; else lo = mid;
                }
                float2 hitUV = lerp(p0, p1, hi);

                // reject back-face hits
                float3 hitWS = PosAt(hitUV);
                float3 Nh = GetNormal(hitUV, hitWS);
                if (dot(Nh, R) > 0.0) return 0;

                float distFade = 1.0 - saturate(length(hitWS - posWS) / _SSRMaxDistance);
                distFade *= distFade;
                float2 e = min(hitUV, 1.0 - hitUV);
                float edge = saturate(min(e.x, e.y) / max(_SSREdgeFade, 1e-4));

                float conf = saturate(mask * fres * facing * distFade * edge);
                float3 col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, hitUV, 0).rgb;
                return float4(col * conf, conf);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ Pass 1: Resolve (spatial + temporal + safety limiter)
        Pass
        {
            Name "SSRResolve"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragResolve
            #pragma multi_compile_local_fragment _ _SSR_NO_MOTION
            #pragma multi_compile_local_fragment _ _SSR_USE_MASK
            #pragma multi_compile_local_fragment _ _SSR_MASK_NORMALS_A

            float4 FragResolve(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float zc = EyeDepthAt(uv);
                float sm, resolveMask;
                DecodeSSRMask(uv, resolveMask, sm);
                float blurScale = 1.0 + _SSRBlur * (1.0 - sm) * _SSRSmoothInfluence;   // rougher => wider kernel

                // 3x3 depth-aware blur + neighbourhood bounds
                float4 acc = 0; float wsum = 0;
                float4 nmin = 1e9, nmax = -1e9;
                [unroll] for (int j = -1; j <= 1; j++)
                {
                    [unroll] for (int i = -1; i <= 1; i++)
                    {
                        float2 o = float2(i, j) * _SSRTexel.zw * blurScale;
                        float2 suv = uv + o;
                        float4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, suv, 0);
                        float kw = (i == 0 ? 2.0 : 1.0) * (j == 0 ? 2.0 : 1.0);
                        float sim = (i == 0 && j == 0) ? 1.0 : saturate(1.0 - abs(EyeDepthAt(suv) - zc) / (0.05 * zc + 0.01));
                        float w = kw * sim;
                        acc += c * w; wsum += w;
                        nmin = min(nmin, c); nmax = max(nmax, c);
                    }
                }
                float4 cur = acc / max(wsum, 1e-4);
                if (any(isnan(cur)) || any(isinf(cur))) cur = 0;

                // luminance cap (added luminance)
                float l = Luminance(cur.rgb);
                if (l > _SSRMaxLuma) cur.rgb *= _SSRMaxLuma / max(l, 1e-4);

                // reproject history
            #if defined(_SSR_NO_MOTION)
                float2 prevUV = uv;
            #else
                float2 mv = SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_PointClamp, uv, 0).xy;
                float2 prevUV = uv - mv;   // [GAP] verify sign/units vs URP TAA on your version
            #endif
                bool histOk = _SSRHistoryValid > 0.5 && all(prevUV >= 0.0) && all(prevUV <= 1.0);

                float4 hist = cur;                                   // default: no history => take current
                if (histOk) hist = SAMPLE_TEXTURE2D_X_LOD(_SSRHistory, sampler_LinearClamp, prevUV, 0);
                else if (_SSRSafeReset > 0.5) hist = 0;              // safe/reduced-motion: fade in from nothing
                if (any(isnan(hist)) || any(isinf(hist))) hist = 0;

                bool limiter = _SSRMaxDelta > 0.0;
                float4 histB = (histOk && !limiter) ? clamp(hist, nmin, nmax) : hist;   // ghost clamp only when limiter is off
                float4 outc = lerp(cur, histB, _SSRFeedback);

                if (limiter)
                {
                    float4 dlt = outc - hist;
                    float m = max(abs(Luminance(dlt.rgb)), abs(dlt.a));
                    if (m > _SSRMaxDelta) outc = hist + dlt * (_SSRMaxDelta / m);
                }
                return outc;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ Pass 2: Composite
        Pass
        {
            Name "SSRComposite"
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            float4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0);
                float a = r.a * _SSRIntensity;
                float aOut = min(a, _SSRMaxOpacity);
                float3 rgb = r.a > 1e-5 ? r.rgb * (aOut / r.a) : 0;
                float l = Luminance(rgb);
                rgb = lerp(l.xxx, rgb, _SSRSaturation);                 // accessibility: desaturate reflections
                return float4(rgb, aOut);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ Pass 3: Debug view (overwrites camera color; see SSRDebugView)
        Pass
        {
            Name "SSRDebug"
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDebug
            #pragma multi_compile_local_fragment _ _SSR_USE_MASK
            #pragma multi_compile_local_fragment _ _SSR_MASK_NORMALS_A

            float3 HeatRamp(float t)
            {
                return saturate(float3(1.5 - abs(4.0 * t - 3.0), 1.5 - abs(4.0 * t - 2.0), 1.5 - abs(4.0 * t - 1.0)));
            }

            float4 FragDebug(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (IsSky(SampleSceneDepth(uv))) return float4(0, 0, 0, 1);

                float mask, sm;
                DecodeSSRMask(uv, mask, sm);
                float finalMask = mask * lerp(1.0, smoothstep(_SSRSmoothMin, _SSRSmoothMin + _SSRSmoothRamp, sm), _SSRSmoothInfluence);

                float3 c;
                if (_SSRDebugView < 1.5)        // 1 Smoothness: grey; magenta = no SSR data (does not reflect)
                    c = mask > 0.0 ? sm.xxx : float3(0.35, 0.0, 0.35);
                else if (_SSRDebugView < 2.5)   // 2 Reflectivity (intensity x feature reflectivity): grey
                    c = mask.xxx;
                else if (_SSRDebugView < 3.5)   // 3 Final mask the trace uses after threshold/ramp: black = off, blue -> red = weak -> strong
                    c = finalMask > 0.0 ? HeatRamp(finalMask) : float3(0, 0, 0);
                else                            // 4 Raw _CameraNormalsTexture alpha
                    c = SAMPLE_TEXTURE2D_X_LOD(_CameraNormalsTexture, sampler_PointClamp, uv, 0).aaa;
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
