# Changelog

## Unreleased (performance, phases 0-5)
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
