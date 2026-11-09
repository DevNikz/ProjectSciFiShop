# Phase 3 handoff: enhanced denoiser (opt-in)

Implemented ahead of the Phase 1/2 parity and timing confirmation, at your request. Nothing was compiled, run or profiled in Unity here.

## Done
- New Volume setting **Denoiser Mode** (Legacy default / Enhanced). Legacy output is unchanged, so the parity gate still applies to it.
- Enhanced, in `SSGICore_Temporal.compute`:
  - Neighbourhood firefly rejection (centre excluded, mean + 4 sigma + 50% of mean).
  - Luminance-confidence history: alpha raised (capped at 0.25) when valid history disagrees with the neighbourhood beyond its noise.
  - Disocclusion fill: pixels younger than 4 frames blend toward the 3x3 mean (up to 50%).
  - Variance of young pixels is boosted so the filter smooths them harder.
- Enhanced, in `SSGICore_Filter.compute`:
  - Age-aware luminance tolerance (age read from `HistMom.z`, which the temporal pass now always writes).
  - Iteration 0 writes its filtered colour back into the history colour texture (SVGF-style feedback; age preserved).
- Plumbing: `_SSGI_Enhanced` constant, `AgeIn` / `HistFbOut` ids, pass wiring in `SSGICorePass` (extra bindings only in Enhanced), smoke test for the default.

## Deliberately NOT done
- Jitter-aware reprojection (unjittered matrices for history, jittered for depth). It needs the URP camera API for the unjittered projection
  (`GetProjectionMatrixNoJitter` in URP 17.x, name unverified here), and a wrong guess would break compilation of the whole package.
  Add it once you confirm the accessor exists in your 6.3 install. With motion vectors present the effect is small; the analytic fallback path is where it matters.
- Spatiotemporal blue noise (needs a generated texture asset).
- Merging the moments and geometry history textures (parity-neutral, no need yet).

## Tuning knobs (shader constants, not yet exposed)
Firefly: 4 sigma / 0.5 mean. Confidence: tolerance 3 sigma + 10% of luma, alpha cap 0.25. Fill: 50% max, ages 1-3. Variance boost: +50% per missing age step. Age tolerance: up to 2.5x at age 1.

## Verify
1. Compile; run tests.
2. Legacy: *Parity > Compare With Reference* still PASS.
3. Enhanced: compare Debug View GI and History Age in motion (camera orbit, moving object, light toggle). Look for: fewer fireflies, less noise in disocclusions,
   and any added blur or ghost trails (feedback blur is the main risk; if static areas look soft, tell me and I will lower the feedback or make it a toggle).
4. Timing: Enhanced adds an extra UAV write in filter iteration 0 and an age read per filter pass; paste before/after ms.

## Entry criteria for Phase 4 (ReSTIR-lite)
- Phase 1 parity PASS, Enhanced denoiser judged acceptable (or tuned), timings pasted.

## Fix after first compile
- Unity warned `_SSGI_AgeIn ... is not set` in Legacy mode. The filter pass now always binds `AgeIn` (history moments) and `HistFbOut`
  (history colour for Enhanced iteration 0, this pass's own output as an inert placeholder otherwise).
