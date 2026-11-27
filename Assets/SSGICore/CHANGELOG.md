# Changelog

## Unreleased (performance, phases 0-6)
* New: Light Response (Volume `Light Response`, `Light GI Floor`, `Light Change Flush`; component `SSGICore Light`). A low-resolution compute pass uses URP's Forward+ cluster light loop to compute a per-pixel ratio of GI-scaled to total light (main + point/spot), applied to the radiance a ray picks up. Lights that moved, changed, toggled, spawned or despawned flush temporal history (and ReSTIR reservoirs and radiance-cache cells) in their range. Skipped entirely, at zero cost, while all scales are 1 and nothing changed. Optional: if its shader cannot compile, SSGICore runs without it.
* New: Contact Occlusion / Contact Radius. The trace writes contact openness, the temporal pass accumulates it, the upsample carries it in the GI texture's alpha, and the composite darkens with `Blend One SrcAlpha`. Not computed for ReSTIR (treated as open).
* New: Leak Reduction (default on): thickness no longer grows 5x with distance, grazing hits are rejected. Slightly changes the image versus earlier builds: turn it and Contact Occlusion off to compare for parity.
* Perf: Adaptive Rays (default on). Pixels with long stable history and unchanged lighting trace one ray instead of Ray Count (only matters when Ray Count > 1).
* Change: `_SSGICore_GITexture` alpha is now contact openness (was 1).
* New (experimental, off by default): radiance cache. Volume toggle `Radiance Cache`. A camera-centred 32^3 world grid of ambient cubes of lit scene colour, fed from sparse screen samples, queried by rays that leave the screen. Falls back to the ambient cube where the cache has no data.
* New (experimental, off by default): ReSTIR-lite. Volume toggles `ReSTIR`, `ReSTIR Spatial`, `ReSTIR History Cap`. First-hit samples are resampled temporally (history reservoirs) and spatially (one neighbour), then resolved into the existing temporal + a-trous denoiser. No visibility test on reused samples.
* New: Denoiser Mode (Legacy / Enhanced). Enhanced adds neighbourhood firefly rejection, luminance-confidence history, disocclusion fill, age-aware filter tolerance and filtered-history feedback. Default Legacy.
* Perf: normals + HZB built right after the depth/normals prepass on the async compute queue (DX12/Vulkan); new `Async Compute Prep` feature toggle, automatic inline fallback.
* Perf: normal resolve and HZB level 1 fused into one kernel using a shared depth tile; HZB levels 2-5 built in one dispatch.
* Perf: HZB levels live in one atlas texture (no per-level texture branch chain in the trace).
* Perf: analytic view-position reconstruction (no inverse-projection multiply per call).
* Perf: low-resolution geometry buffer written by the trace and reused by temporal, filter and upsample.
* Perf: groupshared halo tiles in temporal statistics and filter variance prefilter; pow/exp replaced by squaring/exp2.
* Resolved normals are stored packed; Normals / Normal Source debug views decode accordingly.
* Tools: parity capture/compare menu items, `SSGICoreTestHooks.FrameOverride`, new editor tests.

## 0.1.0 (rev 2)
* Fix: composite pass now allows global state modification.
* Fix: per-pixel normal resolve (Auto) for scenes mixing URP Lit and custom-packed DepthNormals; new Normals debug views.

## 0.1.0
* Initial release: HZB ray-march SSGI (mode A), downsampling (1x/2x/4x), IGN dithering, temporal accumulation,
  variance-guided a-trous denoiser, bilateral upsample, multi-bounce (explicit + temporal feedback), Low/Medium/High presets,
  debug views, EditMode/PlayMode tests, demo content builder.
