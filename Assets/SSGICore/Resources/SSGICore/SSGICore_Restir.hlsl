#ifndef SSGICORE_RESTIR_INCLUDED
#define SSGICORE_RESTIR_INCLUDED

#include "SSGICore_Common.hlsl"

// ReSTIR-lite for the screen-space GI estimate (after ReSTIR GI, Ouyang et al. 2021).
//
// Quantity estimated per pixel: (1/pi) * integral of Lo * cos(theta) d(omega), i.e. what the plain trace averages.
// A reservoir holds ONE first-hit sample: world position xs, world normal ns at the hit, outgoing radiance Lo
// (already containing any further bounces and the fallback), plus the weight W and confidence M.
//
// Candidates are cosine sampled (pdf = cos/pi). Target pdf at receiver y for sample s:
//     phat_y(s) = luminance(Lo) * cos_y(s) / pi
// so the candidate weight phat/pdf = luminance(Lo), and the final estimate is  Lo * cos_y / pi * W,
// with W = wsum / (M * phat_y(selected)).
//
// Simplifications ("lite"): no visibility test on reused samples (neighbour geometry similarity only),
// no MIS (biased combine), temporal reuse treats the receiver as static (Jacobian = 1; moving receivers are
// rejected by the geometry check), spatial reuse uses one neighbour.

#define SSGI_INV_PI 0.31830988618

struct SSGIReservoir
{
    float3 xs;
    float  M;
    float3 Lo;
    float  W;
    float3 ns;
};

struct SSGIResAcc
{
    float3 xs;
    float3 Lo;
    float3 ns;
    float  wsum;
    float  M;
    float  pHatSel;
};

SSGIReservoir ReadReservoir(Texture2D<float4> a, Texture2D<float4> b, Texture2D<float4> c, int2 q)
{
    float4 va = a.Load(int3(q, 0));
    float4 vb = b.Load(int3(q, 0));
    float4 vc = c.Load(int3(q, 0));
    SSGIReservoir r;
    r.xs = va.xyz;
    r.M  = va.w;
    r.Lo = vb.xyz;
    r.W  = vb.w;
    r.ns = vc.xyz;
    if (!all(isfinite(float4(r.xs, r.W))) || !all(isfinite(r.Lo)))
    {
        r.M = 0;
        r.W = 0;
    }
    return r;
}

float ResRand(int2 px, int salt)
{
    return frac(IGN((float2)px + salt * 17.0, _SSGI_FrameIndex) + salt * 0.754877666);
}

// Cosine between the receiver normal and the direction from the receiver to the sample point (world space).
float ResCos(float3 xWS, float3 nWS, float3 xs)
{
    float3 d = xs - xWS;
    return saturate(dot(nWS, d * rsqrt(max(dot(d, d), 1e-8))));
}

float ResTargetPdf(float3 Lo, float cosTheta)
{
    return SSGILuma(Lo) * cosTheta * SSGI_INV_PI;
}

void ResAccUpdate(inout SSGIResAcc acc, float3 xs, float3 Lo, float3 ns, float pHat, float w, float m, float rnd)
{
    acc.M += m;
    acc.wsum += w;
    if (acc.wsum > 0.0 && rnd * acc.wsum < w)
    {
        acc.xs = xs;
        acc.Lo = Lo;
        acc.ns = ns;
        acc.pHatSel = pHat;
    }
}

float ResContributionWeight(SSGIResAcc acc)
{
    return (acc.pHatSel > 1e-8 && acc.M > 0.0) ? acc.wsum / (acc.M * acc.pHatSel) : 0.0;
}

#endif
