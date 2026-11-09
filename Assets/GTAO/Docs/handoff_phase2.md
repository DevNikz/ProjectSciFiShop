# GTAO Optimization: Phase 2 Handoff (Depth prefilter + MIP chain)

Target: Unity 6.3 (6000.3), URP 17.3, RenderGraph, Forward / Forward+, PC/console.
Status: **implemented, NOT yet compiled, run or profiled** (no Unity in the authoring environment). Phase 1 entry checks in `handoff_phase1.md` still apply and were not confirmed by the project owner before this phase.

## Done
- Two new shader passes, appended at the end (existing indices 0-5 unchanged):
  - Pass 6 `GTAO_PrefilterDepth`: raw camera depth to linear eye depth, full-res (mip 0). Clamped to 60000 so it stays finite in 16-bit float.
  - Pass 7 `GTAO_DepthMip`: next level = 2x2 average weighted toward the farthest texel. It reuses the AO's `radius` and `falloffRange`, so occluders inside the falloff band aren't averaged into the background.
- `GTAOPass` builds a 5-level chain (mip 0 full-res to mip 4) as **5 separate transient RTs** (`_GTAO_DepthMip0..4`), not one RT with mips. Reason: RenderGraph tracks a texture as one resource, so sampling mip N-1 while rendering mip N of the same texture is a read/write hazard. URP's bloom chain uses the same per-level pattern. This differs from the plan, which assumed per-mip render attachments.
- `GTAO.hlsl`:
  - New `SampleViewDepth(uv, mip)`. With `_GTAO_DEPTH_MIPS` it reads the chain (nearest level, point sampled, branch chain over the 5 textures). Without the keyword it falls back to `LinDepth(uv)`, which is exactly the Phase 1 behaviour.
  - Used in `FragEstimate` (centre depth), `ReconstructNormal` (4 taps, mip 0), `DoBlur` (full-res depth in the resolve) and the GTAO tap loop.
  - Tap mip = `clamp(log2(s * pxR) - depthMipBias, 0, 4)`, where `s * pxR` is the tap's pixel offset in full-res pixels.
  - `SamplePos` now takes a `mip` argument.
- Keyword `_GTAO_DEPTH_MIPS` (`multi_compile_local_fragment`) added to passes 0, 3, 4, 5. Estimate now has 16 variants.
- `AddPass` / `PassInputs` / `PassData` carry the mip handles. Estimate binds all 5 levels, and the resolve/composite/debug passes bind only mip 0 (`m_DepthMip0Only`). `PassData.mips` is allocated once per pooled instance (no steady-state GC).
- Profiler samplers added: `GTAO Prefilter Depth`, `GTAO Depth Mip`.
- The AO RT layout is unchanged: `(ao, depth)`. Depth in G now comes from mip 0 when mips are on.

## Files touched
- `Shaders/GTAOPass.cs`, `Shaders/GTAO.hlsl`, `Shaders/GTAO.shader`, `Shaders/GTAOSettings.cs`
- `Docs/handoff_phase2.md` (new)

## New settings (all in `GTAOSettings`, listed in `SanitizeInto`)
| Field | Default | Notes |
|---|---|---|
| `useDepthMips` | `true` | A/B toggle. `false` = Phase 1 output, bit-identical |
| `depthMipBias` | `3.3` | XeGTAO's value, range 0-6. Higher = coarser MIPs used later (sharper, more bandwidth) |
| `highPrecisionDepthMips` | `false` | 32-bit float chain instead of 16-bit. Not in the original plan; added because the 16-bit chain loses depth precision at distance |

Because `useDepthMips` defaults to `true`, existing GTAO assets pick up the new path automatically (output is close to, not identical to, the old look). Set it to `false` to restore Phase 1 output.

## Entry checks (before Phase 3)
1. Project compiles. Likely trouble spots: `LOAD_TEXTURE2D_X(_CameraDepthTexture, ...)` and `i.positionCS` in the new frag functions, and the `default(TextureHandle)` entries in `m_DepthMip0Only` being treated as invalid (relies on `nullHandle` being the default value).
2. Frame Debugger shows `GTAO Prefilter Depth` plus four `GTAO Depth Mip N` passes before `GTAO Estimate`, and no errors or "texture not written" warnings.
3. `useDepthMips = false` matches the Phase 1 frame exactly. `useDepthMips = true` is visually close at High/Ultra (no banding, no halos at depth edges, no seams at screen edges).
4. Check both `downsample` on/off, `normals` DepthNormals and ReconstructFromDepth, Lighting and AfterOpaque modes, and an orthographic camera (`_ORTHOGRAPHIC` is on the prefilter pass).
5. GC alloc is still 0 B per frame.

## Measurements to capture
Same scenes, camera path, and build type as the Phase 1 table. For each preset/resolution, record Estimate time with `useDepthMips` off vs on, plus the added `Prefilter Depth` and four `Depth Mip` costs.

| Preset | Res | Mips | Prefilter + 4 mips | Estimate | Resolve/Composite | Total | Delta vs off |
|---|---|---|---|---|---|---|---|
| Medium | 1080p | off | n/a | | | | n/a |
| Medium | 1080p | on | | | | | |
| High | 1080p | off | n/a | | | | n/a |
| High | 1080p | on | | | | | |
| Ultra | 4K | off | n/a | | | | n/a |
| Ultra | 4K | on | | | | | |

Decision rule: depth MIPs trade 5 extra small passes for lower per-tap bandwidth. If total GPU time doesn't drop at High/Ultra (or the saving is within noise), record that here. Consider the compute/SPD alternative (single dispatch for all 5 levels) rather than keeping this raster chain, and tune `depthMipBias` before deciding.

## Known issues / risks
- Compile and runtime status unverified.
- The 5-way `[branch]` chain in `SampleViewDepth` can diverge within a wave when neighbouring pixels pick different mips. This should be mostly coherent (mip depends smoothly on depth), but profile it.
- Odd-sized levels drop the last row/column when halving (same as XeGTAO). There may be a sub-texel offset at coarse levels.
- A 16-bit chain has ~0.06 world-unit steps near 100 m. If AO shimmers at distance, enable `highPrecisionDepthMips`.
- The material keeps references to last frame's `_GTAO_DepthMip*` textures between frames. This is harmless because the shader only reads them under `_GTAO_DEPTH_MIPS` and they are rebound every frame (same pattern as the existing history/motion textures).
- Mip 0 is a full-res R16F/R32F write every frame, even when `downsample` is on.

## Next: Phase 3 - Estimate tuning
Goal: cheaper, better-distributed sampling in `FragEstimate` / `GTAO()`. Gate on Phase 2 measurements (if the chain isn't paying off, fix that first).
1. `GTAO.hlsl`, GTAO loop:
   - Replace `low0 = cos(n + HALF_PI)` and `low1 = cos(n - HALF_PI)` with `-sin(n)` and `sin(n)` (compute `sin(n)` once; it is already needed in `ia0`/`ia1`).
   - Hoist per-slice constants out of the step loop.
2. Replace `Noise()` (IGN + R1) with XeGTAO-style Hilbert-index + R2 spatio-temporal noise (no texture needed). Keep it behind a toggle `GTAONoise { Interleaved, Hilbert }` (default `Hilbert` only if Phase 3 measurements/visuals favour it).
3. `GTAOSettings.cs`:
   - `sampleDistributionPower` (default 2.0 = current hard-coded `s * s`).
   - `thinOccluderCompensation` (default 0, range 0-0.7, XeGTAO's heuristic).
   - Add both, and the noise toggle, to `SanitizeInto`.
4. Verify: with the new settings at defaults and the noise toggle on `Interleaved`, output must match Phase 2 (modulo the sin() change, which is mathematically identical). Re-measure Estimate time per preset.
5. Write `handoff_phase3.md` with the same structure, and Phase 4 (denoiser) entry steps.

## Rollback
`useDepthMips = false` restores Phase 1 rendering at runtime. To remove the code entirely, revert the four files from version control (passes 6-7 are appended, so nothing else is renumbered).
