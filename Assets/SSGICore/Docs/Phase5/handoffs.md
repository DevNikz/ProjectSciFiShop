# Phase 5 handoff: off-screen radiance cache (experimental, opt-in)

Implemented ahead of the Phase 1-4 parity/timing confirmation, at your request. Nothing was compiled, run or profiled in Unity here.
This phase touches the most new API surface (3D history texture, imported GraphicsBuffer, atomics).

## Design (option chosen from the plan: camera-centred world cache fed from the screen)
- Grid: 32^3 cells, cell size = Max Distance / 12 (grid half-extent about 1.33 x Max Distance), re-based on the camera each frame.
  Not toroidal: history is shifted by whole cells in the resolve, so hardware trilinear needs no seam handling.
- Content: per cell an 'ambient cube' of lit scene colour (6 faces, premultiplied rgb + weight), stored in one RGBA16F 3D texture (faces packed along x, 192 x 32 x 32), double buffered.
- `CacheInject` (`SSGICore_Cache.compute`): one screen sample per 8x8 block (position rotates over 64 frames), adds (colour * w, w) to the faces its normal points at,
  with fixed-point `InterlockedAdd` into a structured uint buffer (about 3 MB).
- `CacheResolve`: per cell/face, `prev * 0.97 + acc`, weight capped at 32, also drains (zeroes) the accumulation buffer.
- Query (`SSGICore_Trace.compute`, `FallbackRadianceAt`): for a ray with no on-screen hit (or fading at the screen edge) it looks up 3 points along the ray
  (15%, 40%, 100% of Max Distance), using the faces that face back along the ray; the first confident sample wins, the remainder is the ambient cube.
  With the cache disabled the function returns exactly the old ambient fallback.
- Both trace kernels (plain and ReSTIR) use it. The cache texture is always bound (a 1x1x1 placeholder when off) to avoid Unity's unset-property warnings.

## Files
- New: `SSGICore_Cache.compute`, `SSGICoreCacheHistory.cs`.
- Changed: `SSGICore_Common.hlsl` (constants), `SSGICore_Trace.compute` (cache query), Volume (`Radiance Cache`), `SSGICoreFrameConstants`, `SSGICoreUtil` (ids),
  `SSGICoreResources` (kernels, placeholder volume), `SSGICorePass` (history, cache passes, `StepData` buffer + 3D dispatch support), smoke tests.

## Limitations
- The cache stores the opaque camera colour (direct-lit scene, without the GI composite), so it feeds off-screen surfaces' direct lighting, not their indirect light.
- Coarse (cell size about 1.7 m at the default Max Distance), no visibility inside cells, so it can leak and is low-frequency.
- Only surfaces seen in about the last second or so are known; the cache forgets with a 0.97 per-frame decay.
- Camera-centred: dynamic objects are sampled like everything else, with no motion handling beyond the decay.
- Debug view for the cache confidence from the plan is NOT implemented.

## Verify
1. Compile; run tests. Watch the console for the one-time warning about the 3D history texture (then the cache silently stays off).
2. Cache off: output must equal Phase 4 (parity gate applies).
3. Cache on: look at a corner where a bright wall is just off-screen; rotate the camera away and back. Rays leaving the screen toward it should now return its colour
   instead of the ambient cube. Check for light blobs that follow the camera, or stale colour after lights change.
4. Frame Debugger / Nsight: two extra compute passes (Cache Inject, Cache Resolve) before Trace. Paste their ms.

## Open risks (API assumptions, unverified)
- `AllocHistoryFrameRT` with a Tex3D descriptor (volumeDepth) is assumed to work. If it does not, the cache disables itself with a warning.
- `RenderGraph.ImportBuffer` for a persistent GraphicsBuffer, `UseBuffer`, and `ComputeCommandBuffer.SetComputeBufferParam(BufferHandle)` are assumed to exist in 6.3 as in 6.0.
- `RTHandles.Alloc(... dimension: Tex3D ...)` for the placeholder volume; if allocation fails SSGICore reports 'Could not allocate the 3D placeholder texture' and does not run.
- The accumulation buffer is zeroed once at creation by `SetData`, then drained by the resolve kernel; if a frame skips the resolve after an inject, stale counts add to the next frame (harmless, decays).
- Fixed-point range: 32-bit uints at scale 1024 allow about 4 million units per cell face per frame; extreme HDR colours are clamped by `ClampLuma` first.

## Entry criteria for Phase 6 (presets, tests, final docs)
- Parity PASS for all toggles off, timings pasted for each opt-in feature, decision on which stay in presets.
