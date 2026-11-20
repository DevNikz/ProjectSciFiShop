#ifndef PLANAR_REFLECTION_SHADERGRAPH_INCLUDED
#define PLANAR_REFLECTION_SHADERGRAPH_INCLUDED

// Shader Graph Custom Function nodes (Type: File).
//
// Node 1, Name: SamplePlanarReflection
//   Inputs : ScreenUV (Vector 2), Distortion (Vector 2), Roughness (Float)
//   Output : Color (Vector 3)
//   Feed ScreenUV from a Screen Position node in Default mode.
//
// Node 2, Name: GetPlanarReflectionFade
//   Inputs : PositionWS (Vector 3)   (Position node, World space)
//   Output : Fade (Float)            Multiply your reflection blend by it.
//
// Node 3 (optional, for STP/TAA), Name: GetPlanarReflectionScreenUV
//   Inputs : PositionWS (Vector 3), ScreenUV (Vector 2)   (Screen Position node, Default mode)
//   Output : UV (Vector 2)           Feed this into SamplePlanarReflection instead of the raw ScreenUV.
#include "Packages/com.studio.planar-reflections/Shaders/PlanarReflection.hlsl"

void SamplePlanarReflection_float(float2 ScreenUV, float2 Distortion, float Roughness, out float3 Color)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = float3(0.0, 0.0, 0.0);
#else
    Color = SamplePlanarReflection(ScreenUV, Distortion, Roughness);
#endif
}

void SamplePlanarReflection_half(half2 ScreenUV, half2 Distortion, half Roughness, out half3 Color)
{
#ifdef SHADERGRAPH_PREVIEW
    Color = half3(0.0, 0.0, 0.0);
#else
    Color = SamplePlanarReflection(ScreenUV, Distortion, Roughness);
#endif
}

void GetPlanarReflectionScreenUV_float(float3 PositionWS, float2 ScreenUV, out float2 UV)
{
#ifdef SHADERGRAPH_PREVIEW
    UV = ScreenUV;
#else
    UV = GetPlanarReflectionScreenUV(PositionWS, ScreenUV);
#endif
}

void GetPlanarReflectionScreenUV_half(half3 PositionWS, half2 ScreenUV, out half2 UV)
{
#ifdef SHADERGRAPH_PREVIEW
    UV = ScreenUV;
#else
    UV = GetPlanarReflectionScreenUV(PositionWS, ScreenUV);
#endif
}

void GetPlanarReflectionFade_float(float3 PositionWS, out float Fade)
{
#ifdef SHADERGRAPH_PREVIEW
    Fade = 1.0;
#else
    Fade = GetPlanarReflectionFade(PositionWS);
#endif
}

void GetPlanarReflectionFade_half(half3 PositionWS, out half Fade)
{
#ifdef SHADERGRAPH_PREVIEW
    Fade = 1.0;
#else
    Fade = GetPlanarReflectionFade(PositionWS);
#endif
}

#endif
