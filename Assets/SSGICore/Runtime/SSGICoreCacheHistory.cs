using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGICore
{
    /// <summary>
    /// Per-camera radiance cache history: a double-buffered 3D texture (6 cube faces packed along x) and the
    /// fixed-point accumulation buffer that the inject kernel fills and the resolve kernel drains.
    /// </summary>
    public sealed class SSGICoreCacheHistory : CameraHistoryItem
    {
        public const int GridSize = 32;
        public const int Faces = 6;

        int m_Tex;
        int m_LastFrame = -1000;
        float m_CellSize;
        Vector3Int m_Base;
        GraphicsBuffer m_Acc;

        public GraphicsBuffer Accumulation { get { return m_Acc; } }

        public override void OnCreate(BufferedRTHandleSystem owner, uint typeId)
        {
            base.OnCreate(owner, typeId);
            m_Tex = MakeId(0);
        }

        public RTHandle GetCurrent() { return GetCurrentFrameRT(m_Tex); }
        public RTHandle GetPrevious() { return GetPreviousFrameRT(m_Tex); }

        /// <summary>Allocates the buffers if needed. Returns false when the 3D history texture could not be created.</summary>
        public bool TryUpdate()
        {
            if (GetCurrentFrameRT(m_Tex) != null && m_Acc != null)
                return true;

            Reset();

            var desc = new RenderTextureDescriptor(GridSize * Faces, GridSize, GraphicsFormat.R16G16B16A16_SFloat, 0);
            desc.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            desc.volumeDepth = GridSize;
            desc.enableRandomWrite = true;
            desc.msaaSamples = 1;
            desc.useMipMap = false;
            AllocHistoryFrameRT(m_Tex, 2, ref desc, "SSGICore_RadianceCache");

            if (GetCurrentFrameRT(m_Tex) == null)
                return false;

            int count = GridSize * GridSize * GridSize * Faces * 4;
            m_Acc = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(uint));
            m_Acc.SetData(new uint[count]); // start from zero
            return true;
        }

        /// <summary>Grid base change since the previous frame, valid only if that frame wrote the cache with the same cell size.</summary>
        public bool TryGetShift(int frame, float cellSize, Vector3Int newBase, out Vector3Int delta)
        {
            bool valid = frame - m_LastFrame >= 0 && frame - m_LastFrame <= 1 && Mathf.Approximately(cellSize, m_CellSize);
            delta = valid ? newBase - m_Base : Vector3Int.zero;
            return valid;
        }

        public void MarkWritten(int frame, float cellSize, Vector3Int gridBase)
        {
            m_LastFrame = frame;
            m_CellSize = cellSize;
            m_Base = gridBase;
        }

        public override void Reset()
        {
            ReleaseHistoryFrameRT(m_Tex);
            if (m_Acc != null)
            {
                m_Acc.Release();
                m_Acc = null;
            }
            m_LastFrame = -1000;
        }
    }
}
