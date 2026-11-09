# GTAO Optimization: Phase 3 Handoff (Estimate tuning)

Target: Unity 6.3 (6000.3), URP 17.3, RenderGraph, Forward / Forward+, PC/console.
Status: **implemented, NOT yet compiled, run or profiled** (no Unity in the authoring environment). The Phase 1 and Phase 2 entry checks (`handoff_phase1.md`, `handoff_phase2.md`) have still not been confirmed by the project owner, so this phase stacks on unverified work.

## Done (all in `FragEstimate` / `GTAO()` in `GTAO.hlsl`)
- **Analytic horizon floor:** `low0/low1 = cos(n ± π/2)` replaced by `-sin(n)` / `sin(n)`. `sin(n)` is computed once per slice and reused in the integral (`ia0`/`ia1`), which previously called `sin(n)` again.
- **Hoisted / cheaper per-tap math:**
  - `1/slices` and `1/steps` precomputed (no per-slice/per-step divides).
  - `length` + two divides per tap replaced by one `dot` + `rsqrt` (`il0`, `il1`) and multiplies.
- **`sampleDistributionPower`:** tap distribution exponent. A uniform-branch fast path keeps `s*s` when the value is 2 (the previous hard-coded behaviour), so the default has no extra cost.
- **`thinOccluderCompensation`** (XeGTAO): `hc = c > hc ? c : lerp(hc, c, thin)`. At 0 this is exactly the old `max(hc, c)`.
- **`GTAONoise { Interleaved, Hilbert }`:** `Hilbert` = XeGTAO's Hilbert-curve index (64×64 tile, computed with bit ops, no LUT texture) + R2 sequence, indexed by `frame & 63`. `Interleaved` is the original IGN + R1.
- New shader constant `_GTAO_Tune` (x distribution power, y thin-occluder, z noise mode). No new shader keywords or variants (all runtime uniform branches).

## Files touched
`Shaders/GTAO.hlsl`, `Shaders/GTAOPass.cs` (`IDs.Tune` + `UpdateMaterial`), `Shaders/GTAOSettings.cs`, `Docs/handoff_phase3.md` (new). `GTAO.shader` is unchanged.

## New settings (new "Sampling" header; all in `SanitizeInto`)
| Field | Default | Range | Notes |
|---|---|---|---|
| `sampleDistributionPower` | `2` | 1-3 | 2 = previous behaviour |
| `thinOccluderCompensation` | `0` | 0-0.7 | 0 = previous behaviour |
| `noise` | `Interleaved` | enum | Original noise kept as default; see below |

At defaults the output is mathematically identical to Phase 2. Expect only float rounding differences from the `rsqrt` change, not visible artifacts.

## Entry checks (before Phase 4)
1. Project compiles. The shader is the main risk: the `[branch] if (...) s = s * s; else s = pow(...)` statement, `HilbertIndex` uint/shift code, and the `bool distQuadratic` local.
2. At defaults, a captured frame matches Phase 2 (diff the AO debug view; expect ≤ 1/255 noise-level differences, no structure).
3. Toggle `noise = Hilbert`: compare with temporal on and off, and with blur Off/Low/Medium. Look for visible tiling or a 64-pixel pattern in the raw AO (blur Off, temporal off) and for flicker with temporal on.
4. Sweep `sampleDistributionPower` 1 → 3 and `thinOccluderCompensation` 0 → 0.7 on a scene with railings, foliage and fences. Thin compensation should lighten the AO behind thin geometry without lifting contact darkening elsewhere.
5. GC alloc is still 0 B per frame.

## Measurements to capture
Same scene, camera path, build type and table layout as Phases 1-2. Record Estimate time at each preset for: Phase 2 build, Phase 3 defaults (should be equal or slightly faster), and `noise = Hilbert`. The `rsqrt`/`sin` changes are small; if Estimate time is within noise, say so rather than claiming a win.

**Decision to record:** whether to make `Hilbert` the default. Criteria: lower perceived noise after the denoiser at equal sample counts, no visible tiling, no measurable extra cost. I left the default at `Interleaved` because I couldn't evaluate it visually.

## Known issues / risks
- Compile and runtime status unverified.
- `thinOccluderCompensation` > 0 makes the horizon non-monotonic, so it can lighten legitimate contact AO on thick geometry if set high. Keep it small unless thin occluders are the issue.
- `Hilbert` noise has a 64×64 spatial period. With very low sample counts and no denoiser it may show more structure than IGN.
- `sampleDistributionPower` ≠ 2 takes the `pow` path. That's a few extra ALU ops per tap, negligible but nonzero.

## Next: Phase 4 - Denoiser (spatial + temporal)
Goal: replace the Gaussian-with-`exp()` separable blur and strengthen the temporal filter. Read this phase's plan in the original proposal; the concrete steps below reflect the current code.

Decision to make first: **Phase 4 needs per-pixel linear depth at AO resolution without stuffing it into the AO texture.** The plan was to move depth to the Phase 2 mip chain (AO-res depth = mip `log2(downsample)`). That makes the chain a hard dependency of the new denoiser. Either (a) force the prefilter on whenever `denoiser = Atrous` (even if `useDepthMips` is off for AO taps) and add a light `needsDepthChain` flag, or (b) keep depth in the AO texture's G channel for now and defer the layout change. If Phase 2 measurements showed the chain is a net loss at Low/Medium, choose (b) and note it in `handoff_phase4.md`.

**4a. Spatial (à-trous)**
1. `GTAO.shader`: append Pass 8 `GTAO_Atrous` (5-tap sparse B3-spline per axis or 5×5, step `2^i` passed via a constant). Keep the existing blur passes (indices 2-3) so `GTAODenoiser.Legacy` can still select them.
2. `GTAO.hlsl`: add `DepthPlaneWeight(centerDepth, centerNormal, sampleDepth, sampleUV, ...)` (plane-distance weight), `PackEdges` / `UnpackEdges` (if edges are stored in the AO texture), and `FragAtrous`.
3. `GTAOPass.cs`: add `PassAtrous = 8` + `s_Samplers` entry; add ping-pong RTs via `CreateAOTexture`; map the existing `blur` enum to iterations (Off=0, Low=1, Medium=2, High=3) via a new `GetAtrousIterations()` on `GTAOSettings`; make the last iteration do the upsample for `downsample` (Phase 5 will replace its filter).
4. `GTAOSettings.cs`: `enum GTAODenoiser { Legacy, Atrous }`, new `denoiser` field defaulting to `Atrous`, added to `SanitizeInto`.

**4b. Temporal**
1. `FragTemporal`: age-based adaptive alpha, 4-tap depth-validated history bilinear (replacing the single linear tap), `DepthPlaneWeight` rejection. Keep the existing 3×3 variance clamp and `NaN` guard.
2. History format: RGBA16F `(ao, depth, age, unused)` via `Pick` (fallback to the current RG16F with age disabled). Plan the extra memory/bandwidth cost in `handoff_phase4.md`.
3. Use `cam.GetProjectionMatrixNoJitter()` (or `UniversalCameraData.GetProjectionMatrixNoJitter`) for `prevProj` / `UpdateMaterial` so URP TAA jitter doesn't corrupt reprojection. Verify against URP 17.3 API.
4. `GTAOSettings.cs`: `minHistoryAlpha` (default so that the converged feel matches the current `temporalWeight`).

Verify: `denoiser = Legacy` reproduces Phase 3 output; Atrous reduces noise at equal cost or better at Low/Medium; no halos at depth edges; no ghosting on moving objects.

## Rollback
All Phase 3 settings default to the previous behaviour, so nothing needs reverting at runtime. To remove the code, revert the three files from version control (no shader pass or keyword changes were made in this phase).
