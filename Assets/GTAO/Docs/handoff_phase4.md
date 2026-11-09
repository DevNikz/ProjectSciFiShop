# GTAO Optimization: Phase 4 Handoff (Denoiser: spatial + temporal)

Target: Unity 6.3 (6000.3), URP 17.3, RenderGraph, Forward / Forward+, PC/console.
Status: **implemented, NOT yet compiled, run or profiled.** The project owner has compiled earlier phases (that is how the `renderingModeActual` error was found and fixed), but nothing from Phases 2-4 has been run or visually checked yet. Treat Phases 2-4 entry checks as all still open.

## Done
**Spatial: `GTAO_Atrous` (shader pass 8, `FragAtrous`)**
- 3×3 binomial kernel `[1 2 1]⊗[1 2 1]`, dilated by step 1, 2, 4 on successive iterations (a-trous). Each neighbour is weighted by `DepthPlaneWeight`: distance from the centre pixel's tangent plane, `saturate(1 - |dot(N, Pq - P)| / (planeTolerance * depth))`. Planar surfaces at grazing angles keep smoothing; real depth/normal edges stop it.
- The centre normal comes from the existing `GetNormal` (DepthNormals texture or reconstruction); neighbour positions from the stored linear depth in the AO texture's G channel.
- Iterations = the existing `blur` setting: Off 0, Low 1, Medium 2, High 3 (`GTAOSettings.GetAtrousIterations`). Ping-pong between two transient RG16F RTs (`_GTAO_AtrousA/B`).
- Sky / beyond-far-plane pixels return unchanged (early out via `_ProjectionParams.z`).
- The final resolve/composite pass no longer does a full blur in Atrous mode: radius 0, or 1 when `downsample` is on (the 1-tap bilateral upsample, same as the old minimum). `GTAOSettings.GetResolveBlurRadius`.

**Temporal: v2 in `FragTemporal` (selected by `denoiser = Atrous`)**
- 4 point taps of history with bilinear weights, each validated against the reprojected expected depth (replaces one `LinearClamp` tap), so one bad texel no longer poisons the whole footprint.
- Per-pixel history age in `.z`. History weight = `min(effAge / (effAge + 1), temporalWeight)`, where `effAge` is the confidence-weighted age. History rebuilds quickly after disocclusion instead of blending against a stale value. `temporalWeight` is now the *maximum* history weight.
- History format is RGBA16F `(ao, depth, age, -)` in Atrous mode (`s_FmtHist`); Legacy keeps RG16F. Switching `denoiser` reallocates history, and the first frame after a switch is treated as invalid.
- The variance-clamped 3×3 neighbourhood, motion-vector reprojection, velocity fade and NaN guards are unchanged. The Legacy branch is the Phase 3 code path.
- History stores the *temporal output before the spatial filter* (same as before), so spatial filtering doesn't feed back.

**Plumbing:** `PassAtrous = 8` + sampler; `PassData/PassInputs.atrousStep` (set per pass at execution via `_GTAO_AtrousStep`, since the step differs between passes of one frame); `_GTAO_Denoise` constant (x planeTolerance, y temporal-v2 flag). Pass 8 uses the same keywords as Estimate (`_GTAO_DEPTH_MIPS`, `_GBUFFER_NORMALS_OCT`, `_SOURCE_DEPTH_NORMALS`, `_ORTHOGRAPHIC`).

## Files touched
`Shaders/GTAO.hlsl`, `Shaders/GTAO.shader`, `Shaders/GTAOPass.cs`, `Shaders/GTAOSettings.cs`, `Docs/handoff_phase4.md` (new).

## New settings (all in `SanitizeInto`)
| Field | Default | Notes |
|---|---|---|
| `denoiser` | `Atrous` | `Legacy` = Phase 3 behaviour (old blur, old temporal, RG16F history) |
| `planeTolerance` | `0.02` | 0.001-0.2. Fraction of view depth. Atrous only. Lower = sharper edges, more residual noise |

**Behaviour change:** because `denoiser` defaults to `Atrous`, existing GTAO assets switch to the new denoiser automatically. Set `denoiser = Legacy` to restore Phase 3 output.

## Deviations from the Phase 4 plan (and why)
- **Depth stays in the AO texture's G channel** (decision option "b" from the Phase 3 handoff). The denoiser therefore does not depend on the depth MIP chain, so `useDepthMips` can be off.
- **No packed edge bits / R8G8 AO layout.** Edge-stopping uses plane distance computed from neighbours' depth plus the centre normal; no extra data in the AO texture.
- **No AO-variance weight.** Only plane distance is used for edge stopping. Variance guidance (SVGF-style) is possible later but needs a variance channel.
- **No `minHistoryAlpha`.** `temporalWeight` serves as the cap (max history weight).
- **No unjittered-projection change.** URP TAA applies jitter to both the current and previous projection that GTAO stores, so reprojection is already self-consistent. Skipping this also avoids another unverified URP API call. Watch for double accumulation if URP TAA is on (see risks).
- The à-trous kernel is 3×3 (9 taps) rather than 5×5 (25 taps) to keep the pass cheap.

## Entry checks (before Phase 5)
1. Project compiles and the shader compiles for all keyword variants. Likely trouble spots: `FragAtrous` (unrolled loops, `abs` on int, `_ProjectionParams`), the `[branch] if` with an early `return` inside `FragTemporal`, and `float4` returned into an RG16F target in Legacy mode.
2. `denoiser = Legacy` matches the Phase 3 frame (history RG16F, `GTAO Blur H` pass present, no Atrous passes).
3. `denoiser = Atrous`: Frame Debugger shows `GTAO Atrous 1..N` (N from blur: Low 1, Medium 2, High 3) between `GTAO Temporal` and `GTAO Resolve`/`Composite`; Blur H is absent; no "texture not written/read" errors.
4. Visual: less noise than Legacy at the same blur setting; no halos or leaking at depth edges; contact darkening preserved at corners; no ghosting trails behind moving objects; quick recovery after disocclusion (walk the camera around a corner). Check temporal off + blur Off (raw AO) and temporal on.
5. Check `downsample` on and off, DepthNormals vs ReconstructFromDepth, Lighting vs AfterOpaque, orthographic camera, `useDepthMips` on/off.
6. 0 B GC per frame.

## Measurements to capture
Same scene, camera path, build as before. Per preset/resolution, Legacy vs Atrous at blur Low/Medium/High, temporal on/off: record `GTAO Temporal`, `Blur H` / `Atrous 1..3`, `Resolve/Composite` times and total. Record memory delta (history RGBA16F is 2× RG16F, ×2 textures per camera).

Decision rule: Atrous must give equal-or-better noise at equal-or-lower total cost, or at least a clear quality win at the same cost. If it's slower with no visible gain at a given preset, say so here.

## Known issues / risks
- Compile/runtime status unverified.
- **Cost:** 9 taps each with a `ViewPos` + a normal fetch at centre per iteration, versus the old 2×(2R+1) taps with `exp()`. Likely cheaper at High, comparable at Low/Medium; profile.
- **Upsample quality with `downsample`:** the final pass is still a 1-tap bilateral vertical filter, not a true 2×2 bilateral upsample (Phase 5). Expect some blockiness/edge softness in half-res mode.
- **URP TAA interaction:** with URP TAA enabled, TAA also accumulates AO. Double accumulation can over-smooth or smear. If so, lower `temporalWeight` or disable GTAO temporal and rely on the a-trous filter + TAA.
- Plane tolerance is relative to depth; very thin or highly curved geometry may need a smaller value, large flat distant surfaces a larger one.
- `ageSum`/age is stored in half-float and capped at 128. `temporalWeight` above ~0.99 can't be reached by age alone (the settings clamp it to 0.98 anyway).
- When `denoiser` is switched at runtime the history is reallocated (one frame of no history).

## Next: Phase 5 - Half-res upsample
Goal: a proper depth-aware 2×2 upsample for `downsample = true`, and an option to skip the full-res resolve.
1. `GTAO.hlsl`: add `FragUpsample` (new shader pass 9). For each full-res pixel: take the 4 nearest AO-resolution texels (from the last a-trous output), weight each by `bilinear × depth similarity` against `SampleViewDepth(uv, 0)` (full-res) versus the texel's stored depth in `.y`, optionally multiplied by a normal-similarity term (reuse `DepthPlaneWeight` with the full-res normal); normalise; fall back to nearest if the weights collapse.
2. `GTAOPass.cs`: add `PassUpsample = 9` + sampler. In Atrous mode with `downsample`, replace the resolve pass's 1-tap vertical filter with this upsample (set `GetResolveBlurRadius` to 0 in that case, and let `ResolveAO` skip `DoBlur`). Needs `depth`/`depthMips` (mip 0) and `normals` bound, as in Atrous.
3. `GTAOSettings.cs`: `enum GTAOUpsample { Bilateral, Bilinear }`, default `Bilateral`, added to `SanitizeInto`. With `Bilinear`, publish the low-res AO texture directly (bilinear filtering) and skip the full-res resolve pass; this saves bandwidth at the cost of edge leaks.
4. Keep the per-frame 2×2 jitter in `FragEstimate`.
5. Verify: with `downsample = false` nothing changes; with `downsample = true` compare against full-res AO for edge leaks and blockiness; measure the saving vs full-res.
6. Write `handoff_phase5.md` and the Phase 6 (bent normals) entry steps. Phase 6 is gated on adopting the include in your own shaders; URP Lit does not read bent normals.

## Rollback
`denoiser = Legacy` restores Phase 3 rendering at runtime. To remove the code entirely, revert the four files (pass 8 is appended; no other passes were renumbered).
