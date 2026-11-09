# GTAO Optimization: Phase 5 Handoff (Half-res upsample)

Target: Unity 6.3 (6000.3), URP 17.3, RenderGraph, Forward / Forward+, PC/console.
Status: **implemented, NOT yet compiled, run or profiled.** Phases 2-4 are also still unverified by running; their entry checks remain open. This phase only matters when `downsample = true`; with `downsample = false` nothing changes.

## Done
**Bilateral 2×2 upsample (Atrous + `downsample`)**
- New `UpsampleAO(uv)` in `GTAO.hlsl`. For each full-res pixel it reads the 4 nearest AO-resolution texels (from the last denoise output) and weights each by `bilinear coverage × exp(-relativeDepthError × blurDepthSharpness)`, where the error compares the texel's stored depth (`.y`) with the full-res pixel's depth (`SampleViewDepth(uv, 0)`). If every tap is rejected, the tap closest in depth is used (not a blind blend).
- It replaces the old 1-tap vertical blur that doubled as the upsample. It is folded into the existing `ResolveAO`, which the Resolve, Composite and Debug passes all share, so there is **no new shader pass, keyword or render target** (a deviation from the plan, which proposed a separate pass 9; folding it avoids an extra pass and keeps pass numbering unchanged).
- Selected by `_GTAO_Denoise.z` (set when `denoiser = Atrous` and `downsample` is on). The Legacy denoiser keeps its blur-as-upsample.
- It reuses `blurDepthSharpness` (the same depth-sharpness control the legacy blur used), so there is no new tuning field.
- `GTAOSettings.GetResolveBlurRadius()` is now 0 for Atrous (the previous "1 when downsampling" workaround is no longer needed).

**`GTAOUpsample { Bilateral, Bilinear }` setting**
- `Bilateral` (default): as above.
- `Bilinear`: applies only with Lighting mode + Atrous + `downsample`. The full-res Resolve pass is skipped and the **last denoise pass** (Atrous iteration N, or Temporal, or Estimate if those are off) publishes its half-res output directly as `_ScreenSpaceOcclusionTexture` (and sets the SSAO keyword/params). The AO targets are created with bilinear filtering in this mode, because URP lit shaders sample the AO with the texture's own filter. This saves the full-res R8 write plus the full-res depth read, at the cost of leaking AO across depth edges.
- In AfterOpaque mode `Bilinear` has no effect (the composite pass always runs, using the bilateral upsample).

## Files touched
`Shaders/GTAO.hlsl`, `Shaders/GTAOPass.cs`, `Shaders/GTAOSettings.cs`, `Docs/handoff_phase5.md` (new). `GTAO.shader` is unchanged.

## New settings (in `SanitizeInto`)
| Field | Default | Notes |
|---|---|---|
| `upsample` | `Bilateral` | Only matters with `downsample` on and `denoiser = Atrous` |

## Entry checks (before Phase 6)
1. Compiles; the shader compiles for all variants (`UpsampleAO`: unrolled loop, `k >> 1`, early exit pattern).
2. `downsample = false`: frame identical to Phase 4.
3. `downsample = true`, `Bilateral`: compare against `downsample = false` at the same quality. Look for blockiness, 2-px halos at silhouettes, and AO leaking from background onto foreground edges (raise `blurDepthSharpness` if it leaks, lower it if you see aliasing/jaggies).
4. `downsample = true`, `Bilinear`, Lighting mode: Frame Debugger shows no `GTAO Resolve` pass; `_ScreenSpaceOcclusionTexture` is the half-res target; lit surfaces still receive AO (SSAO keyword set, no black/white screen). Test with `blur` Off/Low/Medium/High and with temporal on/off (the published pass differs: Estimate, Temporal, or the last Atrous).
5. AfterOpaque mode with `Bilinear` selected: behaves like `Bilateral`.
6. Legacy denoiser + `downsample`: unchanged from Phase 4's Legacy.
7. 0 B GC per frame.

## Measurements to capture
Same scene/camera path/build. Compare full-res vs `downsample` (Bilateral) vs `downsample` (Bilinear) at Medium/High/Ultra, 1080p/1440p/4K: per-pass times (Estimate, Temporal, Atrous, Resolve/Composite) and total. Report quality (screenshots at the same camera positions). If half-res Bilateral isn't meaningfully cheaper than full-res with fewer samples, say so.

## Known issues / risks
- Compile and runtime status unverified.
- With `Bilinear`, the published texture is whichever pass ran last, and may be the **history texture** (`hCur`) when the Atrous passes are off. It is read-only for the rest of the frame, but verify there's no flicker or one-frame lag.
- `Bilinear` publishes an RG16F (or RGBA16F history) texture as `_ScreenSpaceOcclusionTexture`; URP reads `.x`. If some custom shader assumes a single-channel R8 texture, it should still work, but check.
- The temporal jitter still cycles a 2×2 pattern; with a short history the half-res upsample can show faint 2×2 crawl on high-contrast edges. Lower `temporalWeight` or add samples if visible.
- Upsample weights are depth-only (no normals). Surfaces at a steep angle with similar depth but different orientation can blend; a normal-similarity term could be added if this matters.

## Next: Phase 6 - Bent normals + specular occlusion (gated)
Only worth doing if you will adopt the new include in your own shaders: **URP Lit reads only the AO `.x` channel** and the URP package can't be edited in place, so nothing consumes bent normals automatically. Confirm that before starting.
1. `GTAO.hlsl`: in `GTAO()` accumulate the bent-normal direction per slice (XeGTAO's cone-centre method: the sum of the visible-cone bisector vectors, rotated back into view space), return it alongside the visibility. `FragEstimate` becomes MRT: SV_Target0 = (ao, depth), SV_Target1 = bent normal (e.g. RGBA8 snorm or octahedral RG8). XeGTAO notes this adds roughly 25% to the estimate cost.
2. `GTAOPass.cs`: `PassInputs` gains `dst2`; `AddPass` calls `SetRenderAttachment(dst2, 1, Write)`; create the extra RT with `CreateAOTexture`. Denoise the bent normal with the same temporal and à-trous weights (extra history RT, extra ping-pong RTs, or pack it into the existing RGBA history's spare channel). Re-normalise after filtering.
3. Publish `_GTAO_BentNormalsTexture` + keyword `_GTAO_BENT_NORMALS` the same way as `_ScreenSpaceOcclusionTexture` (`publishGlobals` path).
4. New `Shaders/GTAOBentNormals.hlsl` for consumers: `GTAO_SampleBentNormal(uv)`, `SpecularOcclusion(ao, NoV, roughness)` (Lagarde) or the bent-normal cone-cone intersection variant, plus a short helper for sampling diffuse GI/ambient with the bent normal. Provide a Shader Graph custom function wrapper.
5. `GTAOSettings.cs`: `bentNormals` (default false), `specularOcclusion` (default true when bent normals are on); both in `SanitizeInto`.
6. Verify: `bentNormals = false` has zero cost and is identical to Phase 5; with it on, a test shader shows plausible directional ambient on a wall corner and reduced specular leaks.
7. Write `handoff_phase6.md` (final handoff).

## Rollback
`upsample = Bilateral` (default) is the standard path; `denoiser = Legacy` restores the pre-Phase-4 behaviour for everything including upsampling. To remove the code, revert the three source files (no shader pass/keyword changes in this phase).
