#ifndef GTAO_URP_INCLUDED
#define GTAO_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

#define GTAO_PI      3.14159265
#define GTAO_HALF_PI 1.57079633
#define GTAO_R1      0.6180339887

TEXTURE2D_X(_GTAO_History);
TEXTURE2D_X(_GTAO_MotionVectors);
// Linear eye-depth MIP chain, one RT per level (mip 0 = full res). Bound only when _GTAO_DEPTH_MIPS is on.
TEXTURE2D_X(_GTAO_DepthMip0);
TEXTURE2D_X(_GTAO_DepthMip1);
TEXTURE2D_X(_GTAO_DepthMip2);
TEXTURE2D_X(_GTAO_DepthMip3);
TEXTURE2D_X(_GTAO_DepthMip4);
// View-space bent normals (xyz, not normalised) at AO resolution. Bound only when _GTAO_BENT_ENABLED is on.
TEXTURE2D_X(_GTAO_BentIn);      // current frame (output of the previous stage)
TEXTURE2D_X(_GTAO_BentHistory); // previous frame, temporal pass only

float4 _GTAO_Params;    // x radius, y power, z falloffRange, w unused
float4 _GTAO_Params2;   // x slices, y steps, z maxPixelRadius, w multiBounceAlbedo
float4 _GTAO_Fade;      // x fadeStart, y fadeEnd, z downsample, w frame
float4 _GTAO_Proj;      // P00, P11, P02, P12
float4 _GTAO_ProjOrtho; // P03, P13
float4 _GTAO_Size;      // AO tex w,h,1/w,1/h
float4 _GTAO_FullSize;  // camera w,h,1/w,1/h
float4 _GTAO_Temporal;  // x historyWeight (0=off), y velocityFadeInvPx, z depthTolerance, w clampK
float4 _GTAO_Blur;      // x radius, y depthSharpness
float4 _GTAO_MipParams; // x depthMipBias, y highest mip index
float4 _GTAO_Denoise;   // x planeTolerance, y temporal v2 (4-tap history + age) when > 0.5, z bilateral 2x2 upsample when > 0.5
float _GTAO_AtrousStep; // pixel step of the current a-trous iteration (set per pass)
float4 _GTAO_Tune;      // x sampleDistributionPower, y thinOccluderCompensation, z noise (0 interleaved, 1 hilbert)
float4x4 _GTAO_ViewToWorld;
float4x4 _GTAO_WorldToView;
float4x4 _GTAO_PrevViewProj;
float4x4 _GTAO_PrevView;

float RawToLinear(float raw)
{
#if defined(_ORTHOGRAPHIC)
    return LinearDepthToEyeDepth(raw);
#else
    return LinearEyeDepth(raw, _ZBufferParams);
#endif
}

bool IsFar(float raw)
{
#if UNITY_REVERSED_Z
    return raw <= 1e-7;
#else
    return raw >= 1.0 - 1e-7;
#endif
}

float LinDepth(float2 uv) { return RawToLinear(SampleSceneDepth(uv)); }

// Linear eye depth. With _GTAO_DEPTH_MIPS this reads the prefiltered chain; mip is rounded to the nearest level
// (point sampled, like XeGTAO). Without it, mip is ignored and full-res camera depth is used.
float SampleViewDepth(float2 uv, float mip)
{
#if defined(_GTAO_DEPTH_MIPS)
    [branch] if (mip < 0.5) return SAMPLE_TEXTURE2D_X_LOD(_GTAO_DepthMip0, sampler_PointClamp, uv, 0).x;
    [branch] if (mip < 1.5) return SAMPLE_TEXTURE2D_X_LOD(_GTAO_DepthMip1, sampler_PointClamp, uv, 0).x;
    [branch] if (mip < 2.5) return SAMPLE_TEXTURE2D_X_LOD(_GTAO_DepthMip2, sampler_PointClamp, uv, 0).x;
    [branch] if (mip < 3.5) return SAMPLE_TEXTURE2D_X_LOD(_GTAO_DepthMip3, sampler_PointClamp, uv, 0).x;
    return SAMPLE_TEXTURE2D_X_LOD(_GTAO_DepthMip4, sampler_PointClamp, uv, 0).x;
#else
    return LinDepth(uv);
#endif
}

float3 ViewPos(float2 uv, float d)
{
    float2 ndc = uv * 2.0 - 1.0;
#if defined(_ORTHOGRAPHIC)
    return float3((ndc.x - _GTAO_ProjOrtho.x) / _GTAO_Proj.x, (ndc.y - _GTAO_ProjOrtho.y) / _GTAO_Proj.y, -d);
#else
    return float3((ndc.x + _GTAO_Proj.z) * d / _GTAO_Proj.x, (ndc.y + _GTAO_Proj.w) * d / _GTAO_Proj.y, -d);
#endif
}

float3 SamplePos(float2 uv, float mip) { return ViewPos(uv, SampleViewDepth(uv, mip)); }

float GTAO_FastACos(float x)
{
    x = clamp(x, -1.0, 1.0);
    float ax = abs(x);
    float r = (-0.156583 * ax + GTAO_HALF_PI) * sqrt(1.0 - ax);
    return x >= 0.0 ? r : GTAO_PI - r;
}

// Jimenez 2014 interleaved gradient noise, animated across 64 frames.
float IGN(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }

// Index along a Hilbert curve over a 64x64 tile (XeGTAO), computed with bit ops instead of a LUT texture.
uint HilbertIndex(uint x, uint y)
{
    uint index = 0u;
    [unroll] for (int l = 5; l >= 0; l--)
    {
        uint level = 1u << (uint)l;
        uint rx = (x & level) > 0u ? 1u : 0u;
        uint ry = (y & level) > 0u ? 1u : 0u;
        index += level * level * ((3u * rx) ^ ry);
        if (ry == 0u)
        {
            if (rx == 1u) { x = 63u - x; y = 63u - y; }
            uint tmp = x; x = y; y = tmp;
        }
    }
    return index;
}

float2 Noise(int2 p, uint f)
{
    [branch] if (_GTAO_Tune.z > 0.5) // Hilbert + R2 spatio-temporal noise (XeGTAO)
    {
        uint idx = HilbertIndex((uint)p.x & 63u, (uint)p.y & 63u) + 288u * (f & 63u);
        return frac(0.5 + (float)idx * float2(0.75487766624669276, 0.56984029099805327));
    }
    float fr = (float)(f & 63u);
    float n1 = IGN(float2(p) + 5.588238 * fr);
    float n2 = frac(IGN(float2(p.yx) + 17.0) + fr * GTAO_R1);
    return float2(n1, n2);
}

float2 SnapUV(float2 o) { return round(o * _GTAO_FullSize.xy) * _GTAO_FullSize.zw; }

// Normal from depth: closer-neighbour method (atyuwen / Wicked Engine).
float3 ReconstructNormal(float2 uv, float3 P, float d)
{
    float2 t = _GTAO_FullSize.zw;
    float dl = SampleViewDepth(uv - float2(t.x, 0), 0.0), dr = SampleViewDepth(uv + float2(t.x, 0), 0.0);
    float dd = SampleViewDepth(uv - float2(0, t.y), 0.0), du = SampleViewDepth(uv + float2(0, t.y), 0.0);
    float3 dx = abs(dl - d) < abs(dr - d) ? P - ViewPos(uv - float2(t.x, 0), dl) : ViewPos(uv + float2(t.x, 0), dr) - P;
    float3 dy = abs(dd - d) < abs(du - d) ? P - ViewPos(uv - float2(0, t.y), dd) : ViewPos(uv + float2(0, t.y), du) - P;
    float3 n = cross(dx, dy);
    n *= rsqrt(max(dot(n, n), 1e-12));
    return dot(n, -P) < 0.0 ? -n : n;
}

float3 GetNormal(float2 uv, float3 P, float d)
{
#if defined(_SOURCE_DEPTH_NORMALS)
    float3 nw = SampleSceneNormals(uv);
    float l = dot(nw, nw);
    if (l < 0.25) return ReconstructNormal(uv, P, d); // empty normals texture -> fallback
    return mul((float3x3)_GTAO_WorldToView, nw * rsqrt(l));
#else
    return ReconstructNormal(uv, P, d);
#endif
}

void ScreenRadius(float3 P, out float2 uvR, out float pxR)
{
    float R = _GTAO_Params.x;
#if defined(_ORTHOGRAPHIC)
    uvR = 0.5 * R * _GTAO_Proj.xy;
#else
    uvR = 0.5 * R * _GTAO_Proj.xy / max(-P.z, 1e-3);
#endif
    float2 px = uvR * _GTAO_FullSize.xy;
    pxR = max(px.x, px.y);
    float mx = _GTAO_Params2.z;
    if (pxR > mx) { uvR *= mx / pxR; pxR = mx; }
}

// GTAO (Jimenez et al. 2016), following the public XeGTAO reference implementation.
float GTAO(float2 uv, float3 P, float3 N, float2 nz, out float3 bent)
{
    bent = float3(0.0, 0.0, 0.0); // stays zero if the pixel is skipped; the caller falls back to the normal
    float3 V = normalize(-P);
    float2 uvR; float pxR;
    ScreenRadius(P, uvR, pxR);
    if (pxR < 1.3) return 1.0;

    int slices = max((int)_GTAO_Params2.x, 1);
    int steps  = max((int)_GTAO_Params2.y, 1);
    float invSlices = 1.0 / slices, invSteps = 1.0 / steps;
    float R = _GTAO_Params.x;
    float fRange = max(_GTAO_Params.z, 0.01) * R;
    float fMul = -1.0 / fRange;
    float fAdd = (R * (1.0 - _GTAO_Params.z)) / fRange + 1.0;
    float minS = 1.3 / pxR;
    float distPow = _GTAO_Tune.x;
    bool distQuadratic = abs(distPow - 2.0) < 1e-4; // fast path; uniform across the draw
    float thin = _GTAO_Tune.y;
    float vis = 0.0;

    [loop] for (int sl = 0; sl < slices; sl++)
    {
        float phi = (sl + nz.x) * invSlices * GTAO_PI;
        float sp, cp; sincos(phi, sp, cp);
        float2 omega = float2(cp, sp) * uvR;
        float3 dir3 = float3(cp, sp, 0);
        float3 ortho = dir3 - dot(dir3, V) * V;
        float3 ax = cross(ortho, V);
        float3 axis = ax * rsqrt(max(dot(ax, ax), 1e-10));
        float3 pn = N - axis * dot(N, axis);
        float pl = length(pn);
        float sgn = sign(dot(ortho, pn));
        float cosN = saturate(dot(pn, V) / max(pl, 1e-4));
        float n = sgn * GTAO_FastACos(cosN);
        float sinN = sin(n);
        float low0 = -sinN, low1 = sinN; // == cos(n +- pi/2)
        float hc0 = low0, hc1 = low1;

        [loop] for (int st = 0; st < steps; st++)
        {
            float sn = frac(nz.y + (float)(sl + st * slices) * GTAO_R1);
            float s = (st + sn) * invSteps;
            [branch] if (distQuadratic) s = s * s; // denser near the pixel
            else s = pow(s, distPow);
            s += minS;
            float2 off = SnapUV(s * omega);
            float mip = clamp(log2(max(s * pxR, 1.0)) - _GTAO_MipParams.x, 0.0, _GTAO_MipParams.y); // unused without _GTAO_DEPTH_MIPS
            float3 D0 = SamplePos(uv + off, mip) - P;
            float3 D1 = SamplePos(uv - off, mip) - P;
            float dd0 = dot(D0, D0), dd1 = dot(D1, D1);
            float il0 = rsqrt(max(dd0, 1e-10)), il1 = rsqrt(max(dd1, 1e-10));
            float l0 = dd0 * il0, l1 = dd1 * il1;
            float w0 = saturate(l0 * fMul + fAdd), w1 = saturate(l1 * fMul + fAdd);
            float c0 = lerp(low0, dot(D0, V) * il0, w0);
            float c1 = lerp(low1, dot(D1, V) * il1, w1);
            // thin occluder compensation (XeGTAO): thin == 0 reduces to hc = max(hc, c)
            hc0 = c0 > hc0 ? c0 : lerp(hc0, c0, thin);
            hc1 = c1 > hc1 ? c1 : lerp(hc1, c1, thin);
        }

        pl = lerp(pl, 1.0, 0.05);
        float h0 = -GTAO_FastACos(hc1), h1 = GTAO_FastACos(hc0);
        h0 = n + clamp(h0 - n, -GTAO_HALF_PI, GTAO_HALF_PI);
        h1 = n + clamp(h1 - n, -GTAO_HALF_PI, GTAO_HALF_PI);
        float ia0 = (cosN + 2.0 * h0 * sinN - cos(2.0 * h0 - n)) * 0.25;
        float ia1 = (cosN + 2.0 * h1 * sinN - cos(2.0 * h1 - n)) * 0.25;
        vis += pl * (ia0 + ia1);

#if defined(_GTAO_BENT_ENABLED)
        {
            // Bent normal of this slice (Jimenez 2016 "Algorithm 2", as in XeGTAO), expressed in the slice plane
            // spanned by the view vector V and the tangent on the +omega side (the same plane n is measured in).
            // XeGTAO rotates (dir.xy, 0) into view alignment instead, which only approximates this tangent.
            float3 tang = ortho * rsqrt(max(dot(ortho, ortho), 1e-10));
            float t0 = (6.0 * sin(h0 - n) - sin(3.0 * h0 - n) + 6.0 * sin(h1 - n) - sin(3.0 * h1 - n)
                        + 16.0 * sinN - 3.0 * (sin(h0 + n) + sin(h1 + n))) * (1.0 / 12.0);
            float t1 = (-cos(3.0 * h0 - n) - cos(3.0 * h1 - n) + 8.0 * cosN
                        - 3.0 * (cos(h0 + n) + cos(h1 + n))) * (1.0 / 12.0);
            bent += pl * (t0 * tang + t1 * V);
        }
#endif
    }
    return saturate(vis * invSlices);
}

// Ground-Truth Ambient Occlusion multi-bounce approximation (Jimenez 2016 GDC talk).
float MultiBounce(float x, float albedo)
{
    float a = 2.0404 * albedo - 0.3324;
    float b = -4.7951 * albedo + 0.6417;
    float c = 2.7552 * albedo + 0.6903;
    return max(x, ((x * a + b) * x + c) * x);
}

// Pass 0: estimate AO + store linear eye depth for later reprojection/blur. bentView is the view-space bent
// normal (unit length, or zero when the pixel is skipped); it is only written out when _GTAO_BENT_ENABLED is on.
float2 EstimateCore(Varyings i, out float3 bentView)
{
    bentView = float3(0.0, 0.0, 0.0);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float2 uv = i.texcoord;
    int2 ap = (int2)floor(uv * _GTAO_Size.xy);
    uint f = (uint)_GTAO_Fade.w;
    int down = max((int)_GTAO_Fade.z, 1);
    int2 jit = down > 1 ? int2(f & 1u, (f >> 1) & 1u) : int2(0, 0); // temporal sub-pixel jitter when downsampled
    float2 uvC = ((float2)(ap * down + jit) + 0.5) * _GTAO_FullSize.zw;

    float raw = SampleSceneDepth(uvC);
    float d = SampleViewDepth(uvC, 0.0);
    if (IsFar(raw)) return float2(1.0, d);
    if (_GTAO_Fade.y > 0.0 && d >= _GTAO_Fade.y) return float2(1.0, d); // beyond fade distance: skip

    float3 P = ViewPos(uvC, d);
    float3 N = GetNormal(uvC, P, d);
    float2 nz = Noise(ap, f);

    float3 bent;
    float ao = GTAO(uvC, P, N, nz, bent);
    float bl = dot(bent, bent);
    bent = bl > 1e-6 ? bent * rsqrt(bl) : N; // degenerate (e.g. pixel too small on screen): geometric normal
    bentView = bent;
    ao = pow(saturate(ao), _GTAO_Params.y);
    if (_GTAO_Params2.w > 0.0) ao = MultiBounce(ao, saturate(_GTAO_Params2.w));
    if (_GTAO_Fade.y > 0.0)
    {
        float fade = saturate((_GTAO_Fade.y - d) / max(_GTAO_Fade.y - _GTAO_Fade.x, 1e-3));
        ao = lerp(1.0, ao, fade);
        bentView = lerp(N, bent, fade);
    }
    return float2(ao, d);
}

#if defined(_GTAO_BENT_ENABLED)
struct EstimateOut { float2 ao : SV_Target0; float4 bent : SV_Target1; };
EstimateOut FragEstimate(Varyings i)
{
    EstimateOut o;
    float3 b;
    o.ao = EstimateCore(i, b);
    o.bent = float4(b, 0.0);
    return o;
}
#else
float2 FragEstimate(Varyings i) : SV_Target { float3 b; return EstimateCore(i, b); }
#endif

// Pass 1: temporal resolve. Variance-clamped 3x3 neighbourhood, reprojected by motion vectors when available,
// else by camera matrices. Legacy: one bilinear history tap with a fixed weight. v2 (Atrous denoiser): four
// depth-validated history taps (bilinear weights) plus a per-pixel history age in .z, so history rebuilds quickly
// after disocclusion instead of blending against a stale value.
float4 TemporalCore(Varyings i, out float3 bentOut)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float2 uv = i.texcoord;
    float2 cur = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).xy;
    float3 curB = float3(0.0, 0.0, 0.0);
#if defined(_GTAO_BENT_ENABLED)
    curB = SAMPLE_TEXTURE2D_X_LOD(_GTAO_BentIn, sampler_PointClamp, uv, 0).xyz;
#endif
    bentOut = curB;
    if (_GTAO_Temporal.x <= 0.0) return float4(cur, 1.0, 0.0); // no history yet: never sample uninitialised texture

    float2 t = _GTAO_Size.zw;
    float m1 = 0, m2 = 0, mn = cur.x, mx = cur.x;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float a = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv + float2(x, y) * t, 0).x;
        m1 += a; m2 += a * a; mn = min(mn, a); mx = max(mx, a);
    }
    float mean = m1 / 9.0;
    float sigma = sqrt(max(m2 / 9.0 - mean * mean, 0.0));
    float lo = max(mn, mean - _GTAO_Temporal.w * sigma);
    float hi = min(mx, mean + _GTAO_Temporal.w * sigma);

    float3 P = ViewPos(uv, cur.y);
    float3 Wp = mul(_GTAO_ViewToWorld, float4(P, 1.0)).xyz;
    float4 pc = mul(_GTAO_PrevViewProj, float4(Wp, 1.0));
    float2 prevUV = pc.xy / max(abs(pc.w), 1e-5) * 0.5 + 0.5;
    float expected = -mul(_GTAO_PrevView, float4(Wp, 1.0)).z;
#if defined(_MOTION_VECTORS)
    prevUV = uv - SAMPLE_TEXTURE2D_X_LOD(_GTAO_MotionVectors, sampler_PointClamp, uv, 0).xy; // URP convention: prev = uv - mv
#endif

    bool ok = all(prevUV > 0.0) && all(prevUV < 1.0);
    float velPx = length((uv - prevUV) * _GTAO_FullSize.xy);
    float velFade = saturate(1.0 - velPx * _GTAO_Temporal.y); // fade history under motion

    [branch] if (_GTAO_Denoise.y > 0.5)
    {
        float2 pt = prevUV * _GTAO_Size.xy - 0.5;
        float2 fl = floor(pt);
        float2 fr = pt - fl;
        float aoSum = 0.0, ageSum = 0.0, wSum = 0.0;
        float3 bentSum = float3(0.0, 0.0, 0.0);
        [unroll] for (int k = 0; k < 4; k++)
        {
            float2 o = float2(k & 1, k >> 1);
            float bw = (o.x > 0.5 ? fr.x : 1.0 - fr.x) * (o.y > 0.5 ? fr.y : 1.0 - fr.y);
            float2 tuv = (fl + o + 0.5) * _GTAO_Size.zw;
            float4 hk = SAMPLE_TEXTURE2D_X_LOD(_GTAO_History, sampler_PointClamp, tuv, 0);
            bool finiteTap = hk.x == hk.x && hk.y == hk.y && hk.z == hk.z; // NaN guard
            float dv = saturate(1.0 - abs(hk.y - expected) / max(expected * _GTAO_Temporal.z, 1e-4)); // disocclusion rejection
            float wk = finiteTap ? bw * dv : 0.0;
            aoSum += hk.x * wk; ageSum += hk.z * wk; wSum += wk;
#if defined(_GTAO_BENT_ENABLED)
            bentSum += SAMPLE_TEXTURE2D_X_LOD(_GTAO_BentHistory, sampler_PointClamp, tuv, 0).xyz * wk; // same weights as AO
#endif
        }
        float invW = wSum > 1e-5 ? 1.0 / wSum : 0.0;
        float valid = ok ? saturate(wSum) * velFade : 0.0;
        float effAge = ageSum * invW * valid;           // confidence-weighted history age
        float wv = min(effAge / (effAge + 1.0), _GTAO_Temporal.x); // 0 on the first frame, rises towards temporalWeight
        float aoOut = lerp(cur.x, clamp(aoSum * invW, lo, hi), wv);
#if defined(_GTAO_BENT_ENABLED)
        bentOut = lerp(curB, bentSum * invW, wv); // not clamped; a bent normal has no neighbourhood-variance equivalent here
#endif
        return float4(aoOut, cur.y, min(effAge + 1.0, 128.0), 0.0);
    }

    float2 h = SAMPLE_TEXTURE2D_X_LOD(_GTAO_History, sampler_LinearClamp, prevUV, 0).xy;
    if (h.x != h.x || h.y != h.y) ok = false; // NaN guard

    float depthErr = abs(h.y - expected) / max(expected, 1e-3);
    float conf = saturate(1.0 - depthErr / max(_GTAO_Temporal.z, 1e-4)); // disocclusion rejection
    float w = ok ? _GTAO_Temporal.x * conf * velFade : 0.0;
    return float4(lerp(cur.x, clamp(h.x, lo, hi), w), cur.y, 0.0, 0.0);
}

#if defined(_GTAO_BENT_ENABLED)
struct TemporalOut { float4 ao : SV_Target0; float4 bent : SV_Target1; };
TemporalOut FragTemporal(Varyings i)
{
    TemporalOut o;
    float3 b;
    o.ao = TemporalCore(i, b);
    o.bent = float4(b, 0.0);
    return o;
}
#else
float4 FragTemporal(Varyings i) : SV_Target { float3 b; return TemporalCore(i, b); }
#endif

// Weight of a neighbour by its distance from the centre pixel's tangent plane: 1 on the plane, 0 beyond 1/invTol.
float DepthPlaneWeight(float3 N, float3 P, float3 Pq, float invTol)
{
    return saturate(1.0 - abs(dot(N, Pq - P)) * invTol);
}

// Pass 8: one a-trous iteration. 3x3 binomial kernel dilated by _GTAO_AtrousStep, each tap weighted by plane distance.
// The bent normal (when enabled) is filtered with the same weights; it is renormalised only at the final resolve.
float2 AtrousCore(Varyings i, out float3 bentOut)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float2 uv = i.texcoord;
    float2 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).xy;
    float3 cb = float3(0.0, 0.0, 0.0);
#if defined(_GTAO_BENT_ENABLED)
    cb = SAMPLE_TEXTURE2D_X_LOD(_GTAO_BentIn, sampler_PointClamp, uv, 0).xyz;
#endif
    bentOut = cb;
    float dc = c.y;
    if (dc >= _ProjectionParams.z * 0.99) return c; // sky / beyond far plane: AO is 1, nothing to filter

    float3 P = ViewPos(uv, dc);
    float3 N = GetNormal(uv, P, dc);
    float invTol = 1.0 / max(_GTAO_Denoise.x * dc, 1e-4);
    float2 stp = _GTAO_Size.zw * _GTAO_AtrousStep;

    float sum = 0.0, wsum = 0.0;
    float3 bsum = float3(0.0, 0.0, 0.0);
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 suv = uv + float2(x, y) * stp;
        float2 s = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, suv, 0).xy;
        float w = (float)((2 - abs(x)) * (2 - abs(y))) * DepthPlaneWeight(N, P, ViewPos(suv, s.y), invTol); // [1 2 1] x [1 2 1]
        sum += s.x * w; wsum += w;
#if defined(_GTAO_BENT_ENABLED)
        bsum += SAMPLE_TEXTURE2D_X_LOD(_GTAO_BentIn, sampler_PointClamp, suv, 0).xyz * w;
#endif
    }
#if defined(_GTAO_BENT_ENABLED)
    bentOut = bsum / wsum;
#endif
    return float2(sum / wsum, c.y); // centre tap has weight 4, so wsum >= 4
}

#if defined(_GTAO_BENT_ENABLED)
struct AtrousOut { float2 ao : SV_Target0; float4 bent : SV_Target1; };
AtrousOut FragAtrous(Varyings i)
{
    AtrousOut o;
    float3 b;
    o.ao = AtrousCore(i, b);
    o.bent = float4(b, 0.0);
    return o;
}
#else
float2 FragAtrous(Varyings i) : SV_Target { float3 b; return AtrousCore(i, b); }
#endif

// Depth-aware separable blur, shared by the H and V+upsample passes.
float2 DoBlur(float2 uv, float2 dir, bool fullResDepth)
{
    int R = (int)_GTAO_Blur.x;
    float2 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).xy;
    float dc = fullResDepth ? SampleViewDepth(uv, 0.0) : c.y;
    float sg = max(R * 0.5, 0.5);
    float sum = 0.0, wsum = 0.0;
    [loop] for (int k = -R; k <= R; k++)
    {
        float2 s = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv + dir * (float)k * _GTAO_Size.zw, 0).xy;
        float w = exp(-(k * k) / (2.0 * sg * sg)) * exp(-abs(s.y - dc) / max(dc, 1e-3) * _GTAO_Blur.y);
        sum += s.x * w; wsum += w;
    }
    return float2(wsum > 1e-5 ? sum / wsum : c.x, c.y);
}

float2 FragBlurH(Varyings i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    return DoBlur(i.texcoord, float2(1, 0), false);
}

// Depth-aware 2x2 upsample from AO resolution to full resolution: the four nearest AO texels, each weighted by
// bilinear coverage x similarity of its stored depth to the full-res pixel's depth. If every tap is rejected,
// the tap closest in depth wins instead of a blind bilinear blend. bentOut gets the same filtering (zero unless
// _GTAO_BENT_ENABLED is on).
float UpsampleAO(float2 uv, out float3 bentOut)
{
    float dF = SampleViewDepth(uv, 0.0);
    float2 pt = uv * _GTAO_Size.xy - 0.5;
    float2 fl = floor(pt);
    float2 fr = pt - fl;
    float sum = 0.0, wsum = 0.0, bestAO = 1.0, bestErr = 1e30;
    float3 bsum = float3(0.0, 0.0, 0.0), bestB = float3(0.0, 0.0, 0.0);
    [unroll] for (int k = 0; k < 4; k++)
    {
        float2 o = float2(k & 1, k >> 1);
        float bw = (o.x > 0.5 ? fr.x : 1.0 - fr.x) * (o.y > 0.5 ? fr.y : 1.0 - fr.y);
        float2 tuv = (fl + o + 0.5) * _GTAO_Size.zw;
        float2 s = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, tuv, 0).xy;
        float3 sb = float3(0.0, 0.0, 0.0);
#if defined(_GTAO_BENT_ENABLED)
        sb = SAMPLE_TEXTURE2D_X_LOD(_GTAO_BentIn, sampler_PointClamp, tuv, 0).xyz;
#endif
        float err = abs(s.y - dF) / max(dF, 1e-3);
        float w = bw * exp(-err * _GTAO_Blur.y);
        sum += s.x * w; bsum += sb * w; wsum += w;
        if (err < bestErr) { bestErr = err; bestAO = s.x; bestB = sb; }
    }
    bool ok = wsum > 1e-4;
    bentOut = ok ? bsum / max(wsum, 1e-4) : bestB;
    return ok ? sum / wsum : bestAO;
}

float ResolveAO(Varyings i)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float3 unusedBent;
    [branch] if (_GTAO_Denoise.z > 0.5) return UpsampleAO(i.texcoord, unusedBent); // Atrous + downsample
    return DoBlur(i.texcoord, float2(0, 1), true).x; // legacy: vertical blur doubles as bilateral upsample
}

float4 FragResolve(Varyings i) : SV_Target   { float a = ResolveAO(i); return float4(a, a, a, a); }
float4 FragComposite(Varyings i) : SV_Target { return float4(0, 0, 0, ResolveAO(i)); } // Blend Zero SrcAlpha
float4 FragDebug(Varyings i) : SV_Target     { float a = ResolveAO(i); return float4(a, a, a, 1); }

// Pass 6: raw camera depth -> linear eye depth (mip 0 of the depth chain).
float FragPrefilterDepth(Varyings i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float raw = LOAD_TEXTURE2D_X(_CameraDepthTexture, uint2(i.positionCS.xy)).x;
    return min(RawToLinear(raw), 60000.0); // keep finite for 16-bit float targets
}

// Pass 7: next depth MIP. 2x2 average weighted toward the farthest texel (XeGTAO), using the same
// radius/falloff as the AO so occluders closer than the falloff range don't get averaged into the background.
float FragDepthMip(Varyings i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    int2 p = int2(i.positionCS.xy) * 2;
    float d0 = LOAD_TEXTURE2D_X(_BlitTexture, p).x;
    float d1 = LOAD_TEXTURE2D_X(_BlitTexture, p + int2(1, 0)).x;
    float d2 = LOAD_TEXTURE2D_X(_BlitTexture, p + int2(0, 1)).x;
    float d3 = LOAD_TEXTURE2D_X(_BlitTexture, p + int2(1, 1)).x;

    float R = _GTAO_Params.x;
    float fRange = max(_GTAO_Params.z, 0.01) * R;
    float fMul = -1.0 / fRange;
    float fAdd = (R * (1.0 - _GTAO_Params.z)) / fRange + 1.0;
    float dMax = max(max(d0, d1), max(d2, d3));
    float w0 = saturate((dMax - d0) * fMul + fAdd);
    float w1 = saturate((dMax - d1) * fMul + fAdd);
    float w2 = saturate((dMax - d2) * fMul + fAdd);
    float w3 = saturate((dMax - d3) * fMul + fAdd);
    return (w0 * d0 + w1 * d1 + w2 * d2 + w3 * d3) / max(w0 + w1 + w2 + w3, 1e-5);
}

// Pass 9: full-resolution world-space bent normal (rgb = n * 0.5 + 0.5, a = 1 if valid, 0 if the pixel has no
// usable bent normal and the consumer should use its geometric normal). Upsampled like the AO when downsampled.
float4 FragResolveBent(Varyings i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float2 uv = i.texcoord;
    float3 bent = float3(0.0, 0.0, 0.0);
    [branch] if (_GTAO_Denoise.z > 0.5)
        UpsampleAO(uv, bent);
    else
    {
#if defined(_GTAO_BENT_ENABLED)
        bent = SAMPLE_TEXTURE2D_X_LOD(_GTAO_BentIn, sampler_PointClamp, uv, 0).xyz;
#endif
    }
    float l2 = dot(bent, bent);
    if (l2 < 1e-6) return float4(0.5, 0.5, 0.5, 0.0);
    float3 bw = normalize(mul((float3x3)_GTAO_ViewToWorld, bent * rsqrt(l2)));
    return float4(bw * 0.5 + 0.5, 1.0);
}

#endif
