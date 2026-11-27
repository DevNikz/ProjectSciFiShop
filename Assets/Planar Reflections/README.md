# Planar Reflections (URP Forward+) - Phase 4

Multi-reflector, Game and Scene view reflections with quality controls. DX12 is the main target.

## Project setup
- Unity 6000.0.12f1 or later (`SubmitRenderRequest` inside `beginCameraRendering` needs it).
- URP asset: Forward+ renderer. Render Graph on or off both work (the blur does not use the render graph).
- Package lives in `Packages/com.studio.planar-reflections` (embedded).

## Try it
1. `GameObject > Planar Reflections > Planar Reflector` creates a plane, a material, and the component.
2. Optional: `Assets > Create > Planar Reflections > Profile`, assign it on the reflector.
3. Optional: `Assets > Create > Planar Reflections > Settings`, save it as
   `Assets/Resources/PlanarReflections/PlanarReflectionSettings.asset`.

## Profile (per reflector, shared by assets)
- Resolution scale, format (HDR / HDR half / LDR), MSAA.
  MSAA is on or off here. The reflection texture is always single-sample; URP renders with its own MSAA target
  and resolves into it. The sample count comes from the URP asset (2 or more). Any value but Off means "on".
  Ignored while STP upscaling is active (STP forces TAA, which cannot combine with MSAA).
- Layer mask (default: everything except Water and UI) and clip offset.
  The reflector's own renderers are hidden during the reflection render (`forceRenderingOff`), so objects on the
  reflector's layer still reflect.
- Distance fade: per-pixel, by distance to the camera. Off while End <= Start.
- Blur mode:
  - None: sharp.
  - Mips: box-filtered mip chain, roughness picks the LOD (range set in the profile).
  - Blurred: half-res Kawase blur in a second texture, roughness blends sharp and blurred.

## Settings (project-wide)
- Max active reflectors per source camera (default 4). The rest keep their last image.
- Blur backend: Auto (compute on DX12, raster elsewhere), Raster, or Compute.

## Shader
- `PlanarReflections/PlanarReflectionLit` has a Roughness slider and uses the fade.
- The lit shader receives shadows: main light (including cascades) and additional lights in Forward+, plus light
  layers and cookies. Lighting uses URP's Blinn-Phong with no specular; the planar reflection is the specular.
  - `Shadow On Reflection` (default 0.5): how much the main light shadow also dims the reflection.
    Without it a shadow disappears where the fresnel blend is mostly reflection. 0 = shadow only on the diffuse term.
  - `Disable Shadow Receiving` turns shadow receiving off for the material.
  - Shader Graph lit graphs already receive shadows. Multiply the reflection by the main light shadow attenuation to match.
- Shader Graph: use a Custom Function node (Type: File) with `Shaders/PlanarReflectionShaderGraph.hlsl`.
  - Add a MotionVectors pass that writes zero (see `PlanarReflectionLit.shader`) so STP/TAA does not smear the reflection.
  - `GetPlanarReflectionScreenUV`: PositionWS (V3), ScreenUV (V2) -> UV (V2). Feed it into `SamplePlanarReflection` (needed for STP/TAA).
  - `SamplePlanarReflection`: ScreenUV (V2), Distortion (V2), Roughness (Float) -> Color (V3).
  - `GetPlanarReflectionFade`: PositionWS (V3) -> Fade (Float). Multiply your reflection blend by it.
  - Do not expose `_PlanarReflectionTex` as a property.

## Budget (Phase 4)
Profile > Budget cuts the cost of the mirrored camera. All of it is per camera, set on the hidden reflection camera.
- Render Shadows (default off): skips main and additional light shadows. The biggest Forward+ saving.
- Far Clip (0 = same as the source camera): shorter draw distance for the reflection. It also caps the shadow
  distance and the lights visible to the reflection, which is how you reduce Forward+ light cost.
- LOD Bias Scale (default 1 = unchanged): scales `QualitySettings.lodBias` only while the reflection renders.
  Below 1 picks coarser LODs and can drop LOD groups that have a Culled level (often characters) out of the reflection.
  Profiles saved with an older default of 0.5 keep that value: set it to 1.
- Renderer Index (-1 = the URP default renderer): picks a stripped budget renderer.

Forward+ has no per-camera light cap you can change at runtime. The visible-light limit is a compile-time URP value
(`ShaderConfig`), and the Additional Lights settings in the URP asset are ignored in Forward+. Use Far Clip.

### Budget renderer setup (manual, one time)
1. `Assets > Create > Rendering > URP Universal Renderer`. Name it `PlanarReflectionBudget`.
2. Rendering Path: Forward+. Depth Priming Mode: Disabled. Transparent Receive Shadows: off.
3. Renderer Features: none (no SSAO, Decals, or full screen passes).
4. Add it to the URP asset's Renderer List and note its index.
5. Set that index as Renderer Index in the profile.
6. `Tools > Planar Reflections > Validate Setup` lists the renderers and flags profiles that point to a missing
   or heavy renderer.

STP: it needs the post pass, so a reflection camera keeps post processing on while STP is active. The budget renderer
saves less in that case.

### Measuring
- Profiler: the marker `PlanarReflections.Render` wraps each reflection render.
- Frame Debugger: compare the reflection camera's events before and after (shadow passes gone, fewer draws).

## Spike checklist (verify on DX12 first)
Phase 1/2 items still apply (orientation, `Invert Culling`, recursion, clip, Scene view). New:
- [ ] Mips mode: higher roughness gives a visibly blurrier reflection.
- [ ] Blurred mode (raster backend): the blur appears and is not black.
      The blur runs through `Graphics.ExecuteCommandBuffer` right after the reflection render.
      If it is black or one frame late, report it.
- [ ] Blurred mode with backend = Compute on DX12: same result as raster.
- [ ] MSAA on with URP MSAA enabled and STP off: smoother reflection edges, no "Missing resolve surface" errors.
- [ ] STP on (render scale 1.0 and below): reflection is not ghosted or offset, and `msaa-ignored-stp` warns once.
      Update interval is forced to 1 while STP is active.
- [ ] STP, moving camera: the reflector appears in the Frame Debugger's MotionVectors pass (zero motion).
      Mirrors and TAA/STP are a known hard case, so some softness in motion can remain. Report it.
- [ ] FSR / bilinear / point: reflection resolution and alignment match the source camera.
- [ ] Mips mode: roughness LOD range works (mips are generated after the request is submitted).
- [ ] Frame Debugger: the Blurred-mode blur event sits inside the reflection camera's submit, before the source camera draws.
      If the warning `blur-no-end-callback` shows, `endCameraRendering` did not fire and blur ran outside the context.
- [ ] Jitter alignment: `GetPlanarReflectionScreenUV` relies on `_NonJitteredViewProjMatrix`. If the reflection is
      flipped or missing, the matrix is not set that way in your URP version. Report it.
- [ ] Resolution scale 0.5 halves the reflection texture in the Frame Debugger.
- [ ] Fade: the reflection fades toward the set end distance.
- [ ] Shadows on the plane: a tree or box shadow shows on the reflector, at steep and grazing view angles.
      Cascade seams, soft shadows, and an additional (spot or point) light shadow in Forward+ all work.
- [ ] `Shadow On Reflection` 0 vs 1: the shadow is still visible at 0 on the diffuse part, stronger in the reflection at 1.
- [ ] The MeshRenderer must have Receive Shadows on and the URP asset shadows enabled (main light shadows, additional light shadows).
- [ ] Render Shadows off: the shadow passes are gone from the reflection camera in the Frame Debugger.
- [ ] Far Clip 50: distant objects drop out of the reflection, and the oblique clip still works.
- [ ] LOD Bias Scale 0.5: the source camera's LODs are unchanged after the reflection renders (the value is restored).
      Characters with LOD groups may vanish from the reflection at this value. At 1 they stay.
- [ ] Skinned meshes (characters) on the same layer as the reflector, and with a LOD group, both show in the reflection.
- [ ] Renderer Index pointing at the budget renderer: the Frame Debugger shows the leaner renderer for the reflection only.
      The warning `renderer-index-N` means URP fell back to the default renderer.
- [ ] `PlanarReflections.Render` shows in the Profiler, and its time drops versus the default renderer with shadows on.

## Known limits
- There is no Renderer Feature: URP has no supported per-camera hook for budget work, so the budget is camera and renderer-asset settings.
- MaterialPropertyBlock on reflector renderers breaks SRP Batcher for those renderers.
- Blur textures are half-res and not mip-chained.
- Screen weight is an approximation (bounds extents over distance).

## Tests
Window > General > Test Runner > EditMode runs the math, pool, visibility, profile, and blur-order tests.
