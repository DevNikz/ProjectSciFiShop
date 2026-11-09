# Phase 2 handoff: async compute split and early depth prep

Implemented ahead of the Phase 1 parity/timing confirmation, at your request. Nothing was compiled or profiled in Unity here.

## Done
- New `SSGICorePrepPass` (event `AfterRenderingPrePasses`): records Normals+HZB1 and HZB 2-5, with `EnableAsyncCompute(true)`
  when `SystemInfo.supportsAsyncCompute` (DX12/Vulkan). On other APIs the flag is simply not set.
- New `SSGICoreFrameData : ContextItem`: hands `normalsRes`, `hzbAtlas`, size and normal mode from the prep to the main pass.
- `SSGICorePass`:
  - `RecordDepthPrep` (static, shared) records the two prep passes; the main pass calls it inline when the early prep did not run.
  - `FillGeometryConstants` (static, shared) fills view, projection, ray terms, z params, sizes, normal mode, HZB rects.
  - `Constants` exposes the constants object so both passes share it.
- `SSGICoreFeature`: new `Async Compute Prep` toggle (default on). The prep pass is enqueued only when the DepthNormals prepass
  is requested (Normal input) and the main pass event is after the prepasses.

## When the prep runs
Only if, at `AfterRenderingPrePasses`, both `cameraDepthTexture` and `cameraNormalsTexture` are valid, the camera is perspective,
and Normal Source is not ReconstructFromDepth. In every other case nothing changes versus Phase 1 (work is recorded inline, graphics queue).
If the main pass skips the frame (for example history not ready) the prep passes are culled because nothing reads them.

## Verify
1. Frame Debugger / RenderDoc / Nsight: the two prep passes should appear after the prepass and, on DX12/Vulkan,
   run on the compute queue overlapping opaque shading. Switch the Graphics API to DX12 or Vulkan to see the overlap.
2. Parity: *Parity > Compare With Reference* must still PASS (output is identical in principle; only scheduling changed).
3. Toggle `Async Compute Prep` off and confirm identical output.
4. Paste before/after frame times into `Docs/Phase0/handoffs.md`.

## Open risks
- Assumes the depth texture from the DepthNormals prepass is the same depth the main pass sees later. If your renderer copies
  or modifies depth after opaques (extra depth writers, depth copy), HZB would be stale. Turn the toggle off in that case.
- `EnableAsyncCompute` behaviour with depth-format sources in Unity 6.3 is unverified.
- Async overlap only helps if the GPU has graphics work to overlap; on a 3050 mobile at 144 fps the gain may be small. Measure.

## Entry criteria for Phase 3 (enhanced denoiser)
- Phase 1 parity PASS, timings for Phases 1 and 2 pasted. Phase 3 changes the look, behind a Volume toggle.
