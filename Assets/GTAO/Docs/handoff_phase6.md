# GTAO Optimization: Phase 6 Handoff (Bent normals + specular occlusion) - FINAL

Target: Unity 6.3 (6000.3), URP 17.3, RenderGraph, Forward / Forward+, PC/console.
Status: **implemented, NOT yet compiled, run or profiled.** This is the last planned phase. Phases 2-6 have only been checked by reading the diffs (brace balance, line endings, settings coverage), never in Unity. Expect a round of compile/shader errors; fix them phase by phase using the toggle list at the bottom.

## Done
**Producer (all gated by `bentNormals`, off by default, zero cost when off)**
- `GTAO()` accumulates a per-slice bent normal under the material keyword `_GTAO_BENT_ENABLED`, using the Jimenez 2016 "Algorithm 2" integrals. I checked the formulas (`t0`, `t1`, the `pl` weighting and the final normalisation) against XeGTAO's `XeGTAO.hlsli`. **One deliberate difference:** XeGTAO rotates `(dir.xy, 0)` into view alignment; this implementation uses the exact slice-plane tangent `normalize(ortho)` (the plane `n` is already measured in), so it is not bit-for-bit XeGTAO.
- Pixels with no usable bent normal (sky/far, or screen radius too small) fall back to the geometric normal; faded pixels blend toward the normal by the distance fade.
- **MRT plumbing:** Estimate, Temporal and Atrous passes write a second target (`dst2`, attachment index 1) with the bent normal (RGBA16F, view space, unnormalised after filtering). Each has a wrapper returning an `SV_Target0/1` struct only when `_GTAO_BENT_ENABLED` is on; otherwise they keep their old single-target signature.
- **Denoising:** the temporal pass blends the bent history with the same depth-validated tap weights and history weight as the AO; each a-trous iteration filters it with the same plane weights; the 4x2 upsample (when `downsample` is on) filters it like the AO. A second history pair (`bentRt`, RGBA16F) is allocated per camera only while bent normals are on.
- **New pass 9 `GTAO_ResolveBent`:** full-res RGBA8 world-space bent normal (`rgb = n * 0.5 + 0.5`, `a = 1` valid / `0` no usable bent normal). Exported as global texture `_GTAO_BentNormalsTexture` and global keyword `_GTAO_BENT_NORMALS`. It runs in both Lighting and AfterOpaque modes.
- The keyword reset pass now also clears `_GTAO_BENT_NORMALS` (it is enqueued whenever bent normals are produced, not only in Lighting mode), via a configurable `Configure(resetSSAO, resetBent)`.

**Consumer: `Shaders/GTAOBentNormals.hlsl`**
- `GTAO_SampleBentNormalWS(screenUV)`, `GTAO_GetBentNormalWS(screenUV, normalWS, strength)` (falls back to the normal where invalid or when the keyword is off), `GTAO_SpecularOcclusion(NoV, ao, perceptualRoughness)` (Lagarde & de Rousiers 2014), and Shader Graph Custom Function wrappers `GTAO_BentNormal_float` / `GTAO_SpecularOcclusion_float`.
- URP Lit does **not** read any of this. You must include the file in your own shaders (or Shader Graph) and add `#pragma multi_compile_fragment _ _GTAO_BENT_NORMALS`.

## Files touched
`Shaders/GTAO.hlsl`, `Shaders/GTAO.shader` (pass 9; keyword added to passes 0, 1, 8), `Shaders/GTAOPass.cs`, `Shaders/GTAOFeature.cs`, `Shaders/GTAOSettings.cs`, `Shaders/GTAOBentNormals.hlsl` (new), `Docs/handoff_phase6.md` (new).

## New setting
| Field | Default | Notes |
|---|---|---|
| `bentNormals` | `false` | Effective only with `denoiser = Atrous` (`GTAOSettings.UsesBentNormals`); silently ignored with Legacy |

## Deviations from the Phase 6 plan
- **No `specularOcclusion` setting.** Specular occlusion is a consumer-side function, so it lives in the include instead of the producer settings.
- **Only the Lagarde specular-occlusion variant.** I did not add the cone-cone intersection variant: its specular-lobe cone-angle mapping needs constants I couldn't verify, and the plan said "or".
- **No bent-normal debug view** in the pass. Inspect `_GTAO_BentNormals` (RGBA8, world normal * 0.5 + 0.5) in the Frame Debugger.
- **Local vs global keyword names differ on purpose:** material keyword `_GTAO_BENT_ENABLED` (producer variants) and global keyword `_GTAO_BENT_NORMALS` (consumers).

## Entry checks
1. Everything compiles, including all new keyword variants (Estimate/Atrous now have ~32 variants each). Likely trouble spots: the `Estimate/Temporal/Atrous` MRT wrapper structs, `[branch] if (...) UpsampleAO(uv, bent); else {...}` in `FragResolveBent`, and `RenderGraph` rejecting the MRT attachment setup (formats/sizes must match per pass; they should).
2. `bentNormals = false`: frame identical to Phase 5, no extra passes/RTs (Frame Debugger shows no `GTAO Resolve Bent`).
3. `bentNormals = true`, Atrous: Frame Debugger shows Estimate/Temporal/Atrous writing two targets and a `GTAO Resolve Bent` pass; `_GTAO_BentNormals` looks like a smooth world-space normal map: flat floors ~ (0.5, 1.0, 0.5) in the encoded colour, tilting toward open space near corners and away from nearby walls. No black/NaN pixels, `a` ≈ 1 almost everywhere except the sky.
4. With a test shader or Shader Graph using the include: ambient/probe lookups with the bent normal give plausible directional lighting at corners; specular occlusion darkens reflections inside creases. Test with `downsample` on and off, temporal on and off, blur Off/Low/Medium/High.
5. Switching `bentNormals` on/off at runtime reallocates the history (one frame without history); check nothing flickers or leaks (`_GTAO_BENT_NORMALS` is cleared after transparents).
6. Camera-switch/multi-camera: only cameras whose feature has bent normals enabled should see the keyword.

## Measurements to capture
Same scene/camera path/build as earlier phases. Estimate, Temporal, Atrous and total GPU time with `bentNormals` off vs on, per preset and resolution (XeGTAO reports roughly +25% for the directional part; the extra MRT write and per-pass bent taps add to it here). Record the extra memory: RGBA16F raw + up to 2 ping-pong + 2 history textures at AO resolution, plus an RGBA8 full-res target.

## Known issues / risks
- Compile and runtime status unverified; the bent-normal math follows XeGTAO but the view-space handedness/orientation mapping to Unity's view space (camera looks down -Z, V = toward camera = +Z) is the most likely source of a visually wrong result (e.g. bent normals tilting the wrong way across screen X or Y). If so, flip one axis of `tang` in `GTAO()` first.
- Bent normals are filtered unnormalised, and zero vectors (sky) can pull neighbouring averages toward zero near sky edges; the `a` flag in the final target and the geometric-normal fallback in the include cover this.
- Temporal blend of the bent normal is not variance-clamped (there is no equivalent), so fast-changing geometry can lag slightly more than the AO.
- Added cost is real: do not enable it unless a shader consumes it.

## Whole-project summary (Phases 1-6)
| Phase | What | Rollback toggle |
|---|---|---|
| 1 | Per-pass profiling samplers, `PassInputs`, no per-frame GC (settings clone, reflection) | n/a (pure refactor) |
| 2 | Linear-depth MIP chain, AO taps read coarser MIPs | `useDepthMips = false` |
| 3 | Cheaper estimate math, `sampleDistributionPower`, `thinOccluderCompensation`, Hilbert noise option | defaults reproduce Phase 2 (`noise = Interleaved`) |
| 4 | A-trous plane-aware spatial denoiser + depth-validated temporal with history age | `denoiser = Legacy` |
| 5 | Bilateral half-res upsample, optional bilinear low-res publish | `upsample = Bilateral`, or `downsample = false` |
| 6 | Bent normals + consumer include | `bentNormals = false` |

**Back to the original behaviour in one go:** `useDepthMips = false`, `noise = Interleaved`, `denoiser = Legacy`, `bentNormals = false` (and leave `sampleDistributionPower = 2`, `thinOccluderCompensation = 0`). Pass indices 0-5 and the original settings fields were never renamed or removed.

**If something breaks after pulling these in, bisect in this order:** `bentNormals` off, then `denoiser = Legacy`, then `useDepthMips` off. Each step removes one phase's rendering path without recompiling.

## Rollback of the code
Phase 6 is additive: revert the five modified files and delete `GTAOBentNormals.hlsl` to remove it. Pass 9 is appended, so nothing else is renumbered.
