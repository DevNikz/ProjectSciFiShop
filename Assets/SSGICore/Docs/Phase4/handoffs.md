# Phase 4 handoff: ReSTIR-lite (experimental, opt-in)

Implemented ahead of the Phase 1-3 parity/timing confirmation, at your request. Nothing was compiled, run or profiled in Unity here.
Treat this as the least verified phase: new math, new history textures, no reference to compare against.

## Done
- Volume: `ReSTIR` (off by default), `ReSTIR Spatial` (on), `ReSTIR History Cap` (20).
- `SSGICore_Restir.hlsl`: reservoir struct, packing helpers, target pdf, update/weight functions, derivation notes.
- `SSGICore_Trace.compute`: `TracePathEx` (reports the first-hit position/normal; legacy `TracePath` wraps it, so the legacy kernel is unchanged),
  new kernel `TraceRestir` = RIS over the pixel's rays + temporal reuse from the previous frame's reservoir.
- `SSGICore_Restir.compute`: kernel `RestirSpatial` = one-neighbour spatial reuse (radius 6 low-res px, geometry gate, Jacobian in [0.1, 10]) + resolve into
  the existing `trace` texture, so temporal accumulation, a-trous and upsample are untouched.
- `SSGICoreRestirHistory`: per-camera double-buffered reservoirs (A: position + M RGBA32F, B: radiance + W, C: normal, both RGBA16F).
- `SSGICorePass`: acquires the history, falls back to the plain trace if unavailable, wires the two passes, marks the history written.
- Resources/ids/constants/tests updated.

## Estimator (see header of SSGICore_Restir.hlsl)
Output per pixel stays (1/pi) * integral(Lo cos), the same quantity the plain trace averages. Candidate weight = luminance(Lo);
final value = Lo * cos / pi * W, W = wsum / (M * phat). Stored reservoirs are the pre-spatial ones.

## Known limitations / bias
- No visibility test on reused samples: spatial (and stale temporal) samples can leak light through thin occluders. Turn `ReSTIR Spatial` off to reduce this.
- No MIS (biased combine); temporal reuse assumes a static receiver (moving receivers are rejected by the geometry check).
- Miss samples are stored as a far point along the ray (MaxDistance), so ambient/sky samples reuse reasonably but not exactly.
- Sample radiance is whatever was lit when traced (stale for changing lighting, bounded by the history cap).
- With `Ray Count` > 1 the candidates are resampled (one survives), not averaged; quality then relies on reuse.
- Checkerboard tracing and a reservoir-confidence debug view from the plan are NOT implemented.

## Cost warning (unmeasured)
Extra bandwidth at trace resolution: Trace writes 3 reservoir textures and reads 3 from the previous frame; Spatial reads up to 6 more taps.
At 1080p half-res that is roughly tens of MB per frame. On a 3050 mobile at 144 fps it may cost more than it saves unless you also cut
`Filter Iterations` (reuse is meant to replace some filtering) and rays. Measure before keeping it on.

## Verify
1. Compile; run tests.
2. ReSTIR off: output must equal Phase 3 (legacy kernels untouched). Parity gate still applies.
3. ReSTIR on, static camera: image should converge faster than plain trace at equal ray count; check for banding or blotches (weight explosions).
4. Moving camera / moving object: look for ghost trails and light leaking; compare `ReSTIR Spatial` on/off and `History Cap` 4 vs 20.
5. Paste timings for off / on / on with fewer filter iterations.

## Open risks
- HLSL: `(SSGIResAcc)0` zero-cast and `isfinite` on vectors are standard but unverified on every target compiler.
- Reservoir A uses 32-bit floats (16 B/pixel) because world positions do not survive fp16.
- Dispatch passes bind 15 textures in the ReSTIR trace; if Unity reports an unset property, send me the name.

## Entry criteria for Phase 5 (off-screen radiance cache)
- ReSTIR judged worth keeping (or left off) with timings pasted.
