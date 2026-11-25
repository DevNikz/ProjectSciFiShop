using System.Collections.Generic;
// BLK-05  Render Graph passes (URP 17 / Unity 6). Reads SSRResolvedParams only; never reads designer settings for accessibility.
// Passes: [Mask] -> Depth pyramid (Hi-Z) -> Trace -> Resolve (spatial + temporal + limiter) -> Composite (depth-aware upsample, blend into active color) -> [Debug view].
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace SSRFeature
{
    internal sealed class SSRRenderPass : ScriptableRenderPass, System.IDisposable
    {
        const int kPassTrace = 0, kPassResolve = 1, kPassComposite = 2, kPassDebug = 3, kPassHiZBase = 4, kPassHiZReduce = 5, kPassDenoise = 6;
        const int kHiZMaxMips = 8;
        static readonly Vector4 k_ScaleBias = new Vector4(1f, 1f, 0f, 0f);
        static readonly ShaderTagId[] k_Tags =
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        static class Ids
        {
            public static readonly int Mask = Shader.PropertyToID("_SSRMask");
            public static readonly int MaskValue = Shader.PropertyToID("_SSRMaskValue");
            public static readonly int SmoothOverride = Shader.PropertyToID("_SSRSmoothOverride");
            public static readonly int History = Shader.PropertyToID("_SSRHistory");
            public static readonly int HiZ = Shader.PropertyToID("_SSRHiZ");
            public static readonly int HiZDims = Shader.PropertyToID("_SSRHiZDims");
            public static readonly int HiZMaxMip = Shader.PropertyToID("_SSRHiZMaxMip");
            public static readonly int HiZSrc = Shader.PropertyToID("_SSRHiZSrc");
            public static readonly int Texel = Shader.PropertyToID("_SSRTexel");
            public static readonly int MaxDistance = Shader.PropertyToID("_SSRMaxDistance");
            public static readonly int Thickness = Shader.PropertyToID("_SSRThickness");
            public static readonly int MaxSteps = Shader.PropertyToID("_SSRMaxSteps");
            public static readonly int RefineSteps = Shader.PropertyToID("_SSRRefineSteps");
            public static readonly int EdgeFade = Shader.PropertyToID("_SSREdgeFade");
            public static readonly int FresnelPower = Shader.PropertyToID("_SSRFresnelPower");
            public static readonly int MinReflectance = Shader.PropertyToID("_SSRMinReflectance");
            public static readonly int Reflectivity = Shader.PropertyToID("_SSRReflectivity");
            public static readonly int Frame = Shader.PropertyToID("_SSRFrame");
            public static readonly int Feedback = Shader.PropertyToID("_SSRFeedback");
            public static readonly int HistoryValid = Shader.PropertyToID("_SSRHistoryValid");
            public static readonly int SafeReset = Shader.PropertyToID("_SSRSafeReset");
            public static readonly int MaxDelta = Shader.PropertyToID("_SSRMaxDelta");
            public static readonly int MaxLuma = Shader.PropertyToID("_SSRMaxLuma");
            public static readonly int Intensity = Shader.PropertyToID("_SSRIntensity");
            public static readonly int MaxOpacity = Shader.PropertyToID("_SSRMaxOpacity");
            public static readonly int Saturation = Shader.PropertyToID("_SSRSaturation");
            public static readonly int SmoothMin = Shader.PropertyToID("_SSRSmoothMin");
            public static readonly int SmoothRamp = Shader.PropertyToID("_SSRSmoothRamp");
            public static readonly int SmoothInfluence = Shader.PropertyToID("_SSRSmoothInfluence");
            public static readonly int Blur = Shader.PropertyToID("_SSRBlur");
            public static readonly int DebugView = Shader.PropertyToID("_SSRDebugView");
            public static readonly int DistFadePower = Shader.PropertyToID("_SSRDistFadePower");
            public static readonly int NormalSharpness = Shader.PropertyToID("_SSRNormalSharpness");
            public static readonly int HitReproj = Shader.PropertyToID("_SSRHitReproj");
            public static readonly int TraceHit = Shader.PropertyToID("_SSRTraceHit");
            public static readonly int PrevVP = Shader.PropertyToID("_SSRPrevVP");
            public static readonly int DenoiseStep = Shader.PropertyToID("_SSRDenoiseStep");
        }

        struct DrawItem { public Mesh mesh; public Matrix4x4 matrix; public int sub; public Material mat; public float reflectivity, smoothOverride; }
        sealed class MaskData { public readonly List<DrawItem> items = new List<DrawItem>(); }
        static readonly MaterialPropertyBlock s_Mpb = new MaterialPropertyBlock();

        sealed class FullscreenData
        {
            public TextureHandle src;
            public Material mat;
            public int pass;
            public SSRResolvedParams p;
            public int frame;
            public bool useMask, useNormalsMask, hasMotion, historyValid, hasNormals;
            public int debugView;
            public RTHandle history;
            public Vector4 texel;
            public TextureHandle hiz;
            public Vector4 hizDims;
            public int hizMaxMip;
            public TextureHandle hit;
            public Matrix4x4 prevVP;
            public bool hitReproj;
            public float denoiseStep;
        }

        sealed class HiZData
        {
            public Material mat;
            public TextureHandle hiz, tmp;
            public int mips, width, height;
        }

        readonly SSRHistory _history = new SSRHistory();
        Material _ssr;
        SSRMaskMaterials _maskMats;
        readonly Plane[] _planes = new Plane[6];
        readonly List<Material> _matScratch = new List<Material>();
        SSRSettings _settings;
        SSRResolvedParams _p;
        bool _warned, _hasNormals;
        TextureHandle _hiz;          // set per frame in RecordRenderGraph, copied into every pass by Fill
        Vector4 _hizDims;
        int _hizMaxMip;
        TextureHandle _hit;          // R16F surface->hit distance written by Trace, read by Resolve
        Matrix4x4 _prevVP;
        bool _hitReproj;

        public SSRRenderPass()
        {
            // Opaque texture (Copy Color) is guaranteed to exist by BeforeRenderingTransparents. Verify in Render Graph Viewer.
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        public void Setup(Material ssr, Shader maskShader, SSRSettings settings, in SSRResolvedParams p)
        {
            _ssr = ssr; _settings = settings; _p = p;
            if (maskShader != null && (_maskMats == null || _maskMats.Shader != maskShader))
            { _maskMats?.Dispose(); _maskMats = new SSRMaskMaterials(maskShader); }
        }

        public void Dispose() { _history.Dispose(); _maskMats?.Dispose(); _maskMats = null; }

        public override void RecordRenderGraph(RenderGraph rg, ContextContainer frame)
        {
            if (_ssr == null || !_p.enabled) return;

            var res = frame.Get<UniversalResourceData>();
            var cam = frame.Get<UniversalCameraData>();
            if (res.isActiveTargetBackBuffer)
            {
                if (!_warned) { _warned = true; Debug.LogWarning("[SSR] Camera renders straight to the backbuffer; skipping."); }
                return;
            }

            bool hasDepth = res.cameraDepthTexture.IsValid();
            _hasNormals = res.cameraNormalsTexture.IsValid();
            bool hasOpaque = res.cameraOpaqueTexture.IsValid();
            if (!hasDepth)
            {
                if (!_warned) { _warned = true; Debug.LogWarning($"[SSR] Camera depth texture unavailable (depth={hasDepth}, normals={_hasNormals}, opaque={hasOpaque}); skipping."); }
                return;
            }

            if (_settings.surfaceMode == SSRSurfaceMode.NormalsAlpha && !_hasNormals)
            {
                if (!_warned) { _warned = true; Debug.LogWarning("[SSR] NormalsAlpha mode needs the normals texture; skipping."); }
                return;
            }

            TextureHandle opaque = res.cameraOpaqueTexture;
            if (!hasOpaque)
            {
                if (cam.cameraTargetDescriptor.msaaSamples > 1)
                {
                    if (!_warned) { _warned = true; Debug.LogWarning("[SSR] No opaque texture and camera uses MSAA; enable Opaque Texture in the URP asset or disable MSAA. Skipping."); }
                    return;
                }
                RenderTextureDescriptor cd = cam.cameraTargetDescriptor;
                cd.depthBufferBits = 0; cd.msaaSamples = 1;
                opaque = UniversalRenderer.CreateRenderGraphTexture(rg, cd, "_SSROpaqueCopy", false, FilterMode.Bilinear);
                rg.AddCopyPass(res.activeColorTexture, opaque, "SSR Copy Color");
            }
            if (!_hasNormals && !_warned) { _warned = true; Debug.LogWarning("[SSR] Normals texture unavailable; reconstructing normals from depth (lower quality)."); }

            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
            {
                if (!_warned) { _warned = true; Debug.LogWarning("[SSR] The Hi-Z depth pyramid needs CopyTexture support; skipping."); }
                return;
            }

            bool useMask = _settings.surfaceMode == SSRSurfaceMode.Registered;
            if (useMask && SSRSurfaceRegistry.Count == 0) return;     // nothing registered => nothing to reflect
            if (useMask && _maskMats == null)
            {
                if (!_warned) { _warned = true; Debug.LogWarning("[SSR] Mask shader 'Hidden/SSR/Mask' not found/compiled; skipping."); }
                return;
            }

            TextureHandle motion = res.motionVectorColor;
            bool hasMotion = motion.IsValid();

            RenderTextureDescriptor baseDesc = cam.cameraTargetDescriptor;
            int w = Mathf.Max(1, Mathf.RoundToInt(baseDesc.width * _p.resolutionScale));
            int h = Mathf.Max(1, Mathf.RoundToInt(baseDesc.height * _p.resolutionScale));

            RenderTextureDescriptor desc = baseDesc;
            desc.width = w; desc.height = h; desc.depthBufferBits = 0; desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;

            // ---- Hi-Z: min eye depth pyramid at full resolution ----
            _hiz = RecordDepthPyramid(rg, res, baseDesc.width, baseDesc.height, out int hizMips);
            _hizDims = new Vector4(baseDesc.width, baseDesc.height, 1f / baseDesc.width, 1f / baseDesc.height);
            _hizMaxMip = hizMips - 1;

            // ---- history (imported, persistent) ----
            Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cam.camera.projectionMatrix, true) * cam.camera.worldToCameraMatrix;   // Camera.projectionMatrix is unjittered in URP (TAA jitter is applied to URP's own matrices), so jitter does not leak into the reprojection
            bool histValid = _history.Acquire(cam.camera, w, h, viewProj, out RTHandle histReadRT, out RTHandle histWriteRT, out _prevVP);
            TextureHandle histRead = rg.ImportTexture(histReadRT);
            TextureHandle histWrite = rg.ImportTexture(histWriteRT);

            // ---- optional mask ----
            TextureHandle mask = TextureHandle.nullHandle;
            if (useMask) mask = RecordMask(rg, frame, baseDesc);

            TextureHandle trace = UniversalRenderer.CreateRenderGraphTexture(rg, desc, "_SSRTrace", false, FilterMode.Bilinear);

            bool hitFmtOk = SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.Render);
            if (_p.hitReprojection && !hitFmtOk && !_warned) { _warned = true; Debug.LogWarning("[SSR] R16_SFloat render targets unsupported; hit-point reprojection disabled."); }
            _hitReproj = _p.hitReprojection && hitFmtOk;
            RenderTextureDescriptor hitDesc = desc;
            hitDesc.graphicsFormat = hitFmtOk ? GraphicsFormat.R16_SFloat : GraphicsFormat.R16G16B16A16_SFloat;   // fallback only keeps the MRT bindable
            _hit = UniversalRenderer.CreateRenderGraphTexture(rg, hitDesc, "_SSRTraceHit", false, FilterMode.Point);
            Vector4 texel = new Vector4(w, h, 1f / w, 1f / h);
            int frameIdx = _p.jitterPerFrame ? Time.frameCount : 0;

            // ---- Trace ----
            using (var b = rg.AddRasterRenderPass<FullscreenData>("SSR Trace", out var d))
            {
                Fill(d, opaque, kPassTrace, frameIdx, useMask, hasMotion, histValid, null, texel);
                b.UseTexture(opaque, AccessFlags.Read);
                b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                b.UseTexture(_hiz, AccessFlags.Read);
                if (_hasNormals) b.UseTexture(res.cameraNormalsTexture, AccessFlags.Read);
                if (useMask) b.UseTexture(mask, AccessFlags.Read);
                b.SetRenderAttachment(trace, 0, AccessFlags.Write);
                b.SetRenderAttachment(_hit, 1, AccessFlags.Write);
                b.SetRenderFunc((FullscreenData x, RasterGraphContext c) => Run(x, c));
            }

            // ---- Resolve: temporal accumulation (hit-point reprojection) + luminance-delta limiter -> writes history target ----
            using (var b = rg.AddRasterRenderPass<FullscreenData>("SSR Resolve", out var d))
            {
                Fill(d, trace, kPassResolve, frameIdx, useMask, hasMotion, histValid, histReadRT, texel);
                b.AllowGlobalStateModification(true);   // Run() sets _SSRPrevVP through the command buffer
                b.UseTexture(trace, AccessFlags.Read);
                b.UseTexture(_hit, AccessFlags.Read);
                b.UseTexture(histRead, AccessFlags.Read);
                if (useMask) b.UseTexture(mask, AccessFlags.Read);
                if (_settings.surfaceMode == SSRSurfaceMode.NormalsAlpha) b.UseTexture(res.cameraNormalsTexture, AccessFlags.Read);
                b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                if (hasMotion) b.UseTexture(motion, AccessFlags.Read);
                b.SetRenderAttachment(histWrite, 0, AccessFlags.Write);
                b.SetRenderFunc((FullscreenData x, RasterGraphContext c) => Run(x, c));
            }

            // ---- Denoise: edge-aware a-trous on the accumulated result (history itself stays unfiltered) ----
            TextureHandle denoised = RecordDenoise(rg, res, histWrite, mask, desc, _p.denoiseIterations, frameIdx, useMask, hasMotion, histValid, texel);

            // ---- Composite: premultiplied blend into camera color ----
            using (var b = rg.AddRasterRenderPass<FullscreenData>("SSR Composite", out var d))
            {
                Fill(d, denoised, kPassComposite, frameIdx, useMask, hasMotion, histValid, null, texel);
                b.UseTexture(denoised, AccessFlags.Read);
                b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);                       // depth-aware upsample + full-res mask
                if (useMask) b.UseTexture(mask, AccessFlags.Read);
                if (_hasNormals) b.UseTexture(res.cameraNormalsTexture, AccessFlags.Read);                // NormalsAlpha mask + normal-aware upsample
                b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.ReadWrite);
                b.AllowPassCulling(false);
                b.SetRenderFunc((FullscreenData x, RasterGraphContext c) => Run(x, c));
            }

            // ---- Debug view: overwrites camera color with the decoded SSR inputs ----
            if (_settings.debugView != SSRDebugView.Off)
            {
                if (_settings.debugView == SSRDebugView.RawNormalsAlpha && !_hasNormals)
                {
                    if (!_warned) { _warned = true; Debug.LogWarning("[SSR] Debug view RawNormalsAlpha needs the normals texture; skipping."); }
                    return;
                }

                using (var b = rg.AddRasterRenderPass<FullscreenData>("SSR Debug", out var d))
                {
                    Fill(d, opaque, kPassDebug, frameIdx, useMask, hasMotion, histValid, null, texel);
                    b.UseTexture(opaque, AccessFlags.Read);
                    b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                    if (_hasNormals) b.UseTexture(res.cameraNormalsTexture, AccessFlags.Read);
                    if (useMask) b.UseTexture(mask, AccessFlags.Read);
                    b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc((FullscreenData x, RasterGraphContext c) => Run(x, c));
                }
            }
        }

        // N chained a-trous draws (stride 1, 2, 4, ...). Returns the last target, or src when iterations == 0.
        TextureHandle RecordDenoise(RenderGraph rg, UniversalResourceData res, TextureHandle src, TextureHandle mask, RenderTextureDescriptor desc,
                                    int iterations, int frameIdx, bool useMask, bool hasMotion, bool histValid, Vector4 texel)
        {
            TextureHandle current = src;
            for (int i = 0; i < iterations; i++)
            {
                TextureHandle dst = UniversalRenderer.CreateRenderGraphTexture(rg, desc, "_SSRDenoise", false, FilterMode.Bilinear);
                using (var b = rg.AddRasterRenderPass<FullscreenData>("SSR Denoise", out var d))
                {
                    Fill(d, current, kPassDenoise, frameIdx, useMask, hasMotion, histValid, null, texel);
                    d.denoiseStep = 1 << i;
                    b.AllowGlobalStateModification(true);   // Run() sets _SSRDenoiseStep through the command buffer
                    b.UseTexture(current, AccessFlags.Read);
                    b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                    if (_hasNormals) b.UseTexture(res.cameraNormalsTexture, AccessFlags.Read);
                    if (useMask) b.UseTexture(mask, AccessFlags.Read);
                    b.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    b.SetRenderFunc((FullscreenData x, RasterGraphContext c) => Run(x, c));
                }
                current = dst;
            }
            return current;
        }

        // Mip 0 = linear eye depth, mip n = min of the 2x2 below it. Each reduce draw goes into a temp target and is copied into
        // the mip, so no draw reads and writes the same texture (D3D11-safe). [GAP] verify the unsafe pass + CopyTexture on your exact 6000.x.
        TextureHandle RecordDepthPyramid(RenderGraph rg, UniversalResourceData res, int w, int h, out int mipCount)
        {
            mipCount = 1;
            while (mipCount < kHiZMaxMips && (Mathf.Min(w, h) >> mipCount) >= 8) mipCount++;

            var full = new TextureDesc(w, h)
            {
                colorFormat = GraphicsFormat.R32_SFloat, useMipMap = true, autoGenerateMips = false,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "_SSRHiZ"
            };
            var half = new TextureDesc(Mathf.Max(1, w >> 1), Mathf.Max(1, h >> 1))
            {
                colorFormat = GraphicsFormat.R32_SFloat, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "_SSRHiZTmp"
            };
            TextureHandle hiz = rg.CreateTexture(full);
            TextureHandle tmp = rg.CreateTexture(half);

            using (var b = rg.AddUnsafePass<HiZData>("SSR Depth Pyramid", out var d))
            {
                d.mat = _ssr; d.hiz = hiz; d.tmp = tmp; d.mips = mipCount; d.width = w; d.height = h;
                b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                b.UseTexture(hiz, AccessFlags.Write);
                b.UseTexture(tmp, AccessFlags.Write);
                b.SetRenderFunc((HiZData x, UnsafeGraphContext c) => RunDepthPyramid(x, c));
            }
            return hiz;
        }

        static void RunDepthPyramid(HiZData x, UnsafeGraphContext c)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(c.cmd);
            RTHandle hiz = x.hiz, tmp = x.tmp;

            cmd.SetRenderTarget(hiz, 0, CubemapFace.Unknown, 0);
            Blitter.BlitTexture(cmd, tmp, k_ScaleBias, x.mat, kPassHiZBase);       // source unused by the base pass

            int sw = x.width, sh = x.height;
            for (int m = 1; m < x.mips; m++)
            {
                int dw = Mathf.Max(1, sw >> 1), dh = Mathf.Max(1, sh >> 1);
                cmd.SetGlobalVector(Ids.HiZSrc, new Vector4(m - 1, sw, sh, 0f));    // recorded in order, unlike material properties
                cmd.SetRenderTarget(tmp, 0, CubemapFace.Unknown, 0);
                cmd.SetViewport(new Rect(0, 0, dw, dh));
                Blitter.BlitTexture(cmd, hiz, k_ScaleBias, x.mat, kPassHiZReduce);
                cmd.CopyTexture(tmp, 0, 0, 0, 0, dw, dh, hiz, 0, m, 0, 0);
                sw = dw; sh = dh;
            }
        }

        TextureHandle RecordMask(RenderGraph rg, ContextContainer frame, RenderTextureDescriptor baseDesc)
        {
            var cam = frame.Get<UniversalCameraData>();
            var res = frame.Get<UniversalResourceData>();

            RenderTextureDescriptor md = baseDesc;
            md.depthBufferBits = 0; md.msaaSamples = 1; md.graphicsFormat = GraphicsFormat.R8G8_UNorm;   // R reflectivity, G smoothness
            TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(rg, md, "_SSRMask", true, FilterMode.Point);

            _maskMats.Purge();
            GeometryUtility.CalculateFrustumPlanes(cam.camera, _planes);
            bool useSmoothness = _p.smoothnessInfluence > 0f;

            using (var b = rg.AddRasterRenderPass<MaskData>("SSR Mask", out var d))
            {
                d.items.Clear();
                var all = SSRSurfaceRegistry.Items;
                for (int k = 0; k < all.Count; k++)
                {
                    SSRSurface surf = all[k];
                    MeshRenderer r = surf.Renderer; MeshFilter f = surf.Filter;
                    if (r == null || f == null || !r.enabled) continue;
                    Mesh mesh = f.sharedMesh; if (mesh == null) continue;
                    if (!GeometryUtility.TestPlanesAABB(_planes, r.bounds)) continue;

                    r.GetSharedMaterials(_matScratch);
                    int n = Mathf.Min(_matScratch.Count, mesh.subMeshCount);
                    for (int i = 0; i < n; i++)
                    {
                        Material m = _maskMats.Get(_matScratch[i], _settings.fallbackSmoothness);
                        if (m == null) continue;
                        d.items.Add(new DrawItem
                        {
                            mesh = mesh, matrix = r.localToWorldMatrix, sub = i, mat = m,
                            reflectivity = _p.reflectivity * surf.reflectivityScale,
                            smoothOverride = !useSmoothness ? 1f : (surf.overrideSmoothness ? surf.smoothness : -1f),
                        });
                    }
                }

                b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);   // shader depth-tests manually
                b.SetRenderAttachment(mask, 0, AccessFlags.Write);
                b.SetGlobalTextureAfterPass(mask, Ids.Mask);
                b.SetRenderFunc((MaskData x, RasterGraphContext c) => RunMask(x, c));
            }
            return mask;
        }

        static void RunMask(MaskData x, RasterGraphContext c)
        {
            for (int i = 0; i < x.items.Count; i++)
            {
                DrawItem it = x.items[i];
                s_Mpb.SetFloat(Ids.MaskValue, it.reflectivity);
                s_Mpb.SetFloat(Ids.SmoothOverride, it.smoothOverride);
                c.cmd.DrawMesh(it.mesh, it.matrix, it.mat, it.sub, 0, s_Mpb);
            }
        }

        void Fill(FullscreenData d, TextureHandle src, int pass, int frameIdx, bool useMask, bool hasMotion,
                  bool histValid, RTHandle history, Vector4 texel)
        {
            d.src = src; d.mat = _ssr; d.pass = pass; d.p = _p; d.frame = frameIdx;
            d.useMask = useMask; d.useNormalsMask = _settings.surfaceMode == SSRSurfaceMode.NormalsAlpha; d.hasMotion = hasMotion; d.historyValid = histValid;
            d.history = history; d.texel = texel; d.hasNormals = _hasNormals; d.debugView = (int)_settings.debugView;
            d.hiz = _hiz; d.hizDims = _hizDims; d.hizMaxMip = _hizMaxMip;
            d.hit = _hit; d.prevVP = _prevVP; d.hitReproj = _hitReproj; d.denoiseStep = 1f;
        }

        static void Run(FullscreenData x, RasterGraphContext c)
        {
            Material m = x.mat; ref readonly SSRResolvedParams p = ref x.p;
            CoreUtils.SetKeyword(m, "_SSR_USE_MASK", x.useMask);
            CoreUtils.SetKeyword(m, "_SSR_MASK_NORMALS_A", x.useNormalsMask);
            CoreUtils.SetKeyword(m, "_SSR_NO_MOTION", !x.hasMotion);
            CoreUtils.SetKeyword(m, "_SSR_NO_NORMALS", !x.hasNormals);

            m.SetVector(Ids.Texel, x.texel);
            m.SetVector(Ids.HiZDims, x.hizDims);          // identical in every pass: material values are read when the command buffer runs, not per pass
            m.SetFloat(Ids.HiZMaxMip, x.hizMaxMip);
            m.SetFloat(Ids.MaxDistance, p.maxDistance);
            m.SetFloat(Ids.Thickness, p.thickness);
            m.SetFloat(Ids.MaxSteps, p.maxSteps);
            m.SetFloat(Ids.RefineSteps, p.refineSteps);
            m.SetFloat(Ids.EdgeFade, p.edgeFade);
            m.SetFloat(Ids.FresnelPower, p.fresnelPower);
            m.SetFloat(Ids.MinReflectance, p.minReflectance);
            m.SetFloat(Ids.Reflectivity, p.reflectivity);
            m.SetFloat(Ids.Frame, x.frame);
            m.SetFloat(Ids.Feedback, p.temporalFeedback);
            m.SetFloat(Ids.HistoryValid, x.historyValid ? 1f : 0f);
            m.SetFloat(Ids.SafeReset, p.resetHistoryToZero ? 1f : 0f);
            m.SetFloat(Ids.MaxDelta, p.maxDeltaPerFrame);
            m.SetFloat(Ids.MaxLuma, p.maxLuminance);
            m.SetFloat(Ids.Intensity, p.intensity);
            m.SetFloat(Ids.MaxOpacity, p.maxOpacity);
            m.SetFloat(Ids.Saturation, p.saturation);
            m.SetFloat(Ids.SmoothMin, p.smoothnessThreshold);
            m.SetFloat(Ids.SmoothRamp, p.smoothnessRamp);
            m.SetFloat(Ids.SmoothInfluence, p.smoothnessInfluence);
            m.SetFloat(Ids.Blur, p.roughnessBlur);
            m.SetFloat(Ids.DebugView, x.debugView);
            m.SetFloat(Ids.DistFadePower, p.distanceFadePower);
            m.SetFloat(Ids.NormalSharpness, p.normalSharpness);
            m.SetFloat(Ids.HitReproj, x.hitReproj ? 1f : 0f);
            // per-pass values go through the command buffer (recorded in order); material values are read when it runs, so only the last would win
            if (x.pass == kPassDenoise) c.cmd.SetGlobalFloat(Ids.DenoiseStep, x.denoiseStep);
            if (x.pass == kPassResolve) { c.cmd.SetGlobalMatrix(Ids.PrevVP, x.prevVP); RTHandle hitRT = x.hit; m.SetTexture(Ids.TraceHit, hitRT); }
            if (x.history != null) m.SetTexture(Ids.History, x.history);
            if (x.pass == kPassTrace) { RTHandle hizRT = x.hiz; m.SetTexture(Ids.HiZ, hizRT); }

            // [GAP] TextureHandle -> RTHandle implicit conversion as used in Unity's Blit samples; verify on your exact 6000.x.
            Blitter.BlitTexture(c.cmd, x.src, k_ScaleBias, m, x.pass);
        }
    }
}
