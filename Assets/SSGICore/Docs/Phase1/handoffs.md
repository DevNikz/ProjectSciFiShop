# Phase 1 handoff: lossless GPU speedups

## Done
- `SSGICore_Common.hlsl`: analytic `ViewPosFromPixel` (constants `_SSGI_RayA/_RayB`); `LoadHzb` reads one atlas via `_SSGI_HzbRects`;
  `PackNormalVS/UnpackNormalVS/UnpackNormalSource`; `LoadLowGeo` reads the new low-res geometry buffer; `Pow8/Pow32`.
  Removed `ViewPosAt` and `ReconstructNormalVS` (now local to the normals kernel, tile based).
- `SSGICore_Normals.compute`: kernel `NormalsAndHzb1` (replaces `NormalResolve` and `HzbFromDepth`). Depth-reconstructed normal is only computed on paths that use it.
- `SSGICore_Hzb.compute`: kernel `HzbDownMulti` builds levels 2-5 in one dispatch (replaces `HzbDown` x4).
- `SSGICore_Trace.compute`: writes `_SSGI_LowGeoOut` (normal + eye, w = 0 for sky).
- `SSGICore_Temporal.compute`, `SSGICore_Filter.compute`: groupshared halo tiles; temporal/filter/upsample use `LoadLowGeo` (no depth or full-res normal binds in temporal/filter).
- `SSGICore_Upsample.compute`: `Pow8`.
- `SSGICore_Composite.shader`: Normals / Normal Source debug views decode the packed format.
- C#: `SSGICoreUtil` (`ComputeHzbAtlas`, `ComputeRayTerms`, `NormalsFormat`, `GroupCount(size, group)`, new IDs), `SSGIFrameConstants` (ray terms, HZB rects),
  `SSGICoreResources` (new kernels), `SSGICorePass` (atlas, packed normals, low-geo buffer, 9..13 dispatches -> 6 + filter iterations).
- Tests: atlas layout, ray terms vs inverse projection, RMSE helper.

## Behaviour changes to be aware of
- Normals are quantised to 10 bits per component (RGBA16F fallback). Eye depth in the low-res geometry buffer is fp16 (same precision the history already used).
- Both should sit inside the 1% parity gate; that is unverified until you run it.

## Not done
- Optional R11G11B10 `giFull` (deferred; only worth it if the full-res GI write shows up in your timings).
- Raw-normal fast path for fully conforming StylizedLit shaders (Auto remains the default).
- Nothing compiled or profiled in Unity here. Possible first-compile snags: HLSL `[unroll]` over `level` in `HzbDownMulti`,
  `GraphicsFormatUsage.LoadStore` namespace, `Vector2Int` out usage.

## Verify
1. Compile; run Editor tests (`SSGICoreQualityTests`) and the PlayMode smoke test.
2. *Parity > Compare With Reference* against the pre-Phase-1 capture: expect PASS (<= 1%).
3. Debug views Normals / Normal Source look the same as before.
4. Fill the timing table in `Docs/Phase0/handoffs.md`.

## Open risks
- Typed UAV load of R32F inside `HzbDownMulti` needs a platform that supports it (all DX11+/Vulkan desktop GPUs, including the 3050).
- Analytic ray terms assume a non-oblique perspective projection (already a README limitation).

## Entry criteria for Phase 2 (async compute split)
- Parity PASS and timings pasted. Then confirm and I start Phase 2.
