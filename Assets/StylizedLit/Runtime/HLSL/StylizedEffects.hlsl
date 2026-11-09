// StylizedEffects.hlsl  (Phase 6)
// Additive stylized color effects used by the ForwardLit pass only (rim light, matcap, triplanar albedo, hatching/halftone).
// Include after StylizedLitInput.hlsl. Both helpers are compiled out unless their keyword is set.
#ifndef STYLIZED_EFFECTS_INCLUDED
#define STYLIZED_EFFECTS_INCLUDED

#if defined(_RIM_ON)
// Fresnel rim with a hard-to-soft edge. Optionally masked toward the side facing the main light and by main light shadows.
half3 ComputeRim(InputData inputData, Light mainLight)
{
    half fresnel = 1.0h - saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half halfSoft = max(_RimSoftness, 1e-4h) * 0.5h;
    half rim = smoothstep(_RimThreshold - halfSoft, _RimThreshold + halfSoft, fresnel);

    half facingLight = saturate(dot(inputData.normalWS, mainLight.direction));
    rim *= lerp(1.0h, facingLight, _RimLightMask);

    if (_RimShadowAware > 0.5h)
        rim *= mainLight.shadowAttenuation;

    return _RimColor.rgb * rim;
}
#endif

#if defined(_MATCAP_ON)
// Matcap lookup from the view-space normal. _MatcapBlend: 0 = add, 1 = multiply, 2 = replace.
half3 ApplyMatcap(half3 color, InputData inputData)
{
    half3 normalVS = TransformWorldToViewDir(inputData.normalWS, true);
    float2 uv = normalVS.xy * 0.5 + 0.5;
    half3 matcap = SAMPLE_TEXTURE2D_LOD(_MatcapMap, sampler_MatcapMap, uv, 0).rgb * _MatcapTint.rgb;

    if (_MatcapBlend < 0.5h)
        return color + matcap * _MatcapStrength;
    if (_MatcapBlend < 1.5h)
        return color * lerp(half3(1, 1, 1), matcap, _MatcapStrength);
    return lerp(color, matcap, _MatcapStrength);
}
#endif

#if defined(_TRIPLANAR_ON)
// Replaces the UV-mapped base map color with three axis-aligned projections blended by the surface normal.
// _TriplanarSpace: 0 = world, 1 = object. Albedo only (no triplanar normal/occlusion maps; alpha still comes from the UVs).
half3 SampleTriplanarAlbedo(float3 positionWS, float3 normalWS)
{
    float3 p = positionWS;
    float3 n = normalWS;
    if (_TriplanarSpace > 0.5h)
    {
        p = TransformWorldToObject(positionWS);
        n = TransformWorldToObjectNormal(normalWS);
    }

    half3 w = pow(abs((half3)normalize(n)), max(_TriplanarSharpness, 1.0h));
    w /= (w.x + w.y + w.z + 1e-5h);

    float3 q = p * _TriplanarScale;
    half3 cx = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, ComputeStylizedUV(q.zy)).rgb;
    half3 cy = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, ComputeStylizedUV(q.xz)).rgb;
    half3 cz = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, ComputeStylizedUV(q.xy)).rgb;
    return cx * w.x + cy * w.y + cz * w.z;
}
#endif

#if defined(_HATCH_ON)
// Screen-space halftone dots / hatching lines whose weight follows the darkness of the lit color.
// _HatchMode: 0 = halftone, 1 = hatching, 2 = cross-hatching.
half3 ApplyHatching(half3 color, InputData inputData)
{
    float2 p = inputData.normalizedScreenSpaceUV * _ScreenParams.xy;
    half dark = saturate((1.0h - Luminance(color)) + _HatchBias);
    half soft = 0.05h + _HatchSoftness * 0.2h;
    float angle = radians(_HatchAngle);
    float2 dir = float2(cos(angle), sin(angle));
    float2 perp = float2(-dir.y, dir.x);
    half mask;

    if (_HatchMode < 0.5h)
    {
        float2 g = float2(dot(p, dir), dot(p, perp)) / _HatchScale;
        half d = (half)length(frac(g) - 0.5);
        half r = sqrt(dark * 0.3183099h);                       // dot area = darkness
        mask = saturate((r - d) / soft + 0.5h) * saturate(dark * 20.0h);
    }
    else
    {
        float t1 = dot(p, dir) / _HatchScale;
        half w1 = (_HatchMode < 1.5h) ? dark : saturate(dark * 2.0h);
        half m1 = saturate((w1 * 0.5h - (half)abs(frac(t1) - 0.5)) / soft + 0.5h) * saturate(w1 * 20.0h);
        half m2 = 0.0h;
        if (_HatchMode > 1.5h)
        {
            float t2 = dot(p, perp) / _HatchScale;
            half w2 = saturate(dark * 2.0h - 1.0h);
            m2 = saturate((w2 * 0.5h - (half)abs(frac(t2) - 0.5)) / soft + 0.5h) * saturate(w2 * 20.0h);
        }
        mask = max(m1, m2);
    }
    return lerp(color, _HatchColor.rgb, mask * _HatchStrength);
}
#endif

#endif // STYLIZED_EFFECTS_INCLUDED
