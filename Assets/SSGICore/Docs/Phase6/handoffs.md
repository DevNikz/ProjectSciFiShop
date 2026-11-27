# Phase 6 handoff: light-driven GI, contact occlusion, leak reduction, adaptive rays

Nothing was compiled, run or profiled in Unity here. Treat everything below as unverified until the checks at the end pass.

## What was built
- **Light Response pass** (`SSGICore_Light.compute`, kernel `LightResponse`, trace resolution, RG16F `_SSGI_LightTex`: x = ratio - 1, y = delta).
  - `ratio = sum(scale_i * E_i) / sum(E_i)` over the main light and the point/spot lights in the pixel's Forward+ cluster
    (`LIGHT_LOOP_BEGIN` with `inputData.positionWS` / `normalizedScreenSpaceUV`, `GetAdditionalLight(lightIndex, posWS)`). `E_i` is unshadowed `N.L * luma(colour) * attenuation`.
  - Scale array index = URP's additional-light index (`visibleLights` order with `mainLightIndex` skipped), which is what `lightIndex` is inside the loop.
  - `delta = max(DirtyAt(posWS), mainDelta * mainShare)`: CPU-built dirty spheres of lights that changed, plus the main light's change weighted by its share of the light.
- **CPU side**: `SSGICoreLight` (component + registry) and `SSGICoreLightHistory` (CPU-only `CameraHistoryItem`: scale array, up to 16 dirty spheres, main-light scale/delta).
  Spheres are emitted for moved/changed lights (old and new position), spawned/newly visible lights (new) and removed/disabled/culled lights (last position).
  `SSGICoreLightMath.ComputeDelta` maps change to 0..1 (2% of range moved, 1 degree, 5% brightness, 5% GI scale each saturate it).
- **Consumers of the response**
  - Trace `SampleLighting`: radiance picked up at a hit is multiplied by `ApplyLightFloor(ratio)`; previous-frame feedback weight is multiplied by `1 - delta`.
  - Temporal: `alpha = max(alpha, delta * LightFlush)`, and the age is reduced accordingly.
  - TraceRestir: previous reservoir confidence M is scaled by `1 - delta`.
  - Cache Resolve: history of cells inside a dirty sphere is scaled by `1 - amount * falloff`.
  - `RayBudget` (adaptive rays) never reduces rays where `delta >= 0.05`.
- **Light GI Floor** (`lightGiFloor`): `ratio' = lerp(ratio, 1, floor)`, so emissive/lightmapped radiance can be kept independent of the per-light scale.
- **Contact occlusion**: first-bounce hit distance / `Contact Radius` (openness, 1 = open) is written to the trace alpha, accumulated into `HistMom.w` by the temporal pass,
  bilaterally upsampled into the GI texture alpha, and applied by the composite (`Blend One SrcAlpha, Zero One`, `keep = 1 - strength * (1 - openness)`).
- **Leak reduction**: thickness `Thickness * (1 + zScene * 0.02)` instead of `max(1, zScene * 0.1)`, grazing-hit rejection (`dot(n, dir) < -0.02`).
- **Adaptive rays**: age >= max(4, MaxAge / 2) and delta < 0.05 -> 1 ray. Only matters for Ray Count > 1 (Medium has 1).
- The pass is skipped (black texture bound, ratio 1, delta 0) when `Light Response` is off, the shader is unsupported, the light history is not available yet (first frame),
  or no light has scale != 1, nothing changed and the main light is unchanged. In that case behaviour equals the previous build apart from Contact Occlusion, Leak Reduction and Adaptive Rays.

## Files
- New: `Runtime/SSGICoreLight.cs`, `Runtime/SSGICoreLightHistory.cs`, `Resources/SSGICore/SSGICore_Light.compute`,
  `Tests/Editor/SSGICoreLightTests.cs`, `Tests/Runtime/SSGICoreLightRuntimeTests.cs`, this file. (Unity generates the `.meta` files on import.)
- Changed: `SSGICorePass.cs`, `SSGICoreVolume.cs`, `SSGICoreFrameConstants.cs`, `SSGICoreUtil.cs`, `SSGICoreResources.cs` (optional Light shader),
  `SSGICore_Common.hlsl`, `_Trace`, `_Temporal`, `_Upsample`, `_Cache` compute, `SSGICore_Composite.shader`, README, CHANGELOG.

## Known limits
- Shadows are not evaluated in `ratio` (scene colour is already shadowed). A light that is fully shadowed at a pixel still counts there, which only matters for relative scale between lights.
- The ratio multiplies the whole radiance at the hit. With every light at scale 0, emissive and baked light is scaled to 0 too, unless *Light GI Floor* > 0.
- Additional directional lights are not scaled or tracked (scale 1). Non-Forward+ renderers get ratio 1 and only the change signal.
- Dirty spheres are coarse (range spheres): a moved light flushes its whole old and new range, not just the pixels that changed. More than 16 changed lights in a frame keeps the strongest 16.
- A light entering or leaving the camera's culling counts as spawned/removed and flushes its range once.
- Contact occlusion darkens the whole lit colour (direct light included) and is not computed for ReSTIR. The openness is only temporally filtered, not a-trous filtered.
- The light history is available from the second frame of a camera.
- The ratio texture is sampled with a point load at hit pixels (trace resolution), so it is blocky at light boundaries by up to one low-res pixel.

## Verify (in this order)
1. Compile. The riskiest piece is `SSGICore_Light.compute`: it includes URP `Core.hlsl` + `RealtimeLights.hlsl` in a compute shader next to `SSGICore_Common.hlsl`. If it fails to compile,
   SSGICore still runs (the shader is optional, one warning is logged) and only Light Response is disabled. Likely fixes: include order, a clashing name, or `_CLUSTER_LIGHT_LOOP` variant stripping.
2. Confirm the kernel really runs the cluster loop: in the Frame Debugger the `SSGICore Light Response` pass must appear with a non-black output when a light has `giIntensity` 0 or 2.
   If the output is always black, check that the global keyword `_CLUSTER_LIGHT_LOOP` selects the right compute variant and that `urp_ZBinBuffer` / `urp_TileBuffer` / `AdditionalLights` are visible to compute.
3. Parity: Light Response off, Contact Occlusion 0, Leak Reduction off, Adaptive Rays off must equal the Phase 5 output (gate 1% RMSE).
4. Dynamic lights: move a point light fast across a wall: no trailing bounce ghost; a light spawned at runtime lights its neighbourhood within a frame or two; toggling it off removes its bounce.
5. Per-light scale: two lights, `giIntensity` 0 on one: its colour disappears from the bounce but its direct light stays. Then try `Light GI Floor` 1.
6. Contact occlusion: creases at floor/wall corners are darker with the slider at 0.5 and unchanged at 0.
7. Leaks: thin walls/doors that used to pass light through at long distance; check no new dark halos.
8. Timings (paste here): GPU ms for Off / Light Response on, all scales 1 / on, one light moving / Adaptive Rays on with High preset.
   Budget: up to 20% over the previous build; the Light Response pass is expected to be small (one cluster loop at trace resolution).

## Open API assumptions (unverified)
- A global keyword set through `cmd.SetKeyword` in URP's `SetupLights` pass selects the multi_compile variant of a later compute dispatch.
- URP's global constant buffers (`urp_ZBinBuffer`, `urp_TileBuffer`, `AdditionalLights`) bound by that pass stay valid and visible to compute dispatches later in the frame.
- `UniversalCameraHistory.GetHistoryForWrite<T>()` works for a `CameraHistoryItem` that owns no textures, and returns null only on the first frame.
- `UniversalLightData.visibleLights` / `mainLightIndex` and `VisibleLight.finalColor / localToWorldMatrix / range / spotAngle / light` as in URP 17.
- `Blend One SrcAlpha, Zero One` on the camera colour target; the target's alpha is preserved.
