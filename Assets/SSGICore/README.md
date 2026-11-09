# SSGICore 0.1.0

Screen-space global illumination for **Unity 6.3 and 6.5+**, **URP Forward+**, Render Graph only.

## Install
1. Import `SSGICore_v0.1.0.unitypackage` (requires `com.unity.render-pipelines.universal` 17.x).
2. Select your Forward+ **Universal Renderer** asset -> *Add Renderer Feature* -> **SSGICore Feature**.
3. Add a global **Volume** and add the override **Lighting > SSGICore**; tick *Enable*.
4. Optional: *Tools > SSGICore > Create Demo Content* builds a test room + Volume.

The feature requests Depth, Normals (DepthNormals prepass), Motion Vectors and the Opaque texture itself.

## Pipeline
Normals + HZB level 1 -> HZB levels 2-5 (single atlas) -> low-res dithered ray march (1-3 bounces) -> temporal accumulation (motion-vector
reprojection, depth/normal rejection, variance clipping) -> variance-guided a-trous filter -> depth/normal-aware
bilateral upsample -> additive composite.

## Performance notes
* With the DepthNormals prepass active, normals and the HZB are built right after the prepass (async compute on DX12/Vulkan)
  so they overlap opaque shading. Disable via the feature's *Async Compute Prep* toggle if you modify depth after the prepass.
* Resolved normals are packed (R10G10B10A2 where UAV stores are supported, RGBA16F otherwise).
* The trace writes a low-resolution geometry buffer (view normal + eye depth) that the temporal, filter and upsample
  kernels read instead of rebuilding positions from full-resolution depth.
* Parity tooling: in Play mode use *Tools > SSGICore > Parity > Capture Reference / Compare With Reference* (static
  scene, HDR enabled). The gate is a relative luminance RMSE of 1%.

## Settings (Volume)
* **Quality**: Low (1/4 res), Medium (1/2 res), High (1/2 res, 2 rays), Custom.
* **Lighting Source**: Current frame, Previous frame (free extra bounces by feedback), or Blended.
* **Denoiser Mode**: Legacy (default) or Enhanced (extra temporal/filter stabilisation, see CHANGELOG).
* **Radiance Cache (experimental)**: off-screen fallback for rays that leave the screen; see Docs/Phase5/handoffs.md.
* **ReSTIR (experimental)**: resamples first-hit samples across frames/neighbours. Off by default; see Docs/Phase4/handoffs.md.
* **Bounces**: extra explicit bounces inside the trace (albedo is approximated).
* **Debug View**: GI, Raw Trace, History Age.
* **Normal Source**: Auto (default) decides per pixel between raw, octahedral and depth-reconstructed normals, so objects
  whose shaders pack DepthNormals differently all work. Raw / Octahedral / Reconstruct force one mode.
* **Debug View**: also Normals and Normal Source (red = reconstructed, green = raw, blue = octahedral).

## Forward+ albedo note
Forward+ has no albedo buffer. The built-in composite estimates albedo from scene-colour chroma x *Albedo Level*.
For exact results in your own shaders include `Shaders/SSGICoreSampling.hlsl` and use
`SSGICoreSampleGI(screenUV) * albedo`. The texture is exposed as global `_SSGICore_GITexture` after the composite.

## Custom shaders
Objects must be written by the DepthNormals pass to appear in depth/normals. In a hand-written shader add
`UsePass "Universal Render Pipeline/Lit/DepthNormals"` (or implement a `DepthNormals` pass using URP's `DepthNormalsPass.hlsl`).
Objects with no such pass cannot be fixed by SSGICore.

## Known limitations (v0.1.0)
* Compute-shader path only; the fragment fallback (phase 7) is not implemented, unsupported devices skip the effect.
* Perspective cameras only. Transparents do not contribute or receive GI.
* Modes B (GTAO-style) and C (probe fallback for off-screen hits) are planned, not implemented. Off-screen rays use the ambient probe.
* Written against the URP 17.x Render Graph API; not compiled against 6.5 in CI yet. Please report API drift.
