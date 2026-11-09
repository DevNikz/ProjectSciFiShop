# Phase 0 handoff: baseline and parity harness

## Done
- `SSGICoreTestHooks.FrameOverride` (Runtime/SSGICoreUtil.cs): fixes the noise frame index; `SSGICorePass` honours it.
- `SSGICoreParity.RelativeRmse` + gate constant (1%).
- `Editor/SSGICoreParityTool.cs`: menu *Tools > SSGICore > Parity > Capture Reference / Compare With Reference*.
  Renders the main camera 3 times (history needs a prior frame), GI debug view, temporal off, 1920x1080, ARGBHalf.
- Editor test for the RMSE helper.

## Not done / needs you
- No GPU timings exist yet. Nothing was compiled or run in Unity here.
- **Capture the reference on the pre-Phase-1 build** (original zip) before importing Phase 1, otherwise there is nothing to compare against.

## Capture protocol (paste results below)
1. Static scene, plugged in, fixed power profile, HDR on in the URP asset, Game view at 1080p internal.
2. Parity: Play mode, Capture Reference (baseline build), import Phase 1, Compare With Reference.
3. Timings: Unity Profiler GPU module, Nsight Graphics, or RenderDoc. Record per pass ms (Medium preset):
   | Pass | Baseline ms | After Phase 1 ms |
   |---|---|---|
   | Normals (+HZB1) | | |
   | HZB (5 passes / 1 pass) | | |
   | Trace | | |
   | Temporal | | |
   | Filter (all iterations) | | |
   | Upsample | | |
   | Composite | | |

## Open risks
- `Camera.Render()` capture path in URP 17.x is unverified; if it warns, capture via the Game view instead.
- Dynamic content in the scene will fail the gate spuriously.

## Entry criteria for Phase 1
- Baseline reference captured.
