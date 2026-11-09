using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGICore
{
    /// <summary>
    /// Per-camera ReSTIR-lite reservoir history (trace resolution, double buffered):
    /// A = sample world position + M (RGBA32F), B = radiance + contribution weight W, C = sample normal (RGBA16F).
    /// </summary>
    public sealed class SSGICoreRestirHistory : CameraHistoryItem
    {
        int m_A, m_B, m_C;
        int m_Width, m_Height;
        int m_LastFrame = -1000;

        public override void OnCreate(BufferedRTHandleSystem owner, uint typeId)
        {
            base.OnCreate(owner, typeId);
            m_A = MakeId(0);
            m_B = MakeId(1);
            m_C = MakeId(2);
        }

        public RTHandle GetCurrent(int index) { return GetCurrentFrameRT(IdOf(index)); }
        public RTHandle GetPrevious(int index) { return GetPreviousFrameRT(IdOf(index)); }

        int IdOf(int index) { return index == 0 ? m_A : (index == 1 ? m_B : m_C); }

        public void Update(int width, int height)
        {
            if (width == m_Width && height == m_Height && GetCurrentFrameRT(m_A) != null)
                return;

            Reset();

            var full = new RenderTextureDescriptor(width, height, GraphicsFormat.R32G32B32A32_SFloat, 0);
            full.enableRandomWrite = true;
            full.msaaSamples = 1;
            full.useMipMap = false;
            var half = new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_SFloat, 0);
            half.enableRandomWrite = true;
            half.msaaSamples = 1;
            half.useMipMap = false;

            AllocHistoryFrameRT(m_A, 2, ref full, "SSGICore_ResA");
            AllocHistoryFrameRT(m_B, 2, ref half, "SSGICore_ResB");
            AllocHistoryFrameRT(m_C, 2, ref half, "SSGICore_ResC");

            m_Width = width;
            m_Height = height;
            m_LastFrame = -1000;
        }

        /// <summary>True when the previous buffers were written on the immediately preceding frame.</summary>
        public bool IsPreviousValid(int frame)
        {
            int delta = frame - m_LastFrame;
            return delta >= 0 && delta <= 1;
        }

        public void MarkWritten(int frame) { m_LastFrame = frame; }

        public override void Reset()
        {
            ReleaseHistoryFrameRT(m_A);
            ReleaseHistoryFrameRT(m_B);
            ReleaseHistoryFrameRT(m_C);
            m_Width = 0;
            m_Height = 0;
            m_LastFrame = -1000;
        }
    }
}
