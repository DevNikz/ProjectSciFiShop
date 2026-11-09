using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SSGICore
{
    /// <summary>
    /// Per-frame hand-off from the early depth prep pass to the main SSGICore pass (URP frame data).
    /// </summary>
    internal sealed class SSGICoreFrameData : ContextItem
    {
        public bool ready;
        public int fullW, fullH, normalMode;
        public TextureHandle normalsRes, hzbAtlas;

        public override void Reset()
        {
            ready = false;
            fullW = fullH = normalMode = 0;
            normalsRes = default;
            hzbAtlas = default;
        }
    }
}
