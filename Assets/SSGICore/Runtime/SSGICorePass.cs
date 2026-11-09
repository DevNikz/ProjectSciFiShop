using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SSGICore
{
    /// <summary>
    /// Records the whole SSGICore pipeline into the URP render graph:
    /// normals + HZB level 1 -> HZB levels 2-5 -> trace (low-res) -> temporal accumulate -> a-trous filter -> bilateral upsample -> composite.
    /// </summary>
    internal sealed class SSGICorePass : ScriptableRenderPass
    {
        // ---- pass data ------------------------------------------------------------------------------------

        sealed class StepData
        {
            public ComputeShader cs;
            public int kernel, groupsX, groupsY, groupsZ, stepSize, count;
            public int bufferId;
            public BufferHandle buffer;
            public bool hasBuffer;
            public SSGIFrameConstants constants;
            public readonly int[] ids = new int[16];
            public readonly TextureHandle[] tex = new TextureHandle[16];

            public void Begin(ComputeShader shader, int kernelIndex, int width, int height, SSGIFrameConstants c, int step)
            {
                cs = shader;
                kernel = kernelIndex;
                groupsX = SSGICoreUtil.GroupCount(width);
                groupsY = SSGICoreUtil.GroupCount(height);
                groupsZ = 1;
                constants = c;
                stepSize = step;
                count = 0;
                hasBuffer = false;
            }

            public void BeginGroups(ComputeShader shader, int kernelIndex, int groupsInX, int groupsInY, SSGIFrameConstants c, int step)
            {
                cs = shader;
                kernel = kernelIndex;
                groupsX = groupsInX;
                groupsY = groupsInY;
                groupsZ = 1;
                constants = c;
                stepSize = step;
                count = 0;
                hasBuffer = false;
            }

            public void Add(int id, TextureHandle handle)
            {
                ids[count] = id;
                tex[count] = handle;
                count++;
            }

            public void AddBuffer(int id, BufferHandle handle)
            {
                bufferId = id;
                buffer = handle;
                hasBuffer = true;
            }

            public void Execute(ComputeCommandBuffer cmd)
            {
                constants.Apply(cmd, cs);
                cmd.SetComputeIntParam(cs, SSGICoreIds.StepSize, stepSize);
                for (int i = 0; i < count; i++)
                    cmd.SetComputeTextureParam(cs, kernel, ids[i], tex[i]);
                if (hasBuffer)
                    cmd.SetComputeBufferParam(cs, kernel, bufferId, buffer);
                cmd.DispatchCompute(cs, kernel, groupsX, groupsY, groupsZ);
            }
        }

        sealed class CompositeData
        {
            public TextureHandle gi, opaque, depth, raw, age, normals;
            public Material material;
            public MaterialPropertyBlock block;
            public int passIndex;
            public Vector4 prm, size;
        }

        // ---- state ----------------------------------------------------------------------------------------

        SSGICoreVolume m_Volume;
        readonly SSGIFrameConstants m_C = new SSGIFrameConstants();
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();

        static readonly Vector3[] s_AmbientDirs =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };
        static readonly Color[] s_AmbientColors = new Color[6];

        public void Setup(SSGICoreVolume volume)
        {
            m_Volume = volume;
        }

        static TextureHandle CreateUav(RenderGraph rg, int w, int h, GraphicsFormat format, string name)
        {
            var desc = new TextureDesc(w, h)
            {
                colorFormat = format,
                enableRandomWrite = true,
                clearBuffer = false,
                name = name,
            };
            return rg.CreateTexture(desc);
        }

        internal SSGIFrameConstants Constants => m_C;

        /// <summary>Camera-geometry constants shared by the depth prep (normals + HZB) and the main pass.</summary>
        internal static Vector2Int FillGeometryConstants(SSGIFrameConstants c, UniversalCameraData cameraData, int fullW, int fullH, int normalMode)
        {
            Camera camera = cameraData.camera;
            Matrix4x4 view = cameraData.GetViewMatrix(0);
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(0), true);
            c.view = view;
            c.proj = proj;
            c.invProj = proj.inverse;
            c.zParams = SSGICoreUtil.ComputeZParams(camera.nearClipPlane, camera.farClipPlane, SystemInfo.usesReversedZBuffer);
            c.fullSize = new Vector4(fullW, fullH, 1f / fullW, 1f / fullH);
            c.normalMode = normalMode;
            SSGICoreUtil.ComputeRayTerms(c.invProj, out c.rayA, out c.rayB);
            return SSGICoreUtil.ComputeHzbAtlas(fullW, fullH, c.hzbRects);
        }

        /// <summary>
        /// Records normals + HZB level 1 and HZB levels 2-5. Depends only on depth (and DepthNormals), so it can run
        /// early on the async compute queue (see SSGICorePrepPass) or inline in the main pass.
        /// </summary>
        internal static void RecordDepthPrep(RenderGraph renderGraph, SSGIFrameConstants c, TextureHandle depth, TextureHandle normalsIn,
            int fullW, int fullH, Vector2Int hzbAtlasSize, bool asyncCompute, out TextureHandle normalsRes, out TextureHandle hzbAtlas)
        {
            hzbAtlas = CreateUav(renderGraph, hzbAtlasSize.x, hzbAtlasSize.y, GraphicsFormat.R32_SFloat, "SSGICore_HzbAtlas");
            normalsRes = CreateUav(renderGraph, fullW, fullH, SSGICoreUtil.NormalsFormat, "SSGICore_Normals");

            // Normals (per-pixel choice of raw / octahedral / depth-reconstructed) + HZB level 1
            using (var b = renderGraph.AddComputePass("SSGICore Normals+HZB1", out StepData d))
            {
                d.Begin(SSGICoreResources.Normals, SSGICoreResources.KNormalsHzb1, fullW, fullH, c, 0);
                d.Add(SSGICoreIds.Depth, depth);            b.UseTexture(depth, AccessFlags.Read);
                d.Add(SSGICoreIds.Normals, normalsIn);      b.UseTexture(normalsIn, AccessFlags.Read);
                d.Add(SSGICoreIds.NormalsOut, normalsRes);  b.UseTexture(normalsRes, AccessFlags.Write);
                d.Add(SSGICoreIds.HzbOut, hzbAtlas);        b.UseTexture(hzbAtlas, AccessFlags.Write);
                if (asyncCompute) b.EnableAsyncCompute(true);
                b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
            }

            // HZB levels 2..5 (one dispatch; each 16x16 group reduces a block of level-1 texels)
            using (var b = renderGraph.AddComputePass("SSGICore HZB 2-5", out StepData d))
            {
                Vector4 level1 = c.hzbRects[1];
                d.BeginGroups(SSGICoreResources.Hzb, SSGICoreResources.KHzbDownMulti,
                    SSGICoreUtil.GroupCount((int)level1.z, 16), SSGICoreUtil.GroupCount((int)level1.w, 16), c, 0);
                d.Add(SSGICoreIds.HzbOut, hzbAtlas);    b.UseTexture(hzbAtlas, AccessFlags.ReadWrite);
                if (asyncCompute) b.EnableAsyncCompute(true);
                b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
            }
        }

        // ---- record ---------------------------------------------------------------------------------------

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            SSGICoreVolume vol = m_Volume;
            if (vol == null)
                return;

            if (!SSGICoreResources.TryLoad(out string error))
            {
                SSGICoreLog.WarnOnce("resources", error);
                return;
            }

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            Camera camera = cameraData.camera;

            if (camera.orthographic)
            {
                SSGICoreLog.WarnOnce("ortho", "Orthographic cameras are not supported; SSGICore is skipped for them.");
                return;
            }
            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle depth = resourceData.cameraDepthTexture;
            TextureHandle opaque = resourceData.cameraOpaqueTexture;
            if (!depth.IsValid() || !opaque.IsValid())
            {
                SSGICoreLog.WarnOnce("inputs", "Camera depth or opaque texture is unavailable at this pass event; SSGICore is skipped.");
                return;
            }

            TextureHandle normals = resourceData.cameraNormalsTexture;
            TextureHandle motion = resourceData.motionVectorColor;
            bool hasNormals = normals.IsValid() && vol.normalSource.value != SSGICoreNormalSource.ReconstructFromDepth;
            bool hasMotion = motion.IsValid();

            UniversalCameraHistory historyManager = cameraData.historyManager;
            if (historyManager == null)
                return;

            historyManager.RequestAccess<SSGICoreHistory>();
            SSGICoreHistory hist = historyManager.GetHistoryForWrite<SSGICoreHistory>();
            if (hist == null)
                return; // history becomes available next frame

            SSGICoreSettings cfg = SSGICoreQuality.Resolve(vol);

            // previous-frame colour (bounce feedback)
            RTHandle prevColorRT = null;
            if (vol.lightingSource.value != SSGICoreLightingSource.CurrentFrame)
            {
                historyManager.RequestAccess<RawColorHistory>();
                RawColorHistory rawHistory = historyManager.GetHistoryForRead<RawColorHistory>();
                prevColorRT = rawHistory?.GetPreviousTexture(0);
                if (prevColorRT != null && prevColorRT.rt == null)
                    prevColorRT = null;
            }
            bool usePrevColor = prevColorRT != null;

            // ---- sizes -----------------------------------------------------------------------------------
            RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
            int fullW = camDesc.width;
            int fullH = camDesc.height;
            int ds = Mathf.Clamp(cfg.downsample, 1, 4);
            int lowW = SSGICoreUtil.LowSize(fullW, ds);
            int lowH = SSGICoreUtil.LowSize(fullH, ds);

            hist.Update(lowW, lowH);
            bool historyValid = cfg.temporal && hist.IsPreviousValid(Time.frameCount);

            // ReSTIR-lite reservoir history (falls back to the plain trace when unavailable this frame)
            SSGICoreRestirHistory restirHist = null;
            if (vol.restir.value)
            {
                historyManager.RequestAccess<SSGICoreRestirHistory>();
                restirHist = historyManager.GetHistoryForWrite<SSGICoreRestirHistory>();
                if (restirHist != null)
                    restirHist.Update(lowW, lowH);
            }
            bool restirActive = restirHist != null;
            bool restirHistoryValid = restirActive && historyValid && restirHist.IsPreviousValid(Time.frameCount);

            // Radiance cache (falls back to the ambient cube only when its 3D history cannot be created)
            SSGICoreCacheHistory cacheHist = null;
            if (vol.radianceCache.value)
            {
                historyManager.RequestAccess<SSGICoreCacheHistory>();
                cacheHist = historyManager.GetHistoryForWrite<SSGICoreCacheHistory>();
                if (cacheHist != null && !cacheHist.TryUpdate())
                {
                    SSGICoreLog.WarnOnce("cache", "The radiance cache needs a 3D history texture that could not be created; it is disabled.");
                    cacheHist = null;
                }
            }
            bool cacheActive = cacheHist != null;
            float cacheCell = Mathf.Max(vol.maxDistance.value, 1f) / 12f; // grid half-extent = 16 cells = 1.33 x Max Distance
            Vector3Int cacheBase = Vector3Int.zero;
            Vector3Int cacheDelta = Vector3Int.zero;
            bool cacheShiftValid = false;
            if (cacheActive)
            {
                Vector3 camPos = cameraData.camera.transform.position;
                cacheBase = new Vector3Int(
                    Mathf.FloorToInt(camPos.x / cacheCell) - SSGICoreCacheHistory.GridSize / 2,
                    Mathf.FloorToInt(camPos.y / cacheCell) - SSGICoreCacheHistory.GridSize / 2,
                    Mathf.FloorToInt(camPos.z / cacheCell) - SSGICoreCacheHistory.GridSize / 2);
                cacheShiftValid = cacheHist.TryGetShift(Time.frameCount, cacheCell, cacheBase, out cacheDelta);
            }

            // ---- constants -------------------------------------------------------------------------------
            int normalMode = hasNormals ? (int)vol.normalSource.value : (int)SSGICoreNormalSource.ReconstructFromDepth;
            SSGIFrameConstants c = m_C;
            Vector2Int hzbAtlasSize = FillGeometryConstants(c, cameraData, fullW, fullH, normalMode);
            Matrix4x4 view = c.view;
            Matrix4x4 viewProj = c.proj * view;
            c.invView = view.inverse;
            c.prevView = historyValid ? hist.PrevView : view;
            c.prevViewProj = historyValid ? hist.PrevViewProj : viewProj;
            c.lowSize = new Vector4(lowW, lowH, 1f / lowW, 1f / lowH);
            c.near = camera.nearClipPlane;
            c.thickness = vol.thickness.value;
            c.maxDistance = vol.maxDistance.value;
            c.edgeFade = vol.edgeFade.value;
            c.prevWeight = vol.lightingSource.value == SSGICoreLightingSource.PreviousFrame ? 1f : vol.previousFrameWeight.value;
            c.firefly = vol.fireflyClamp.value;
            c.motionReject = vol.motionRejection.value;
            c.fallbackIntensity = vol.fallbackIntensity.value;
            c.albedo = vol.albedoLevel.value;
            c.temporalClamp = vol.temporalClamp.value;
            c.downsample = ds;
            c.rayCount = cfg.rayCount;
            c.maxSteps = cfg.maxSteps;
            c.bounces = cfg.bounces;
            c.frame = (SSGICoreTestHooks.FrameOverride >= 0 ? SSGICoreTestHooks.FrameOverride : Time.frameCount) & 0xFFFF;
            c.usePrevColor = usePrevColor ? 1 : 0;
            c.historyValid = historyValid ? 1 : 0;
            c.maxAge = cfg.maxAge;
            c.hasMotion = hasMotion ? 1 : 0;
            bool enhanced = vol.denoiserMode.value == SSGICoreDenoiserMode.Enhanced;
            c.enhanced = enhanced ? 1 : 0;
            c.resValid = restirHistoryValid ? 1 : 0;
            c.cacheEnabled = cacheActive ? 1 : 0;
            c.cacheBase = new Vector4(cacheBase.x, cacheBase.y, cacheBase.z, cacheCell);
            c.cacheDelta = new Vector4(cacheDelta.x, cacheDelta.y, cacheDelta.z, cacheShiftValid ? 1f : 0f);
            c.resMaxM = vol.restirHistoryCap.value;
            c.resSpatial = vol.restirSpatial.value ? 1 : 0;
            FillAmbient(c.ambient, 1f);

            // ---- resources -------------------------------------------------------------------------------
            TextureHandle black = renderGraph.defaultResources.blackTexture;
            TextureHandle normalsIn = hasNormals ? normals : black;
            TextureHandle motionIn = hasMotion ? motion : black;
            TextureHandle prevColorIn = usePrevColor ? renderGraph.ImportTexture(prevColorRT) : black;

            const GraphicsFormat kRgba = GraphicsFormat.R16G16B16A16_SFloat;
            TextureHandle lowGeo = CreateUav(renderGraph, lowW, lowH, kRgba, "SSGICore_LowGeo");
            TextureHandle trace = CreateUav(renderGraph, lowW, lowH, kRgba, "SSGICore_Trace");
            TextureHandle filterA = CreateUav(renderGraph, lowW, lowH, kRgba, "SSGICore_FilterA");
            TextureHandle filterB = CreateUav(renderGraph, lowW, lowH, kRgba, "SSGICore_FilterB");
            TextureHandle giFull = CreateUav(renderGraph, fullW, fullH, kRgba, "SSGICore_GI");

            TextureHandle histColorCur = renderGraph.ImportTexture(hist.GetCurrent(0));
            TextureHandle histMomCur = renderGraph.ImportTexture(hist.GetCurrent(1));
            TextureHandle histGeoCur = renderGraph.ImportTexture(hist.GetCurrent(2));
            TextureHandle histColorPrev = renderGraph.ImportTexture(hist.GetPrevious(0));
            TextureHandle histMomPrev = renderGraph.ImportTexture(hist.GetPrevious(1));
            TextureHandle histGeoPrev = renderGraph.ImportTexture(hist.GetPrevious(2));

            // ---- 0/1. Normals + HZB: reuse the early (async) prep when it ran, otherwise record it inline ----
            TextureHandle normalsRes, hzbAtlas;
            SSGICoreFrameData prep = frameData.Contains<SSGICoreFrameData>() ? frameData.Get<SSGICoreFrameData>() : null;
            if (prep != null && prep.ready && prep.fullW == fullW && prep.fullH == fullH && prep.normalMode == normalMode)
            {
                normalsRes = prep.normalsRes;
                hzbAtlas = prep.hzbAtlas;
            }
            else
            {
                RecordDepthPrep(renderGraph, c, depth, normalsIn, fullW, fullH, hzbAtlasSize, false, out normalsRes, out hzbAtlas);
            }

            // ---- 1b. Radiance cache: inject sparse screen samples, blend into the shifted history -------------
            TextureHandle cacheTex = renderGraph.ImportTexture(SSGICoreResources.DummyVolume);
            if (cacheActive)
            {
                TextureHandle cachePrev = renderGraph.ImportTexture(cacheHist.GetPrevious());
                TextureHandle cacheCur = renderGraph.ImportTexture(cacheHist.GetCurrent());
                BufferHandle cacheAcc = renderGraph.ImportBuffer(cacheHist.Accumulation);

                using (var b = renderGraph.AddComputePass("SSGICore Cache Inject", out StepData d))
                {
                    d.Begin(SSGICoreResources.Cache, SSGICoreResources.KCacheInject, (fullW + 7) / 8, (fullH + 7) / 8, c, 0);
                    d.Add(SSGICoreIds.Depth, depth);            b.UseTexture(depth, AccessFlags.Read);
                    d.Add(SSGICoreIds.NormalsRes, normalsRes);  b.UseTexture(normalsRes, AccessFlags.Read);
                    d.Add(SSGICoreIds.Color, opaque);           b.UseTexture(opaque, AccessFlags.Read);
                    d.AddBuffer(SSGICoreIds.CacheAcc, cacheAcc); b.UseBuffer(cacheAcc, AccessFlags.ReadWrite);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
                }

                using (var b = renderGraph.AddComputePass("SSGICore Cache Resolve", out StepData d))
                {
                    const int groups = SSGICoreCacheHistory.GridSize / 4;
                    d.BeginGroups(SSGICoreResources.Cache, SSGICoreResources.KCacheResolve, groups, groups, c, 0);
                    d.groupsZ = groups;
                    d.Add(SSGICoreIds.CachePrev, cachePrev);    b.UseTexture(cachePrev, AccessFlags.Read);
                    d.Add(SSGICoreIds.CacheCur, cacheCur);      b.UseTexture(cacheCur, AccessFlags.Write);
                    d.AddBuffer(SSGICoreIds.CacheAcc, cacheAcc); b.UseBuffer(cacheAcc, AccessFlags.ReadWrite);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
                }

                cacheTex = cacheCur;
            }

            // ---- 2. Trace (low-res, dithered, multi-bounce); also writes the low-res geometry buffer ------
            if (!restirActive)
            {
                using (var b = renderGraph.AddComputePass("SSGICore Trace", out StepData d))
                {
                    d.Begin(SSGICoreResources.Trace, SSGICoreResources.KTrace, lowW, lowH, c, 0);
                    d.Add(SSGICoreIds.Depth, depth);            b.UseTexture(depth, AccessFlags.Read);
                    d.Add(SSGICoreIds.NormalsRes, normalsRes);      b.UseTexture(normalsRes, AccessFlags.Read);
                    d.Add(SSGICoreIds.Motion, motionIn);        b.UseTexture(motionIn, AccessFlags.Read);
                    d.Add(SSGICoreIds.Color, opaque);           b.UseTexture(opaque, AccessFlags.Read);
                    d.Add(SSGICoreIds.PrevColor, prevColorIn);  b.UseTexture(prevColorIn, AccessFlags.Read);
                    d.Add(SSGICoreIds.HzbAtlas, hzbAtlas);      b.UseTexture(hzbAtlas, AccessFlags.Read);
                    d.Add(SSGICoreIds.CacheTex, cacheTex);      b.UseTexture(cacheTex, AccessFlags.Read);
                    d.Add(SSGICoreIds.TraceOut, trace);         b.UseTexture(trace, AccessFlags.Write);
                    d.Add(SSGICoreIds.LowGeoOut, lowGeo);       b.UseTexture(lowGeo, AccessFlags.Write);
                    b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
                }
            }
            else
            {
                // ReSTIR-lite: initial candidates + temporal reuse (history reservoirs), then spatial reuse + resolve into 'trace'
                TextureHandle resACur = renderGraph.ImportTexture(restirHist.GetCurrent(0));
                TextureHandle resBCur = renderGraph.ImportTexture(restirHist.GetCurrent(1));
                TextureHandle resCCur = renderGraph.ImportTexture(restirHist.GetCurrent(2));
                TextureHandle resAPrev = renderGraph.ImportTexture(restirHist.GetPrevious(0));
                TextureHandle resBPrev = renderGraph.ImportTexture(restirHist.GetPrevious(1));
                TextureHandle resCPrev = renderGraph.ImportTexture(restirHist.GetPrevious(2));

                using (var b = renderGraph.AddComputePass("SSGICore Trace ReSTIR", out StepData d))
                {
                    d.Begin(SSGICoreResources.Trace, SSGICoreResources.KTraceRestir, lowW, lowH, c, 0);
                    d.Add(SSGICoreIds.Depth, depth);            b.UseTexture(depth, AccessFlags.Read);
                    d.Add(SSGICoreIds.NormalsRes, normalsRes);  b.UseTexture(normalsRes, AccessFlags.Read);
                    d.Add(SSGICoreIds.Motion, motionIn);        b.UseTexture(motionIn, AccessFlags.Read);
                    d.Add(SSGICoreIds.Color, opaque);           b.UseTexture(opaque, AccessFlags.Read);
                    d.Add(SSGICoreIds.PrevColor, prevColorIn);  b.UseTexture(prevColorIn, AccessFlags.Read);
                    d.Add(SSGICoreIds.HzbAtlas, hzbAtlas);      b.UseTexture(hzbAtlas, AccessFlags.Read);
                    d.Add(SSGICoreIds.CacheTex, cacheTex);      b.UseTexture(cacheTex, AccessFlags.Read);
                    d.Add(SSGICoreIds.HistGeoPrev, histGeoPrev); b.UseTexture(histGeoPrev, AccessFlags.Read);
                    d.Add(SSGICoreIds.ResAPrev, resAPrev);      b.UseTexture(resAPrev, AccessFlags.Read);
                    d.Add(SSGICoreIds.ResBPrev, resBPrev);      b.UseTexture(resBPrev, AccessFlags.Read);
                    d.Add(SSGICoreIds.ResCPrev, resCPrev);      b.UseTexture(resCPrev, AccessFlags.Read);
                    d.Add(SSGICoreIds.LowGeoOut, lowGeo);       b.UseTexture(lowGeo, AccessFlags.Write);
                    d.Add(SSGICoreIds.ResAOut, resACur);        b.UseTexture(resACur, AccessFlags.Write);
                    d.Add(SSGICoreIds.ResBOut, resBCur);        b.UseTexture(resBCur, AccessFlags.Write);
                    d.Add(SSGICoreIds.ResCOut, resCCur);        b.UseTexture(resCCur, AccessFlags.Write);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
                }

                using (var b = renderGraph.AddComputePass("SSGICore ReSTIR Spatial", out StepData d))
                {
                    d.Begin(SSGICoreResources.Restir, SSGICoreResources.KRestirSpatial, lowW, lowH, c, 0);
                    d.Add(SSGICoreIds.LowGeo, lowGeo);          b.UseTexture(lowGeo, AccessFlags.Read);
                    d.Add(SSGICoreIds.ResAIn, resACur);         b.UseTexture(resACur, AccessFlags.Read);
                    d.Add(SSGICoreIds.ResBIn, resBCur);         b.UseTexture(resBCur, AccessFlags.Read);
                    d.Add(SSGICoreIds.ResCIn, resCCur);         b.UseTexture(resCCur, AccessFlags.Read);
                    d.Add(SSGICoreIds.TraceOut, trace);         b.UseTexture(trace, AccessFlags.Write);
                    b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
                }
            }

            // ---- 3. Temporal accumulation + variance ----------------------------------------------------
            using (var b = renderGraph.AddComputePass("SSGICore Temporal", out StepData d))
            {
                d.Begin(SSGICoreResources.Temporal, SSGICoreResources.KTemporal, lowW, lowH, c, 0);
                d.Add(SSGICoreIds.TraceIn, trace);              b.UseTexture(trace, AccessFlags.Read);
                d.Add(SSGICoreIds.LowGeo, lowGeo);              b.UseTexture(lowGeo, AccessFlags.Read);
                d.Add(SSGICoreIds.Motion, motionIn);            b.UseTexture(motionIn, AccessFlags.Read);
                d.Add(SSGICoreIds.HistColorPrev, histColorPrev); b.UseTexture(histColorPrev, AccessFlags.Read);
                d.Add(SSGICoreIds.HistMomPrev, histMomPrev);     b.UseTexture(histMomPrev, AccessFlags.Read);
                d.Add(SSGICoreIds.HistGeoPrev, histGeoPrev);     b.UseTexture(histGeoPrev, AccessFlags.Read);
                d.Add(SSGICoreIds.HistColorOut, histColorCur);   b.UseTexture(histColorCur, AccessFlags.Write);
                d.Add(SSGICoreIds.HistMomOut, histMomCur);       b.UseTexture(histMomCur, AccessFlags.Write);
                d.Add(SSGICoreIds.HistGeoOut, histGeoCur);       b.UseTexture(histGeoCur, AccessFlags.Write);
                d.Add(SSGICoreIds.FilterOut, filterA);           b.UseTexture(filterA, AccessFlags.Write);
                b.AllowPassCulling(false);
                b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
            }

            // ---- 4. Variance-guided a-trous filter (ping-pong A <-> B) ----------------------------------
            TextureHandle filtered = filterA;
            int iterations = Mathf.Clamp(cfg.filterIterations, 0, 4);
            for (int i = 0; i < iterations; i++)
            {
                TextureHandle src = (i % 2 == 0) ? filterA : filterB;
                TextureHandle dst = (i % 2 == 0) ? filterB : filterA;
                using (var b = renderGraph.AddComputePass("SSGICore Filter " + i, out StepData d))
                {
                    d.Begin(SSGICoreResources.Filter, SSGICoreResources.KAtrous, lowW, lowH, c, 1 << i);
                    d.Add(SSGICoreIds.LowGeo, lowGeo);      b.UseTexture(lowGeo, AccessFlags.Read);
                    d.Add(SSGICoreIds.FilterIn, src);       b.UseTexture(src, AccessFlags.Read);
                    d.Add(SSGICoreIds.FilterOut, dst);      b.UseTexture(dst, AccessFlags.Write);
                    // Unity reports every declared-but-unset kernel property, so these are always bound.
                    // Age lives in HistMom.z. Iteration 0 of the Enhanced denoiser writes its filtered colour back into the
                    // history (SVGF-style feedback); every other case binds this pass's own output as an inert placeholder
                    // (the shader only writes it when _SSGI_Enhanced != 0 and the step size is 1).
                    d.Add(SSGICoreIds.AgeIn, histMomCur);   b.UseTexture(histMomCur, AccessFlags.Read);
                    if (enhanced && i == 0)
                    {
                        d.Add(SSGICoreIds.HistFbOut, histColorCur);
                        b.UseTexture(histColorCur, AccessFlags.Write);
                    }
                    else
                    {
                        d.Add(SSGICoreIds.HistFbOut, dst);
                    }
                    b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
                }
                filtered = dst;
            }

            // ---- 5. Depth/normal-aware bilateral upsample -----------------------------------------------
            using (var b = renderGraph.AddComputePass("SSGICore Upsample", out StepData d))
            {
                d.Begin(SSGICoreResources.Upsample, SSGICoreResources.KUpsample, fullW, fullH, c, 0);
                d.Add(SSGICoreIds.Depth, depth);        b.UseTexture(depth, AccessFlags.Read);
                d.Add(SSGICoreIds.NormalsRes, normalsRes);  b.UseTexture(normalsRes, AccessFlags.Read);
                d.Add(SSGICoreIds.LowGeo, lowGeo);      b.UseTexture(lowGeo, AccessFlags.Read);
                d.Add(SSGICoreIds.FilterIn, filtered);  b.UseTexture(filtered, AccessFlags.Read);
                d.Add(SSGICoreIds.GIOut, giFull);       b.UseTexture(giFull, AccessFlags.Write);
                b.SetRenderFunc(static (StepData data, ComputeGraphContext ctx) => data.Execute(ctx.cmd));
            }

            // ---- 6. Composite (additive, approximate albedo) + user-facing global GI texture ------------
            SSGICoreDebugView debug = vol.debugView.value;
            using (var b = renderGraph.AddRasterRenderPass("SSGICore Composite", out CompositeData d))
            {
                d.gi = giFull;          b.UseTexture(giFull, AccessFlags.Read);
                d.opaque = opaque;      b.UseTexture(opaque, AccessFlags.Read);
                d.depth = depth;        b.UseTexture(depth, AccessFlags.Read);
                d.raw = default;
                d.age = default;
                d.normals = default;
                if (debug == SSGICoreDebugView.RawTrace) { d.raw = trace;         b.UseTexture(trace, AccessFlags.Read); }
                if (debug == SSGICoreDebugView.Normals || debug == SSGICoreDebugView.NormalSource) { d.normals = normalsRes; b.UseTexture(normalsRes, AccessFlags.Read); }
                if (debug == SSGICoreDebugView.HistoryAge) { d.age = histColorCur; b.UseTexture(histColorCur, AccessFlags.Read); }

                d.material = SSGICoreResources.Composite;
                d.block = m_Block;
                d.passIndex = debug == SSGICoreDebugView.None ? 0 : 1;
                d.prm = new Vector4(vol.intensity.value, vol.albedoLevel.value, (float)debug, ds);
                d.size = c.fullSize;

                b.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                b.SetGlobalTextureAfterPass(giFull, SSGICoreIds.UserGI);
                b.AllowGlobalStateModification(true);
                b.AllowPassCulling(false);
                b.SetRenderFunc(static (CompositeData data, RasterGraphContext ctx) =>
                {
                    RasterCommandBuffer cmd = ctx.cmd;
                    cmd.SetGlobalTexture(SSGICoreIds.CompGI, data.gi);
                    cmd.SetGlobalTexture(SSGICoreIds.CompOpaque, data.opaque);
                    cmd.SetGlobalTexture(SSGICoreIds.CompDepth, data.depth);
                    if (data.raw.IsValid()) cmd.SetGlobalTexture(SSGICoreIds.CompRaw, data.raw);
                    if (data.age.IsValid()) cmd.SetGlobalTexture(SSGICoreIds.CompAge, data.age);
                    if (data.normals.IsValid()) cmd.SetGlobalTexture(SSGICoreIds.CompNormals, data.normals);
                    data.block.SetVector(SSGICoreIds.CompParams, data.prm);
                    data.block.SetVector(SSGICoreIds.CompSize, data.size);
                    cmd.DrawProcedural(Matrix4x4.identity, data.material, data.passIndex, MeshTopology.Triangles, 3, 1, data.block);
                });
            }

            // history bookkeeping for next frame
            hist.MarkWritten(Time.frameCount, view, viewProj);
            if (restirActive)
                restirHist.MarkWritten(Time.frameCount);
            if (cacheActive)
                cacheHist.MarkWritten(Time.frameCount, cacheCell, cacheBase);
        }

        static void FillAmbient(Vector4[] dst, float intensity)
        {
            RenderSettings.ambientProbe.Evaluate(s_AmbientDirs, s_AmbientColors);
            for (int i = 0; i < 6; i++)
            {
                Color col = s_AmbientColors[i];
                dst[i] = new Vector4(col.r, col.g, col.b, 0f) * intensity;
            }
        }
    }
}
