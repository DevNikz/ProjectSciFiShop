// BLK-03  SSR shader. Pass 0 Trace (Hi-Z, writes colour+conf and hit distance) | Pass 1 Resolve (temporal only, hit-point reprojection)
// | Pass 2 Composite (depth+normal-aware upsample) | Pass 3 Debug (SSRDebugView) | Pass 4 HiZ base (eye depth, mip 0)
// | Pass 5 HiZ reduce (min eye depth, one mip per draw) | Pass 6 Denoise (edge-aware a-trous, run N times after Resolve).
// Property names are the seam contract with SSRRenderPass.Ids (owner: BLK-03).
Shader "Hidden/SSR/Reflections"
{
    Properties
    {
        [HideInInspector] _SSRHistory("", 2D) = "black" {}
        [HideInInspector] _SSRHiZ("", 2D) = "black" {}
        [HideInInspector] _SSRHiZDims("", Vector) = (1,1,1,1)
        [HideInInspector] _SSRHiZMaxMip("", Float) = 0
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
        [HideInInspector] _SSRDistFadePower("", Float) = 1
        [HideInInspector] _SSRNormalSharpness("", Float) = 16
        [HideInInspector] _SSRHitReproj("", Float) = 1
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
        TEXTURE2D_X(_SSRTraceHit);    // R16F distance (world units) surface -> hit, 0 = no hit (written by Trace SV_Target1)
        TEXTURE2D(_SSRHiZ);           // R32F min eye depth, full-res mip chain (SSRRenderPass.RecordDepthPyramid)
        float4 _SSRHiZSrc;            // global, set per reduce draw: x = source mip, y/z = source width/height
        float4x4 _SSRPrevVP;          // global, set in the Resolve pass: previous frame unjittered GPU view-projection of this camera
        float _SSRDenoiseStep;        // global, set per denoise draw (cmd.SetGlobal keeps the per-pass order): a-trous stride 1,2,4..
        TEXTURE2D_X(_MotionVectorTexture);

        CBUFFER_START(UnityPerMaterial)
            float4 _SSRTexel;           // w, h, 1/w, 1/h of the trace/history targets
            float4 _SSRHiZDims;         // w, h, 1/w, 1/h of Hi-Z mip 0 (= full-res camera target)
            float _SSRHiZMaxMip;
            float _SSRMaxDistance, _SSRThickness, _SSRMaxSteps, _SSRRefineSteps, _SSREdgeFade;
            float _SSRFresnelPower, _SSRMinReflectance, _SSRReflectivity, _SSRFrame;
            float _SSRFeedback, _SSRHistoryValid, _SSRSafeReset, _SSRMaxDelta, _SSRMaxLuma;
            float _SSRIntensity, _SSRMaxOpacity, _SSRSaturation;
            float _SSRSmoothMin, _SSRSmoothRamp, _SSRSmoothInfluence, _SSRBlur;
            float _SSRDebugView;
            float _SSRDistFadePower, _SSRNormalSharpness, _SSRHitReproj;
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

        // Mask after the smoothness threshold/ramp. 0 = this pixel never reflects.
        float DecodeFinalMask(float2 uv, out float smoothness)
        {
            float mask;
            DecodeSSRMask(uv, mask, smoothness);
            float sw = smoothstep(_SSRSmoothMin, _SSRSmoothMin + _SSRSmoothRamp, smoothness);
            return mask * lerp(1.0, sw, _SSRSmoothInfluence);
        }

        struct TraceOut { float4 color : SV_Target0; float hitDist : SV_Target1; };
        TraceOut MakeTraceOut(float4 c, float d) { TraceOut o; o.color = c; o.hitDist = d; return o; }

        // Spatial IGN pattern, rotated per frame with the R2 sequence (low discrepancy over time => the temporal pass converges faster).
        // frame = 0 (shimmer reduction on) gives the static spatial pattern.
        float2 Noise2D(float2 pix, int frame)
        {
            float2 spatial = float2(InterleavedGradientNoise(pix, 0), InterleavedGradientNoise(pix + 19.19, 0));
            return frac(spatial + float2(0.7548776662, 0.5698402910) * (float)(frame & 255));
        }

        // Where this pixel's reflection was on screen last frame. The mirror image of the hit point sits along the view ray at
        // (surface distance + hit distance); for a planar mirror it is static in world space, so camera motion reprojects exactly.
        // Returns -1 when behind the previous camera.
        float2 ReprojectByHit(float3 posWS, float hitDist)
        {
            float3 virtualWS = posWS + normalize(posWS - GetCameraPositionWS()) * hitDist;
            float4 c = mul(_SSRPrevVP, float4(virtualWS, 1.0));
        #if UNITY_UV_STARTS_AT_TOP
            c.y = -c.y;
        #endif
            return c.w > 1e-4 ? c.xy / c.w * 0.5 + 0.5 : float2(-1.0, -1.0);
        }

        static const float kRoughSpread = 0.75;   // cone size at roughness 1, applied as roughness^2

        // Stochastic cone around R, wider for rough surfaces. Noise is resolved by the spatial + temporal filter.
        float3 PerturbReflectionDir(float3 R, float3 N, float2 pix, float smoothness)
        {
            float rough = 1.0 - smoothness;
            float spread = rough * rough * kRoughSpread * _SSRSmoothInfluence;
            if (spread <= 1e-4) return R;

            float2 xi = Noise2D(pix, (int)_SSRFrame);
            float3 T = normalize(abs(R.y) < 0.99 ? cross(R, float3(0, 1, 0)) : cross(R, float3(1, 0, 0)));
            float3 B = cross(R, T);
            float a = xi.x * 6.2831853;
            float3 Rp = normalize(R + (T * cos(a) + B * sin(a)) * sqrt(xi.y) * spread);

            float ndr = dot(Rp, N);                                   // keep the ray above the surface
            if (ndr < 0.05) Rp = normalize(Rp + N * (0.05 - ndr));
            return Rp;
        }

        // Hierarchical march in screen space. Ray: pixel pos = pp0 + dd * t, 1/eyeDepth = k0 + (k1 - k0) * t.
        // Hi-Z cell = min eye depth. A cell is skipped while the ray stays in front of it, refined down to mip 0 otherwise.
        // At mip 0 a hit needs the ray within the thickness behind the pixel's depth; farther behind = passed behind, keep going.
        bool TraceHiZ(float2 pp0, float2 dd, float k0, float k1, float t, float tMax, int maxIter, int maxMip, out float tHit)
        {
            tHit = 0.0;
            float pixLen = max(length(dd), 1e-4);
            float epsT = 0.01 / pixLen;
            float dk = k1 - k0;
            float2 dims = _SSRHiZDims.xy;

            // slope-aware tolerance: one pixel of ray depth travel on top of the world-unit thickness
            float dzPix = abs(rcp(k0 + dk * tMax) - rcp(k0)) / max(tMax * pixLen, 1.0);

            int mip = 0;
            [loop] for (int i = 0; i < maxIter; i++)
            {
                if (t >= tMax) return false;

                float2 pp = pp0 + dd * t;
                float cs = exp2((float)mip);
                float2 cell = floor(pp / cs);

                float2 bound = (cell + step(0.0, dd)) * cs;           // cell edge in the travel direction
                float tx = abs(dd.x) > 1e-5 ? (bound.x - pp0.x) / dd.x : 1e8;
                float ty = abs(dd.y) > 1e-5 ? (bound.y - pp0.y) / dd.y : 1e8;
                float tExit = min(min(tx, ty), tMax);

                int2 mdim = max((int2)dims >> mip, 1);
                float cellMin = LOAD_TEXTURE2D_LOD(_SSRHiZ, clamp((int2)cell, 0, mdim - 1), mip).r;

                float a = rcp(k0 + dk * t);
                float b = rcp(k0 + dk * tExit);
                if (max(a, b) < cellMin)
                {
                    t = tExit + epsT;                                  // ray stays in front of everything in this cell
                    mip = min(mip + 1, maxMip);
                    continue;
                }

                float tCand = t;                                       // first t inside the cell where the ray reaches cellMin
                if (a < cellMin && abs(dk) > 1e-9) tCand = clamp((rcp(cellMin) - k0) / dk, t, tExit);

                if (mip > 0) { t = tCand; mip--; continue; }

                float thick = _SSRThickness * (1.0 + a * 0.02) + dzPix;
                if (rcp(k0 + dk * tCand) - cellMin < thick) { tHit = tCand; return true; }

                t = tExit + epsT;                                      // behind the surface by more than the thickness
                mip = min(mip + 1, maxMip);
            }
            return false;
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
            TraceOut FragTrace(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = SampleSceneDepth(uv);
                if (IsSky(raw)) return MakeTraceOut(0, 0);

                float smoothness;
                float mask = DecodeFinalMask(uv, smoothness);
                if (mask <= 0.0) return MakeTraceOut(0, 0);

                float3 posWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 N = GetNormal(uv, posWS);
                float3 viewDir = normalize(posWS - GetCameraPositionWS());     // camera -> surface
                float3 R = reflect(viewDir, N);

                float NdotV = saturate(dot(N, -viewDir));
                float fres = lerp(_SSRMinReflectance, 1.0, pow(1.0 - NdotV, _SSRFresnelPower));
                float facing = smoothstep(0.0, 0.2, dot(R, viewDir));          // rays heading back at the camera can't hit anything visible

                float2 pix = uv * _ScaledScreenParams.xy;
                R = PerturbReflectionDir(R, N, pix, smoothness);

                float eyeD = LinearEyeDepth(raw, _ZBufferParams);
                float bias = max(0.01, 0.002 * eyeD) * (1.0 + 2.0 * (1.0 - NdotV));   // more offset at grazing angles (floors)
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

                float2 dims = _SSRHiZDims.xy;
                float2 pp0 = p0 * dims, dd = d * dims;
                float pixLen = length(dd);
                if (tMax * pixLen < 1.5) return MakeTraceOut(0, 0);

                // start one pixel out (plus jitter) so the ray cannot hit the pixel it leaves
                float jitter = Noise2D(pix, (int)_SSRFrame).x;   // frame = 0 when shimmer reduction is on
                float t0 = (1.0 + jitter) / pixLen;

                float tHit;
                if (!TraceHiZ(pp0, dd, k0, k1, t0, tMax, (int)_SSRMaxSteps, (int)_SSRHiZMaxMip, tHit)) return MakeTraceOut(0, 0);

                // sub-pixel refinement within the hit pixel
                float lo = max(tHit - 1.0 / pixLen, 0.0), hi = tHit;
                [loop] for (int r = 0; r < (int)_SSRRefineSteps; r++)
                {
                    float mid = 0.5 * (lo + hi);
                    float rayEyeM = rcp(lerp(k0, k1, mid));
                    float dm = rayEyeM - EyeDepthAt((pp0 + dd * mid) / dims);
                    if (dm > 0.0) hi = mid; else lo = mid;
                }
                float2 hitUV = (pp0 + dd * hi) / dims;

                // reject back-face hits
                float3 hitWS = PosAt(hitUV);
                float3 Nh = GetNormal(hitUV, hitWS);
                if (dot(Nh, R) > 0.0) return MakeTraceOut(0, 0);

                float distT = saturate(length(hitWS - posWS) / _SSRMaxDistance);
                float distFade = pow(1.0 - smoothstep(0.5, 1.0, distT), _SSRDistFadePower);   // full strength up to half range, then fades
                float2 e = min(hitUV, 1.0 - hitUV);
                float edge = saturate(min(e.x, e.y) / max(_SSREdgeFade, 1e-4));
                edge = edge * edge * (3.0 - 2.0 * edge);                       // smoothstep: no visible band at the border

                float conf = saturate(mask * fres * facing * distFade * edge);
                float3 col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, hitUV, 0).rgb;
                return MakeTraceOut(float4(col * conf, conf), max(length(hitWS - posWS), 1e-3));
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
                float raw = SampleSceneDepth(uv);
                float sm;
                float finalMask = DecodeFinalMask(uv, sm);
                if (finalMask <= 0.0 || IsSky(raw)) return 0;   // not reflective: skip the 9 taps and the history fetch

                // 3x3 of the RAW trace: centre = current sample (no pre-blur, so blur never accumulates in history); the rest bound the history
                float4 cur = 0;
                float4 nmin = 1e9, nmax = -1e9;
                float4 m1 = 0, m2 = 0;
                [unroll] for (int j = -1; j <= 1; j++)
                {
                    [unroll] for (int i = -1; i <= 1; i++)
                    {
                        float4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv + float2(i, j) * _SSRTexel.zw, 0);
                        if (i == 0 && j == 0) cur = c;
                        nmin = min(nmin, c); nmax = max(nmax, c);
                        m1 += c; m2 += c * c;
                    }
                }
                if (any(isnan(cur)) || any(isinf(cur))) cur = 0;

                // luminance cap (added luminance)
                float l = Luminance(cur.rgb);
                if (l > _SSRMaxLuma) cur.rgb *= _SSRMaxLuma / max(l, 1e-4);

                // variance clipping box (mean +- 1.25 sigma of the 3x3), intersected with the min/max box
                float4 mean = m1 / 9.0;
                float4 sigma = sqrt(max(m2 / 9.0 - mean * mean, 0.0));
                float4 vmin = max(mean - 1.25 * sigma, nmin);
                float4 vmax = min(mean + 1.25 * sigma, nmax);

                // reproject history: surface motion vector, blended toward the reflected-hit reprojection on smooth surfaces
            #if defined(_SSR_NO_MOTION)
                float2 prevUV = uv;
            #else
                float2 mv = SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_PointClamp, uv, 0).xy;
                float2 prevUV = uv - mv;   // [GAP] verify sign/units vs URP TAA on your version
            #endif
                float motionPx = length((prevUV - uv) * _ScaledScreenParams.xy);
                bool usedHit = false;
                float hitDist = _SSRHitReproj > 0.5 ? LOAD_TEXTURE2D_X_LOD(_SSRTraceHit, min((int2)(uv * _SSRTexel.xy), (int2)_SSRTexel.xy - 1), 0).r : 0.0;
                if (hitDist > 0.0)
                {
                    float3 posWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                    float2 hitUV = ReprojectByHit(posWS, hitDist);
                    if (all(hitUV >= 0.0) && all(hitUV <= 1.0))
                    {
                        prevUV = lerp(prevUV, hitUV, sm * sm);     // rough cones give a noisy hit distance, so trust it only when smooth
                        usedHit = true;
                    }
                }
                bool histOk = _SSRHistoryValid > 0.5 && all(prevUV >= 0.0) && all(prevUV <= 1.0);

                float4 hist = cur;                                   // default: no history => take current
                if (histOk) hist = SAMPLE_TEXTURE2D_X_LOD(_SSRHistory, sampler_LinearClamp, prevUV, 0);
                else if (_SSRSafeReset > 0.5) hist = 0;              // safe/reduced-motion: fade in from nothing
                if (any(isnan(hist)) || any(isinf(hist))) hist = 0;

                bool limiter = _SSRMaxDelta > 0.0;
                float4 histB = (histOk && !limiter) ? clamp(hist, vmin, vmax) : hist;   // ghost clamp only when limiter is off

                // adaptive feedback: rougher = noisier cone samples = more history; fast motion = less history (limiter modes keep the set value)
                // a hit-reprojected history stays valid under camera motion, so it loses less
                float rough = (1.0 - sm) * _SSRSmoothInfluence;
                float feedback = lerp(_SSRFeedback, min(_SSRFeedback + 0.1, 0.95), rough);
                if (!limiter) feedback *= lerp(1.0, usedHit ? 0.85 : 0.6, saturate(motionPx * 0.1));
                float4 outc = lerp(cur, histB, feedback);

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
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_local_fragment _ _SSR_USE_MASK
            #pragma multi_compile_local_fragment _ _SSR_MASK_NORMALS_A
            #pragma multi_compile_local_fragment _ _SSR_NO_NORMALS

            float4 LoadLow(int2 c)
            {
                return LOAD_TEXTURE2D_X_LOD(_BlitTexture, clamp(c, 0, (int2)_SSRTexel.xy - 1), 0);
            }

            // 4 nearest low-res texels weighted by bilinear x eye-depth similarity x normal similarity, so reflections do not bleed over depth edges.
            // If no texel matches the pixel's depth, the closest one in depth is used (no holes).
            float4 DepthAwareUpsample(float2 uv, float zc)
            {
                float2 low = _SSRTexel.xy;
                if (all(low >= _ScaledScreenParams.xy - 0.5))
                    return LoadLow((int2)(uv * low));

                float2 pos = uv * low - 0.5;
                int2 i0 = (int2)floor(pos);
                float2 f = pos - i0;
                float3 nc = GetNormal(uv, PosAt(uv));

                float4 acc = 0; float wsum = 0;
                float4 best = 0; float bestDz = 1e9;
                [unroll] for (int j = 0; j < 2; j++)
                {
                    [unroll] for (int i = 0; i < 2; i++)
                    {
                        int2 c = clamp(i0 + int2(i, j), 0, (int2)low - 1);
                        float2 tuv = (c + 0.5) / low;
                        float zt = EyeDepthAt(tuv);
                        float dz = abs(zt - zc);
                        float wb = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
                        float wd = saturate(1.0 - dz / (0.05 * zc + 0.01)) * pow(saturate(dot(nc, GetNormal(tuv, PosAt(tuv)))), _SSRNormalSharpness * 0.5);
                        float4 v = LoadLow(c);
                        acc += v * (wb * wd); wsum += wb * wd;
                        if (dz < bestDz) { bestDz = dz; best = v; }
                    }
                }
                return wsum > 1e-3 ? acc / wsum : best;
            }

            float4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = SampleSceneDepth(uv);
                if (IsSky(raw)) return 0;
                float sm;
                if (DecodeFinalMask(uv, sm) <= 0.0) return 0;           // full-res mask: no halo onto non-reflective pixels

                float4 r = DepthAwareUpsample(uv, LinearEyeDepth(raw, _ZBufferParams));
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

        // ------------------------------------------------------------------ Pass 4: Hi-Z base (mip 0 = linear eye depth)
        Pass
        {
            Name "SSRHiZBase"
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHiZBase

            float4 FragHiZBase(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return EyeDepthAt(input.texcoord);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ Pass 5: Hi-Z reduce (min eye depth of the 2x2, odd edges included)
        // _BlitTexture = the Hi-Z chain, _SSRHiZSrc = (source mip, source width, source height). Drawn into a temp target, then copied into the mip.
        Pass
        {
            Name "SSRHiZReduce"
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHiZReduce

            float4 FragHiZReduce(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                int srcMip = (int)_SSRHiZSrc.x;
                int2 sd = (int2)_SSRHiZSrc.yz;
                int2 dd = max(sd >> 1, 1);
                int2 dp = min((int2)(input.texcoord * dd), dd - 1);
                int2 sp = dp * 2;
                int2 ext = int2(1, 1) + int2((dp.x == dd.x - 1) && ((sd.x & 1) != 0), (dp.y == dd.y - 1) && ((sd.y & 1) != 0));

                float m = 1e20;
                [loop] for (int y = 0; y <= ext.y; y++)
                {
                    [loop] for (int x = 0; x <= ext.x; x++)
                        m = min(m, LOAD_TEXTURE2D_X_LOD(_BlitTexture, min(sp + int2(x, y), sd - 1), srcMip).r);
                }
                return m;
            }
            ENDHLSL
        }
        // ------------------------------------------------------------------ Pass 6: Denoise (edge-aware a-trous 3x3, one iteration per draw; stride = _SSRDenoiseStep)
        // Runs on the temporally accumulated result. Weights: floor-safe plane distance, normal similarity, relative luminance.
        // Stride grows with roughness; near-mirror pixels pass through untouched.
        Pass
        {
            Name "SSRDenoise"
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDenoise
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_local_fragment _ _SSR_USE_MASK
            #pragma multi_compile_local_fragment _ _SSR_MASK_NORMALS_A
            #pragma multi_compile_local_fragment _ _SSR_NO_NORMALS

            float4 FragDenoise(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = SampleSceneDepth(uv);
                float sm;
                if (IsSky(raw) || DecodeFinalMask(uv, sm) <= 0.0) return 0;

                int2 maxT = (int2)_SSRTexel.xy - 1;
                int2 tc = min((int2)(uv * _SSRTexel.xy), maxT);
                float4 cc = LOAD_TEXTURE2D_X_LOD(_BlitTexture, tc, 0);
                float rough = (1.0 - sm) * _SSRSmoothInfluence;
                if (rough <= 0.02) return cc;

                float3 pc = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 nc = GetNormal(uv, pc);
                float zc = LinearEyeDepth(raw, _ZBufferParams);
                float lc = Luminance(cc.rgb);
                int stride = max(1, (int)round(_SSRDenoiseStep * (0.5 + 2.0 * _SSRBlur * rough)));
                float lumaK = lerp(6.0, 1.5, saturate(rough * 3.0));     // smooth = keep reflection edges, rough = blur freely

                float4 acc = cc * 4.0; float wsum = 4.0;
                [unroll] for (int j = -1; j <= 1; j++)
                {
                    [unroll] for (int i = -1; i <= 1; i++)
                    {
                        if (i == 0 && j == 0) continue;
                        int2 tn = clamp(tc + int2(i, j) * stride, 0, maxT);
                        float2 nuv = (tn + 0.5) * _SSRTexel.zw;
                        float rawN = SampleSceneDepth(nuv);
                        float snm;
                        if (IsSky(rawN) || DecodeFinalMask(nuv, snm) <= 0.0) continue;

                        float3 pn = ComputeWorldSpacePosition(nuv, rawN, UNITY_MATRIX_I_VP);
                        float4 cn = LOAD_TEXTURE2D_X_LOD(_BlitTexture, tn, 0);
                        float ln = Luminance(cn.rgb);
                        float wd = saturate(1.0 - abs(dot(pn - pc, nc)) / (0.02 * zc + 0.01));      // distance to the centre's tangent plane
                        float wn = pow(saturate(dot(nc, GetNormal(nuv, pn))), _SSRNormalSharpness);
                        float wl = exp(-abs(ln - lc) / (ln + lc + 0.05) * lumaK);
                        float w = (i == 0 ? 2.0 : 1.0) * (j == 0 ? 2.0 : 1.0) * wd * wn * wl;
                        acc += cn * w; wsum += w;
                    }
                }
                float4 outc = acc / wsum;
                return (any(isnan(outc)) || any(isinf(outc))) ? cc : outc;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
