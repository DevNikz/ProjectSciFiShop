using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GTAO_URP
{
    internal sealed class GTAOPass : ScriptableRenderPass
    {
        internal const string ShaderName = "Hidden/GTAO_URP/GTAO";

        // Shader pass indices, must match GTAO.shader.
        const int PassEstimate = 0,
            PassTemporal = 1,
            PassBlurH = 2,
            PassResolve = 3,
            PassComposite = 4,
            PassDebug = 5,
            PassPrefilterDepth = 6,
            PassDepthMip = 7;

        const int DepthMipCount = 5; // mip 0 (full res) .. mip 4

        static class IDs
        {
            public static readonly int Params = Shader.PropertyToID("_GTAO_Params");
            public static readonly int Params2 = Shader.PropertyToID("_GTAO_Params2");
            public static readonly int Fade = Shader.PropertyToID("_GTAO_Fade");
            public static readonly int Proj = Shader.PropertyToID("_GTAO_Proj");
            public static readonly int ProjOrtho = Shader.PropertyToID("_GTAO_ProjOrtho");
            public static readonly int Size = Shader.PropertyToID("_GTAO_Size");
            public static readonly int FullSize = Shader.PropertyToID("_GTAO_FullSize");
            public static readonly int Temporal = Shader.PropertyToID("_GTAO_Temporal");
            public static readonly int Blur = Shader.PropertyToID("_GTAO_Blur");
            public static readonly int MipParams = Shader.PropertyToID("_GTAO_MipParams");
            public static readonly int Tune = Shader.PropertyToID("_GTAO_Tune");
            public static readonly int[] DepthMips =
            {
                Shader.PropertyToID("_GTAO_DepthMip0"),
                Shader.PropertyToID("_GTAO_DepthMip1"),
                Shader.PropertyToID("_GTAO_DepthMip2"),
                Shader.PropertyToID("_GTAO_DepthMip3"),
                Shader.PropertyToID("_GTAO_DepthMip4"),
            };
            public static readonly int ViewToWorld = Shader.PropertyToID("_GTAO_ViewToWorld");
            public static readonly int WorldToView = Shader.PropertyToID("_GTAO_WorldToView");
            public static readonly int PrevViewProj = Shader.PropertyToID("_GTAO_PrevViewProj");
            public static readonly int PrevView = Shader.PropertyToID("_GTAO_PrevView");
            public static readonly int History = Shader.PropertyToID("_GTAO_History");
            public static readonly int Motion = Shader.PropertyToID("_GTAO_MotionVectors");
            public static readonly int Depth = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int Normals = Shader.PropertyToID("_CameraNormalsTexture");
            public static readonly int SSAOTex = Shader.PropertyToID(
                "_ScreenSpaceOcclusionTexture"
            );
            public static readonly int AOParam = Shader.PropertyToID("_AmbientOcclusionParam");
        }

        internal static readonly GlobalKeyword SSAOKeyword = GlobalKeyword.Create(
            "_SCREEN_SPACE_OCCLUSION"
        );

        // One sampler per stage so Profiler / Frame Debugger show per-pass GPU time. Indexed by Pass* const.
        static readonly ProfilingSampler[] s_Samplers =
        {
            new ProfilingSampler("GTAO Estimate"),
            new ProfilingSampler("GTAO Temporal"),
            new ProfilingSampler("GTAO Blur H"),
            new ProfilingSampler("GTAO Resolve"),
            new ProfilingSampler("GTAO Composite"),
            new ProfilingSampler("GTAO Debug"),
            new ProfilingSampler("GTAO Prefilter Depth"),
            new ProfilingSampler("GTAO Depth Mip"),
        };

        static readonly string[] s_DepthMipTexNames =
        {
            "_GTAO_DepthMip0",
            "_GTAO_DepthMip1",
            "_GTAO_DepthMip2",
            "_GTAO_DepthMip3",
            "_GTAO_DepthMip4",
        };

        static readonly string[] s_DepthMipPassNames =
        {
            "GTAO Prefilter Depth",
            "GTAO Depth Mip 1",
            "GTAO Depth Mip 2",
            "GTAO Depth Mip 3",
            "GTAO Depth Mip 4",
        };

        sealed class PassData
        {
            internal Material material;
            internal int pass;
            internal TextureHandle src,
                depth,
                normals,
                history,
                motion;
            internal TextureHandle[] mips; // depth MIP chain; entries are null when unused
            internal bool applyGlobals;
            internal float directStrength;
        }

        // Inputs/outputs of one AddPass call; unset handles stay null.
        struct PassInputs
        {
            internal TextureHandle src,
                dst,
                depth,
                normals,
                history,
                motion;
            internal TextureHandle[] depthMips; // null = pass doesn't read the depth MIP chain
            internal bool publishGlobals,
                readWriteDst,
                srcIsDepth;

            internal PassInputs(TextureHandle src, TextureHandle dst)
            {
                this.src = src;
                this.dst = dst;
                depth = normals = history = motion = TextureHandle.nullHandle;
                depthMips = null;
                publishGlobals = readWriteDst = srcIsDepth = false;
            }
        }

        sealed class HistoryData
        {
            internal readonly RTHandle[] rt = new RTHandle[2];
            internal int w,
                h,
                cur,
                lastFrame = -100;
            internal GraphicsFormat fmt;
            internal Matrix4x4 prevView = Matrix4x4.identity,
                prevProj = Matrix4x4.identity;
            internal bool hasPrev;
        }

        readonly Dictionary<int, HistoryData> m_History = new Dictionary<int, HistoryData>();
        readonly List<int> m_Stale = new List<int>();
        readonly TextureHandle[] m_DepthMips = new TextureHandle[DepthMipCount];
        readonly TextureHandle[] m_DepthMip0Only = new TextureHandle[DepthMipCount]; // resolve passes only need mip 0

        GTAOSettings m_S;
        Material m_Mat;
        bool m_NeedNormals,
            m_AfterOpaque;
        bool m_WarnedDepth;

        static GraphicsFormat s_FmtRG = GraphicsFormat.None,
            s_FmtR = GraphicsFormat.None,
            s_FmtDepth16 = GraphicsFormat.None,
            s_FmtDepth32 = GraphicsFormat.None;

        internal GTAOPass()
        {
            profilingSampler = new ProfilingSampler("GTAO");
        }

        internal bool Setup(GTAOSettings s, Material mat)
        {
            if (s == null || mat == null)
                return false;
            m_S = s;
            m_Mat = mat;
            m_AfterOpaque = s.apply == GTAOApply.AfterOpaque || s.debugView;
            m_NeedNormals = s.normals == GTAONormals.DepthNormals;

            // Same event choice as URP's built-in SSAO for Forward (verified against URP 17 source):
            // AfterRenderingPrePasses+1 normally, BeforeRenderingTransparents for after-opaque compositing.
            renderPassEvent = m_AfterOpaque
                ? RenderPassEvent.BeforeRenderingTransparents
                : RenderPassEvent.AfterRenderingPrePasses + 1;

            var input = ScriptableRenderPassInput.Depth;
            if (m_NeedNormals)
                input |= ScriptableRenderPassInput.Normal;
            if (s.temporal && s.useMotionVectors)
                input |= ScriptableRenderPassInput.Motion;
            ConfigureInput(input);
            return true;
        }

        static void ResolveFormats()
        {
            if (s_FmtRG != GraphicsFormat.None)
                return;
            s_FmtRG = Pick(
                GraphicsFormat.R16G16_SFloat,
                GraphicsFormat.R16G16B16A16_SFloat,
                GraphicsFormat.R32G32_SFloat,
                GraphicsFormat.R32G32B32A32_SFloat
            );
            s_FmtR = Pick(GraphicsFormat.R8_UNorm, GraphicsFormat.R8G8B8A8_UNorm);
            s_FmtDepth16 = Pick(
                GraphicsFormat.R16_SFloat,
                GraphicsFormat.R32_SFloat,
                GraphicsFormat.R16G16_SFloat,
                GraphicsFormat.R32G32_SFloat
            );
            s_FmtDepth32 = Pick(
                GraphicsFormat.R32_SFloat,
                GraphicsFormat.R32G32_SFloat,
                GraphicsFormat.R16_SFloat,
                GraphicsFormat.R16G16_SFloat
            );
        }

        static GraphicsFormat Pick(params GraphicsFormat[] candidates)
        {
            foreach (var f in candidates)
                if (SystemInfo.IsFormatSupported(f, GraphicsFormatUsage.Render))
                    return f;
            return GraphicsFormat.R8G8B8A8_UNorm;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Mat == null || m_S == null)
                return;

            var res = frameData.Get<UniversalResourceData>();
            var cam = frameData.Get<UniversalCameraData>();
            if (cam == null || cam.camera == null)
                return;

            TextureHandle depth = res.cameraDepthTexture;
            if (!depth.IsValid())
            {
                if (!m_WarnedDepth)
                {
                    Debug.LogWarning("[GTAO] Camera depth texture unavailable; AO skipped.");
                    m_WarnedDepth = true;
                }
                return;
            }

            TextureHandle normals = TextureHandle.nullHandle;
            bool useNormals = m_NeedNormals && res.cameraNormalsTexture.IsValid();
            if (useNormals)
                normals = res.cameraNormalsTexture;

            TextureHandle color = TextureHandle.nullHandle;
            if (m_AfterOpaque)
            {
                color = res.activeColorTexture;
                if (!color.IsValid())
                    return;
            }

            ResolveFormats();

            var s = m_S;
            var camDesc = cam.cameraTargetDescriptor;
            int fw = Mathf.Max(1, camDesc.width),
                fh = Mathf.Max(1, camDesc.height);
            int down = s.downsample ? 2 : 1;
            int aw = Mathf.Max(1, fw / down),
                ah = Mathf.Max(1, fh / down);

            bool camOk =
                cam.cameraType == CameraType.Game || cam.cameraType == CameraType.SceneView;
            bool wantTemporal = s.temporal && camOk && s.temporalWeight > 0f;
            HistoryData hist = null;
            bool histValid = false;
            int prevIdx = 0,
                curIdx = 0;
            if (wantTemporal)
            {
                hist = AcquireHistory(cam.camera.GetInstanceID(), aw, ah, s_FmtRG, out histValid);
                if (hist == null)
                    wantTemporal = false;
                else
                {
                    prevIdx = hist.cur;
                    curIdx = 1 - hist.cur;
                }
            }

            bool useMotion = wantTemporal && s.useMotionVectors && res.motionVectorColor.IsValid();
            TextureHandle motion = useMotion ? res.motionVectorColor : TextureHandle.nullHandle;

            UpdateMaterial(cam, s, aw, ah, fw, fh, down, useNormals, useMotion, histValid, hist);

            var d = camDesc;
            d.depthStencilFormat = GraphicsFormat.None;
            d.depthBufferBits = 0;
            d.msaaSamples = 1;
            d.bindMS = false;
            d.useMipMap = false;
            d.autoGenerateMips = false;

            TextureHandle aoTex = CreateAOTexture(
                renderGraph,
                d,
                aw,
                ah,
                s_FmtRG,
                "_GTAO_Raw",
                FilterMode.Point
            );

            int blurR = s.GetBlurRadius();
            TextureHandle blurTex = TextureHandle.nullHandle;
            if (blurR > 0)
                blurTex = CreateAOTexture(
                    renderGraph,
                    d,
                    aw,
                    ah,
                    s_FmtRG,
                    "_GTAO_BlurTmp",
                    FilterMode.Point
                );

            TextureHandle final = TextureHandle.nullHandle;
            if (!m_AfterOpaque)
                final = CreateAOTexture(
                    renderGraph,
                    d,
                    fw,
                    fh,
                    s_FmtR,
                    "_ScreenSpaceOcclusionTexture",
                    FilterMode.Bilinear
                );

            // 0. Linear-depth MIP chain
            TextureHandle[] mips = null,
                mip0Only = null;
            if (s.useDepthMips)
            {
                mips = AddDepthMipChain(
                    renderGraph,
                    d,
                    depth,
                    fw,
                    fh,
                    s.highPrecisionDepthMips ? s_FmtDepth32 : s_FmtDepth16
                );
                m_DepthMip0Only[0] = mips[0];
                mip0Only = m_DepthMip0Only;
            }

            // 1. Estimate
            AddPass(
                renderGraph,
                "GTAO Estimate",
                PassEstimate,
                new PassInputs(depth, aoTex)
                {
                    depth = depth,
                    normals = normals,
                    srcIsDepth = true,
                    depthMips = mips,
                }
            );
            TextureHandle chain = aoTex;

            // 2. Temporal resolve
            if (wantTemporal)
            {
                TextureHandle hPrev = renderGraph.ImportTexture(hist.rt[prevIdx]);
                TextureHandle hCur = renderGraph.ImportTexture(hist.rt[curIdx]);
                AddPass(
                    renderGraph,
                    "GTAO Temporal",
                    PassTemporal,
                    new PassInputs(chain, hCur) { history = hPrev, motion = motion }
                );
                chain = hCur;

                hist.cur = curIdx;
                hist.hasPrev = true;
                hist.lastFrame = Time.frameCount;
                hist.prevView = cam.GetViewMatrix();
                hist.prevProj = cam.GetProjectionMatrix();
            }
            else if (hist != null)
            {
                hist.hasPrev = false;
            }

            // 3. Blur H
            if (blurR > 0)
            {
                AddPass(
                    renderGraph,
                    "GTAO Blur H",
                    PassBlurH,
                    new PassInputs(chain, blurTex)
                );
                chain = blurTex;
            }

            // 4. Blur V + upsample -> output
            if (m_AfterOpaque)
            {
                int p = s.debugView ? PassDebug : PassComposite;
                AddPass(
                    renderGraph,
                    s.debugView ? "GTAO Debug" : "GTAO Composite",
                    p,
                    new PassInputs(chain, color)
                    {
                        depth = depth,
                        readWriteDst = !s.debugView,
                        depthMips = mip0Only,
                    }
                );
            }
            else
            {
                AddPass(
                    renderGraph,
                    "GTAO Resolve",
                    PassResolve,
                    new PassInputs(chain, final)
                    {
                        depth = depth,
                        publishGlobals = true,
                        depthMips = mip0Only,
                    }
                );
                // res.ssaoTexture has only an internal setter (URP-package-only); SetGlobalTextureAfterPass
                // above already publishes _ScreenSpaceOcclusionTexture, which is all lit shaders read from.
            }

            CleanupStale();
        }

        // Linear-depth MIP chain: mip 0 = full-res linear eye depth, each next level a farthest-weighted 2x2 average.
        // One transient RT per level (like URP's bloom chain): RenderGraph tracks a texture as one resource, so
        // sampling mip N-1 while rendering mip N of the same texture would be a read/write hazard.
        TextureHandle[] AddDepthMipChain(
            RenderGraph rg,
            RenderTextureDescriptor desc,
            TextureHandle depth,
            int w,
            int h,
            GraphicsFormat fmt
        )
        {
            m_DepthMips[0] = CreateAOTexture(rg, desc, w, h, fmt, s_DepthMipTexNames[0], FilterMode.Point);
            AddPass(
                rg,
                s_DepthMipPassNames[0],
                PassPrefilterDepth,
                new PassInputs(depth, m_DepthMips[0]) { depth = depth, srcIsDepth = true }
            );

            for (int i = 1; i < DepthMipCount; i++)
            {
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
                m_DepthMips[i] = CreateAOTexture(rg, desc, w, h, fmt, s_DepthMipTexNames[i], FilterMode.Point);
                AddPass(
                    rg,
                    s_DepthMipPassNames[i],
                    PassDepthMip,
                    new PassInputs(m_DepthMips[i - 1], m_DepthMips[i])
                );
            }
            return m_DepthMips;
        }

        // Transient AO-resolution (or full-res) RT; only size, format and filter differ between callers.
        static TextureHandle CreateAOTexture(
            RenderGraph rg,
            RenderTextureDescriptor desc,
            int w,
            int h,
            GraphicsFormat fmt,
            string name,
            FilterMode filter
        )
        {
            desc.width = w;
            desc.height = h;
            desc.graphicsFormat = fmt;
            return UniversalRenderer.CreateRenderGraphTexture(rg, desc, name, false, filter);
        }

        void AddPass(RenderGraph rg, string name, int pass, PassInputs io)
        {
            using (var b = rg.AddRasterRenderPass<PassData>(name, out var pd, s_Samplers[pass]))
            {
                pd.material = m_Mat;
                pd.pass = pass;
                pd.src = io.src;
                pd.depth = io.depth;
                pd.normals = io.normals;
                pd.history = io.history;
                pd.motion = io.motion;
                pd.applyGlobals = io.publishGlobals;
                pd.mips ??= new TextureHandle[DepthMipCount]; // PassData is pooled: allocated once, then reused
                for (int i = 0; i < DepthMipCount; i++)
                    pd.mips[i] = io.depthMips != null ? io.depthMips[i] : TextureHandle.nullHandle;
                pd.directStrength = m_S.directLightingStrength;

                b.UseTexture(io.src, AccessFlags.Read);
                if (io.depth.IsValid() && !io.srcIsDepth)
                    b.UseTexture(io.depth, AccessFlags.Read); // estimate uses depth as its dummy src
                if (io.normals.IsValid())
                    b.UseTexture(io.normals, AccessFlags.Read);
                if (io.history.IsValid())
                    b.UseTexture(io.history, AccessFlags.Read);
                if (io.motion.IsValid())
                    b.UseTexture(io.motion, AccessFlags.Read);
                for (int i = 0; i < DepthMipCount; i++)
                    if (pd.mips[i].IsValid())
                        b.UseTexture(pd.mips[i], AccessFlags.Read);

                b.SetRenderAttachment(
                    io.dst,
                    0,
                    io.readWriteDst ? AccessFlags.ReadWrite : AccessFlags.Write
                );
                b.AllowPassCulling(false);

                if (io.publishGlobals)
                {
                    b.AllowGlobalStateModification(true);
                    b.SetGlobalTextureAfterPass(io.dst, IDs.SSAOTex);
                }

                b.SetRenderFunc(
                    static (PassData data, RasterGraphContext ctx) =>
                    {
                        var m = data.material;
                        if (data.depth.IsValid())
                            m.SetTexture(IDs.Depth, data.depth);
                        if (data.normals.IsValid())
                            m.SetTexture(IDs.Normals, data.normals);
                        if (data.history.IsValid())
                            m.SetTexture(IDs.History, data.history);
                        if (data.motion.IsValid())
                            m.SetTexture(IDs.Motion, data.motion);
                        for (int i = 0; i < DepthMipCount; i++)
                            if (data.mips[i].IsValid())
                                m.SetTexture(IDs.DepthMips[i], data.mips[i]);

                        Blitter.BlitTexture(
                            ctx.cmd,
                            data.src,
                            new Vector4(1, 1, 0, 0),
                            m,
                            data.pass
                        );

                        if (data.applyGlobals)
                        {
                            ctx.cmd.SetKeyword(SSAOKeyword, true);
                            ctx.cmd.SetGlobalVector(
                                IDs.AOParam,
                                new Vector4(1f, 0f, 0f, data.directStrength)
                            ); // same layout URP SSAO uses
                        }
                    }
                );
            }
        }

        void UpdateMaterial(
            UniversalCameraData cam,
            GTAOSettings s,
            int aw,
            int ah,
            int fw,
            int fh,
            int down,
            bool useNormals,
            bool useMotion,
            bool histValid,
            HistoryData hist
        )
        {
            var m = m_Mat;
            Matrix4x4 view = cam.GetViewMatrix();
            Matrix4x4 proj = cam.GetProjectionMatrix();
            bool ortho = cam.camera.orthographic;

            CoreUtils.SetKeyword(m, "_SOURCE_DEPTH_NORMALS", useNormals);
            CoreUtils.SetKeyword(m, "_ORTHOGRAPHIC", ortho);
            CoreUtils.SetKeyword(m, "_MOTION_VECTORS", useMotion);
            CoreUtils.SetKeyword(m, "_GTAO_DEPTH_MIPS", s.useDepthMips);

            s.GetSampleCounts(out int slices, out int steps);
            uint frame = s.temporal ? (uint)(Time.frameCount & 63) : 0u;

            m.SetVector(IDs.Params, new Vector4(s.radius, s.intensity, s.falloffRange, 0f));
            m.SetVector(IDs.Params2, new Vector4(slices, steps, s.maxPixelRadius, s.multiBounce));
            m.SetVector(IDs.Fade, new Vector4(s.fadeStart, s.fadeEnd, down, frame));
            m.SetVector(IDs.Proj, new Vector4(proj.m00, proj.m11, proj.m02, proj.m12));
            m.SetVector(IDs.ProjOrtho, new Vector4(proj.m03, proj.m13, 0, 0));
            m.SetVector(IDs.Size, new Vector4(aw, ah, 1f / aw, 1f / ah));
            m.SetVector(IDs.FullSize, new Vector4(fw, fh, 1f / fw, 1f / fh));
            m.SetVector(
                IDs.Temporal,
                new Vector4(
                    histValid ? s.temporalWeight : 0f,
                    1f / s.velocityFadePixels,
                    s.depthTolerance,
                    s.clampStrength
                )
            );
            m.SetVector(IDs.Blur, new Vector4(s.GetBlurRadius(), s.blurDepthSharpness, 0, 0));
            m.SetVector(IDs.MipParams, new Vector4(s.depthMipBias, DepthMipCount - 1, 0, 0));
            m.SetVector(
                IDs.Tune,
                new Vector4(
                    s.sampleDistributionPower,
                    s.thinOccluderCompensation,
                    s.noise == GTAONoise.Hilbert ? 1f : 0f,
                    0f
                )
            );
            m.SetMatrix(IDs.ViewToWorld, view.inverse);
            m.SetMatrix(IDs.WorldToView, view);

            Matrix4x4 pv = histValid && hist != null ? hist.prevView : view;
            Matrix4x4 pp = histValid && hist != null ? hist.prevProj : proj;
            m.SetMatrix(IDs.PrevViewProj, pp * pv);
            m.SetMatrix(IDs.PrevView, pv);
        }

        HistoryData AcquireHistory(int camId, int w, int h, GraphicsFormat fmt, out bool valid)
        {
            valid = false;
            if (!m_History.TryGetValue(camId, out var hd))
            {
                hd = new HistoryData();
                m_History[camId] = hd;
            }

            bool realloc =
                hd.rt[0] == null || hd.rt[1] == null || hd.w != w || hd.h != h || hd.fmt != fmt;
            if (realloc)
            {
                Release(hd);
                for (int i = 0; i < 2; i++)
                {
                    hd.rt[i] = RTHandles.Alloc(
                        w,
                        h,
                        colorFormat: fmt,
                        filterMode: FilterMode.Bilinear,
                        wrapMode: TextureWrapMode.Clamp,
                        name: "_GTAO_History" + i
                    );
                    if (hd.rt[i] == null)
                    {
                        Release(hd);
                        m_History.Remove(camId);
                        return null;
                    }
                }
                hd.w = w;
                hd.h = h;
                hd.fmt = fmt;
                hd.cur = 0;
                hd.hasPrev = false;
            }
            valid = !realloc && hd.hasPrev && (Time.frameCount - hd.lastFrame) <= 2; // camera cut/gap -> reset
            return hd;
        }

        void CleanupStale()
        {
            m_Stale.Clear();
            foreach (var kv in m_History)
                if (Time.frameCount - kv.Value.lastFrame > 120)
                    m_Stale.Add(kv.Key);
            foreach (var k in m_Stale)
            {
                Release(m_History[k]);
                m_History.Remove(k);
            }
        }

        static void Release(HistoryData hd)
        {
            for (int i = 0; i < 2; i++)
            {
                hd.rt[i]?.Release();
                hd.rt[i] = null;
            }
            hd.hasPrev = false;
        }

        internal void Dispose()
        {
            foreach (var kv in m_History)
                Release(kv.Value);
            m_History.Clear();
        }
    }

    // Disables the AO keyword after opaque lighting so stale state can't leak to other cameras.
    internal sealed class GTAOKeywordResetPass : ScriptableRenderPass
    {
        sealed class Empty { }

        internal GTAOKeywordResetPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var b = renderGraph.AddUnsafePass<Empty>("GTAO Keyword Reset", out _))
            {
                b.AllowPassCulling(false);
                b.AllowGlobalStateModification(true);
                b.SetRenderFunc(
                    static (Empty _, UnsafeGraphContext ctx) =>
                        ctx.cmd.SetKeyword(GTAOPass.SSAOKeyword, false)
                );
            }
        }
    }
}
