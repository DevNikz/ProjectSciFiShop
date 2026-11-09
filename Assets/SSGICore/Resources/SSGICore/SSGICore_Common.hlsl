#ifndef SSGICORE_COMMON_INCLUDED
#define SSGICORE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#define SSGI_TWO_PI 6.28318530718

SamplerState sampler_linear_clamp;

// ---- per-frame constants (set from C#: SSGIFrameConstants.Apply) --------------------------------------
float4x4 _SSGI_View;
float4x4 _SSGI_InvView;
float4x4 _SSGI_PrevView;
float4x4 _SSGI_Proj;
float4x4 _SSGI_InvProj;
float4x4 _SSGI_PrevViewProj;
float4   _SSGI_ZParams;
float4   _SSGI_FullSize;      // w, h, 1/w, 1/h
float4   _SSGI_LowSize;       // w, h, 1/w, 1/h
float4   _SSGI_AmbientCube[6];// +X -X +Y -Y +Z -Z
float4   _SSGI_RayA;          // view ray at ndc (0,0).xy, ray per unit ndc.x (xy)  -> see ViewPosFromPixel
float4   _SSGI_RayB;          // ray per unit ndc.y (xy)
float4   _SSGI_HzbRects[6];   // HZB atlas rect per level: x offset, y offset, width, height (level 0 = depth buffer)
float _SSGI_Near;
float _SSGI_Thickness;
float _SSGI_MaxDistance;
float _SSGI_EdgeFade;
float _SSGI_PrevWeight;
float _SSGI_FireflyClamp;
float _SSGI_MotionReject;
float _SSGI_FallbackIntensity;
float _SSGI_AlbedoLevel;
float _SSGI_TemporalClamp;
int _SSGI_Downsample;
int _SSGI_RayCount;
int _SSGI_MaxSteps;
int _SSGI_Bounces;
int _SSGI_FrameIndex;
int _SSGI_NormalMode;     // 0 raw, 1 octahedral, 2 reconstruct from depth
int _SSGI_UsePrevColor;
int _SSGI_HistoryValid;
int _SSGI_MaxAge;
int _SSGI_HasMotion;
int _SSGI_StepSize;
float4   _SSGI_CacheBase;     // xyz = world cell coordinate of grid cell (0,0,0), w = cell size (m)
float4   _SSGI_CacheDelta;    // xyz = this frame's base - last frame's base, w = 1 when last frame's cache is usable
int _SSGI_CacheEnabled;   // 1 = rays that leave the screen query the radiance cache
int _SSGI_ResValid;       // reservoir history usable this frame
int _SSGI_ResMaxM;        // history confidence cap, multiples of the current candidate count
int _SSGI_ResSpatial;     // 1 = spatial reservoir reuse
int _SSGI_Enhanced;       // 1 = enhanced denoiser paths (Volume: Denoiser Mode)

// ---- inputs ---------------------------------------------------------------------------------------------
Texture2D<float>  _SSGI_Depth;     // raw device depth
Texture2D<float4> _SSGI_Normals;     // raw DepthNormals texture (only read by the NormalsAndHzb1 kernel)
Texture2D<float4> _SSGI_NormalsRes;  // resolved normals, packed (see PackNormalVS)
Texture2D<float4> _SSGI_Motion;
Texture2D<float4> _SSGI_Color;
Texture2D<float4> _SSGI_PrevColor;
Texture2D<float>  _SSGI_HzbAtlas;  // min eye-depth pyramid, levels 1..5 packed into one texture (see _SSGI_HzbRects)
Texture2D<float4> _SSGI_LowGeo;    // trace-resolution geometry: view-space normal (xyz) + eye depth (w, <= 0 for sky)

// ---- helpers --------------------------------------------------------------------------------------------
float SSGILuma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

float3 ClampLuma(float3 c)
{
    float l = SSGILuma(c);
    return l > _SSGI_FireflyClamp ? c * (_SSGI_FireflyClamp / l) : c;
}

float EyeDepthFromRaw(float d)
{
    return 1.0 / (_SSGI_ZParams.z * d + _SSGI_ZParams.w);
}

bool IsSkyRaw(float d)
{
#if UNITY_REVERSED_Z
    return d <= 0.0;
#else
    return d >= 1.0;
#endif
}

float2 PixelToUV(int2 px) { return (px + 0.5) * _SSGI_FullSize.zw; }

int2 LowToFull(int2 lp)
{
    return min(lp * _SSGI_Downsample + _SSGI_Downsample / 2, (int2)_SSGI_FullSize.xy - 1);
}

// View space: camera looks down -Z. Returns the point on the pixel's view ray at the given eye depth.
// For a perspective projection the ray through the z = -1 plane is affine in ndc, so it is evaluated from
// three precomputed vectors instead of an inverse-projection multiply and a perspective divide.
float3 ViewPosFromPixel(int2 px, float eye)
{
    float2 uv = PixelToUV(px);
    float2 ndc = uv * 2.0 - 1.0;
#if UNITY_UV_STARTS_AT_TOP
    ndc.y = -ndc.y;
#endif
    float2 ray = _SSGI_RayA.xy + ndc.x * _SSGI_RayA.zw + ndc.y * _SSGI_RayB.xy;
    return float3(ray * eye, -eye);
}

float2 ClipToPixel(float4 clip)
{
    float2 ndc = clip.xy / clip.w;
    float2 uv = ndc * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
    uv.y = 1.0 - uv.y;
#endif
    return uv * _SSGI_FullSize.xy;
}

// ---- normals --------------------------------------------------------------------------------------------
float2 SSGIUnpack888ToFloat2(float3 x)
{
    uint3 i = (uint3)(x * 255.0 + 0.5);
    uint hi = i.z >> 4;
    uint lo = i.z & 15;
    uint2 cb = i.xy | uint2(lo << 8, hi << 8);
    return cb / 4095.0;
}

float3 SSGIDecodeOct(float2 f)
{
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = max(-n.z, 0.0);
    n.xy += n.xy >= 0.0 ? -t.xx : t.xx;
    return normalize(n);
}

// Resolved normals are stored packed: rgb = normal * 0.5 + 0.5, a = source id / 3 (0 depth-reconstructed, 1 raw, 2 octahedral).
float4 PackNormalVS(float3 n, int source)
{
    return float4(n * 0.5 + 0.5, source * (1.0 / 3.0));
}

float3 UnpackNormalVS(float4 packedN)
{
    return normalize(packedN.xyz * 2.0 - 1.0);
}

int UnpackNormalSource(float4 packedN)
{
    return (int)(packedN.w * 3.0 + 0.5);
}

// Resolved view-space normal written by the NormalsAndHzb1 kernel.
float3 LoadNormalVS(int2 px)
{
    return UnpackNormalVS(_SSGI_NormalsRes.Load(int3(px, 0)));
}

float3 ViewToWorldDir(float3 dirVS) { return mul(dirVS, (float3x3)_SSGI_View); }

// ---- reprojection ---------------------------------------------------------------------------------------
float2 AnalyticPrevUV(float3 posVS)
{
    float3 wp = mul(_SSGI_InvView, float4(posVS, 1.0)).xyz;
    float4 c = mul(_SSGI_PrevViewProj, float4(wp, 1.0));
    float2 ndc = c.xy / c.w;
    float2 uv = ndc * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
    uv.y = 1.0 - uv.y;
#endif
    return uv;
}

float2 PrevUV(int2 px, float3 posVS)
{
    if (_SSGI_HasMotion != 0)
        return PixelToUV(px) - _SSGI_Motion.Load(int3(px, 0)).xy;
    return AnalyticPrevUV(posVS);
}

// ---- noise & sampling -----------------------------------------------------------------------------------
float IGN(float2 p, int frame)
{
    p += 5.588238 * (float)(frame & 63);
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

float2 Sample2D(int2 px, int seq)
{
    float a = IGN((float2)px, _SSGI_FrameIndex);
    float b = IGN((float2)px + float2(47.0, 13.0), _SSGI_FrameIndex);
    return frac(float2(a, b) + (float)seq * float2(0.7548776662, 0.5698402910));
}

float3 CosineHemisphere(float2 xi, float3 n)
{
    float phi = SSGI_TWO_PI * xi.x;
    float sinT = sqrt(xi.y);
    float cosT = sqrt(1.0 - xi.y);
    float3 h = float3(cos(phi) * sinT, sin(phi) * sinT, cosT);
    float3 up = abs(n.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 t = normalize(cross(up, n));
    float3 b = cross(n, t);
    return normalize(t * h.x + b * h.y + n * h.z);
}

// ---- HZB (min eye-depth pyramid; level 0 = depth buffer, levels 1..5 live in one atlas) --------------------
float LoadHzb(int level, int2 p)
{
    if (level == 0)
    {
        p = clamp(p, int2(0, 0), (int2)_SSGI_FullSize.xy - 1);
        return EyeDepthFromRaw(_SSGI_Depth.Load(int3(p, 0)));
    }
    float4 rect = _SSGI_HzbRects[level];
    p = clamp(p, int2(0, 0), (int2)rect.zw - 1);
    return _SSGI_HzbAtlas.Load(int3((int2)rect.xy + p, 0));
}

// Geometry of a low-res pixel (written once per frame by the Trace kernel).
bool LoadLowGeo(int2 lp, out float eye, out float3 posVS, out float3 nVS)
{
    float4 g = _SSGI_LowGeo.Load(int3(lp, 0));
    eye = 1e6;
    posVS = 0;
    nVS = float3(0, 0, 1);
    if (g.w <= 0.0) return false;
    eye = g.w;
    posVS = ViewPosFromPixel(LowToFull(lp), eye);
    nVS = g.xyz;
    return true;
}

// x^8 and x^32 by repeated squaring (exact replacement for pow with those constant exponents).
float Pow8(float x)  { x *= x; x *= x; return x * x; }
float Pow32(float x) { x *= x; x *= x; x *= x; x *= x; return x * x; }

#endif
