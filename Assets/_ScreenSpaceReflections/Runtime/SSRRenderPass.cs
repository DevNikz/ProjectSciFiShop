using System.Collections.Generic;
// BLK-05  Render Graph passes (URP 17 / Unity 6). Reads SSRResolvedParams only; never reads designer settings for accessibility.
// Passes: [Mask] -> Trace -> Resolve (spatial + temporal + limiter) -> Composite (blend into active color) -> [Debug view].
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
        const int kPassTrace = 0, kPassResolve = 1, kPassComposite = 2, kPassDebug = 3;
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
        }

        readonly SSRHistory _history = new SSRHistory();
        Material _ssr;
        SSRMaskMaterials _maskMats;
        readonly Plane[] _planes = new Plane[6];
        readonly List<Material> _matScratch = new List<Material>();
        SSRSettings _settings;
        SSRResolvedParams _p;
        bool _warned, _hasNormals;

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

            // ---- history (imported, persistent) ----
            bool histValid = _history.Acquire(cam.camera, w, h, out RTHandle histReadRT, out RTHandle histWriteRT);
            TextureHandle histRead = rg.ImportTexture(histReadRT);
            TextureHandle histWrite = rg.ImportTexture(histWriteRT);

            // ---- optional mask ----
            TextureHandle mask = TextureHandle.nullHandle;
            if (useMask) mask = RecordMask(rg, frame, baseDesc);

            TextureHandle trace = UniversalRenderer.CreateRenderGraphTexture(rg, desc, "_SSRTrace", false, FilterMode.Bilinear);
            Vector4 texel = new Vector4(w, h, 1f / w, 1f / h);
            int frameIdx = _p.jitterPerFrame ? Time.frameCount : 0;

            // ---- Trace ----
            using (var b = rg.AddRasterRenderPass<FullscreenData>("SSR Trace", out var d))
            {
                Fill(d, opaque, kPassTrace, frameIdx, useMask, hasMotion, histValid, null, texel);
                b.UseTexture(opaque, AccessFlags.Read);
                b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                if (_hasNormals) b.UseTexture(res.cameraNormalsTexture, AccessFlags.Read);
                if (useMask) b.UseTexture(mask, AccessFlags.Read);
                b.SetRenderAttachment(trace, 0, AccessFlags.Write);
                b.SetRenderFunc((FullscreenData x, RasterGraphContext c) => Run(x, c));
            }

            // ---- Resolve: spatial filter + temporal + luminance-delta limiter -> writes history target ----
            using (var b = rg.AddRasterRenderPass<FullscreenData>("SSR Resolve", out var d))
            {
                Fill(d, trace, kPassResolve, frameIdx, useMask, hasMotion, histValid, histReadRT, texel);
                b.UseTexture(trace, AccessFlags.Read);
                b.UseTexture(histRead, AccessFlags.Read);
                if (useMask) b.UseTexture(mask, AccessFlags.Read);
                if (_settings.surfaceMode == SSRSurfaceMode.NormalsAlpha) b.UseTexture(res.cameraNormalsTexture, AccessFlags.Read);
                b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                if (hasMotion) b.UseTexture(motion, AccessFlags.Read);
                b.SetRenderAttachment(histWrite, 0, AccessFlags.Write);
                b.SetRenderFunc((FullscreenData x, RasterGraphContext c) => Run(x, c));
            }

            // ---- Composite: premultiplied blend into camera color ----
            using (var b = rg.AddRasterRenderPass<FullscreenData>("SSR Composite", out var d))
            {
                Fill(d, histWrite, kPassComposite, frameIdx, useMask, hasMotion, histValid, null, texel);
                b.UseTexture(histWrite, AccessFlags.Read);
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
        }

        static void Run(FullscreenData x, RasterGraphContext c)
        {
            Material m = x.mat; ref readonly SSRResolvedParams p = ref x.p;
            CoreUtils.SetKeyword(m, "_SSR_USE_MASK", x.useMask);
            CoreUtils.SetKeyword(m, "_SSR_MASK_NORMALS_A", x.useNormalsMask);
            CoreUtils.SetKeyword(m, "_SSR_NO_MOTION", !x.hasMotion);
            CoreUtils.SetKeyword(m, "_SSR_NO_NORMALS", !x.hasNormals);

            m.SetVector(Ids.Texel, x.texel);
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
            if (x.history != null) m.SetTexture(Ids.History, x.history);

            // [GAP] TextureHandle -> RTHandle implicit conversion as used in Unity's Blit samples; verify on your exact 6000.x.
            Blitter.BlitTexture(c.cmd, x.src, k_ScaleBias, m, x.pass);
        }
    }
}
