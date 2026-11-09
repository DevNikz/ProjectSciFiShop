using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SSGICore
{
    /// <summary>
    /// Builds resolved normals and the HZB right after the depth/normals prepass, on the async compute queue when
    /// available, so the work overlaps opaque shading. The main pass picks the result up via SSGICoreFrameData.
    /// Runs only when the DepthNormals prepass owns the camera depth (otherwise the main pass records the same work inline).
    /// </summary>
    internal sealed class SSGICorePrepPass : ScriptableRenderPass
    {
        readonly SSGIFrameConstants m_C;
        SSGICoreVolume m_Volume;
        bool m_Async;

        public SSGICorePrepPass(SSGIFrameConstants constants)
        {
            m_C = constants;
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
        }

        public void Setup(SSGICoreVolume volume, bool asyncCompute)
        {
            m_Volume = volume;
            m_Async = asyncCompute;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            SSGICoreVolume vol = m_Volume;
            if (vol == null || vol.normalSource.value == SSGICoreNormalSource.ReconstructFromDepth)
                return;
            if (!SSGICoreResources.TryLoad(out string _))
                return; // the main pass reports the error

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.camera.orthographic)
                return;

            TextureHandle depth = resourceData.cameraDepthTexture;
            TextureHandle normals = resourceData.cameraNormalsTexture;
            if (!depth.IsValid() || !normals.IsValid())
                return; // no DepthNormals prepass here: depth may still change before the main pass

            RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
            int fullW = camDesc.width;
            int fullH = camDesc.height;
            int normalMode = (int)vol.normalSource.value;

            Vector2Int atlas = SSGICorePass.FillGeometryConstants(m_C, cameraData, fullW, fullH, normalMode);
            SSGICorePass.RecordDepthPrep(renderGraph, m_C, depth, normals, fullW, fullH, atlas, m_Async,
                out TextureHandle normalsRes, out TextureHandle hzbAtlas);

            SSGICoreFrameData data = frameData.Create<SSGICoreFrameData>();
            data.fullW = fullW;
            data.fullH = fullH;
            data.normalMode = normalMode;
            data.normalsRes = normalsRes;
            data.hzbAtlas = hzbAtlas;
            data.ready = true;
        }
    }
}
