# GTAO Optimization: Phase 1 Handoff (Baseline + CPU/GC)

Target: Unity 6.3 (6000.3), URP 17.3, RenderGraph, Forward / Forward+, PC/console.
Status: **implemented, NOT yet compiled or profiled.** The authoring environment had no Unity or C# compiler. Do the entry checks below first.

## Done
- Per-stage `ProfilingSampler`s. Profiler and Frame Debugger now show separate GPU timings:
  `GTAO Estimate`, `GTAO Temporal`, `GTAO Blur H`, `GTAO Resolve`, `GTAO Composite`, `GTAO Debug`.
- `PassInputs` struct replaces the 11-argument `AddPass` signature. Unset handles default to `TextureHandle.nullHandle`.
- `CreateAOTexture` helper replaces the three repeated `CreateRenderGraphTexture` blocks (Phase 2+ will add more callers).
- `GTAOSettings.SanitizeInto(dst)` is the allocation-free clamp-and-copy. `Sanitized()` is kept as a wrapper, so the public API is unchanged.
- `GTAOFeature`:
  - Reuses one `m_Sanitized` instance (no `MemberwiseClone` per camera per frame).
  - The `IsDeferred` reflection now runs once per `Create()` with a cached `FieldInfo`, instead of every camera every frame.
    (An earlier revision of this phase used `UniversalRenderer.renderingModeActual`. That member does not exist in the project's URP and caused CS1061. It was removed in a follow-up fix.)
  - The Deferred warning now logs once (`m_WarnedDeferred`) instead of every frame.

## Files touched
- `Shaders/GTAOPass.cs`
- `Shaders/GTAOFeature.cs`
- `Shaders/GTAOSettings.cs`
- `Docs/handoff_phase1.md` (new)

No shader/HLSL changes. Rendering output should be bit-identical to before.

## New settings and defaults
None.

## Entry checks (do these before Phase 2)
1. Project compiles. (The `renderingModeActual` compile error from the first revision is fixed; the Deferred check is cached reflection on `m_RenderingMode`, which is a private URP field. If a URP update renames it, the check silently disables itself rather than failing.)
2. Output is visually identical to the pre-Phase-1 build (diff a captured frame in Lighting and AfterOpaque modes, with and without `downsample`).
3. Profiler shows the six named GTAO samplers.
4. `GC.Alloc` for the GTAO feature is 0 B per camera per frame in a Player build (Deep Profile off).

## Measurements to capture (fill in; this is the Phase 2+ baseline)
Test scene: ______  Camera path: ______  GPU: ______  Build: Player, not Editor.

| Preset | Res | Downsample | Estimate | Temporal | Blur H | Resolve/Composite | Total | GC B/frame |
|---|---|---|---|---|---|---|---|---|
| Low | 1080p | off | | | | | | |
| Medium | 1080p | off | | | | | | |
| High | 1080p | off | | | | | | |
| Ultra | 1080p | off | | | | | | |
| Medium | 1440p | off/on | | | | | | |
| Medium | 4K | off/on | | | | | | |

Also capture: quality reference screenshots for the same camera positions (High, no downsample), used as the visual baseline for Phases 2-6.

## Known issues / risks
- Compile status unverified (see entry check 1).
- `SanitizeInto` lists fields explicitly. Any new `GTAOSettings` field must be added there too, or it will silently stay at the default in the runtime copy.
- `GTAOPass` ctor still sets `profilingSampler = new ProfilingSampler("GTAO")`. It is now unused by `AddPass` but harmless. It can be removed in a later phase.

## Next: Phase 2 - Depth prefilter + MIP chain
Goal: stop sampling full-res `_CameraDepthTexture` for every AO tap.
1. `GTAO.shader`: append Pass 6 `GTAO_PrefilterDepth` (raw depth to linear view depth, mip0) and Pass 7 `GTAO_DepthMip` (2x2 downsample weighted toward the farthest depth). Do not renumber passes 0-5.
2. `GTAOPass.cs`:
   - Add `PassPrefilterDepth = 6` and `PassDepthMip = 7` consts, and append matching entries to `s_Samplers`.
   - Create a 5-mip transient RT (`useMipMap = true`, `autoGenerateMips = false`). Use R16F with an R32F fallback via `Pick` / `ResolveFormats`.
   - `CreateAOTexture` needs a mip-count parameter for this.
   - Add 1 + 4 raster passes, each writing one mip with `SetRenderAttachment(tex, 0, access, mipLevel)`.
   - Feed the chain to Estimate through `PassInputs` (add a `depthMips` handle).
3. `GTAO.hlsl`:
   - Add `TEXTURE2D_X(_GTAO_DepthMips)` and `SampleViewDepth(uv, mip)`.
   - Use it in `FragEstimate`, `ReconstructNormal` and `DoBlur` instead of `LinDepth` / `SamplePos`.
   - Pick the tap MIP from the pixel offset: `mip = clamp(log2(offsetPx) - bias, 0, 4)`.
4. `GTAOSettings.cs`: add `useDepthMips` (default true, A/B toggle) and `depthMipBias` (default 3.3, range 0-6). Add both to `SanitizeInto`.
5. Keep the AO RT layout `(ao, depth)` in this phase (layout change is Phase 4a).
6. Verify: the `useDepthMips=false` path matches the Phase 1 output exactly. Re-measure the table above and record the Estimate-time delta versus the added prefilter-pass cost. If total GPU time doesn't improve at High/Ultra, report it in `handoff_phase2.md` rather than hiding it.

## Rollback
Phase 1 is pure refactor plus allocation fixes. Revert the three files from version control.
