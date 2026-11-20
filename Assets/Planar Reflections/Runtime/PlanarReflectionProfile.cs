using UnityEngine;

namespace Studio.PlanarReflections
{
    [CreateAssetMenu(menuName = "Planar Reflections/Profile", fileName = "PlanarReflectionProfile")]
    public sealed class PlanarReflectionProfile : ScriptableObject
    {
        const float k_FadeDisabledStart = 1e9f;
        const int k_MainDepthBits = 24;
        // The request destination stays single-sample. URP owns the MSAA color target and resolves it into this texture.
        const int k_MainSamples = 1;

        static PlanarReflectionProfile s_Default;

        [Header("Resolution")]
        [SerializeField, Range(0.25f, 1f)] float m_ResolutionScale = 1f;
        [SerializeField] PlanarReflectionFormat m_Format = PlanarReflectionFormat.Hdr;
        [Tooltip("Off or on. Any value but Off enables URP MSAA for the reflection camera; the sample count comes from the URP asset (needs 2 or more). Ignored while STP is active.")]
        [SerializeField] PlanarReflectionMsaa m_Msaa = PlanarReflectionMsaa.Off;

        [Header("Culling")]
        [Tooltip("Layers the mirrored camera renders. Defaults to everything except Water and UI.")]
        [SerializeField] LayerMask m_LayerMask = ~((1 << 4) | (1 << 5));
        [SerializeField, Range(0f, 0.5f)] float m_ClipOffset = 0.07f;

        [Header("Fade")]
        [Tooltip("Per-pixel fade by distance to the camera. Disabled while End is not greater than Start.")]
        [SerializeField, Min(0f)] float m_FadeStartDistance;
        [SerializeField, Min(0f)] float m_FadeEndDistance;

        [Header("Roughness")]
        [SerializeField] PlanarReflectionBlurMode m_BlurMode = PlanarReflectionBlurMode.Mips;
        [Tooltip("Mips mode: highest LOD that roughness 1 reaches.")]
        [SerializeField, Range(0f, 8f)] float m_RoughnessLodRange = 4f;
        [Tooltip("Blurred mode: Kawase passes (first pass also downsamples to half-res).")]
        [SerializeField, Range(1, 6)] int m_BlurIterations = 3;

        [Header("Budget")]
        [Tooltip("Render shadows in the reflection. Off is the biggest saving in Forward+.")]
        [SerializeField] bool m_RenderShadows;
        [Tooltip("Reflection far clip in meters. 0 = same as the source camera. Also caps shadow distance and the lights visible to the reflection.")]
        [SerializeField, Min(0f)] float m_FarClip;
        [Tooltip("Scales QualitySettings.lodBias while the reflection renders. 1 = unchanged. Below 1 picks coarser LODs and can drop " +
                 "LOD groups with a Culled level (often characters) out of the reflection.")]
        [SerializeField, Range(0.1f, 1f)] float m_LodBiasScale = 1f;
        [Tooltip("Index in the URP asset's renderer list. -1 = the default renderer. Point it at a stripped budget renderer.")]
        [SerializeField, Min(-1)] int m_RendererIndex = -1;

        public static PlanarReflectionProfile Default
        {
            get
            {
                if (s_Default == null)
                {
                    s_Default = CreateInstance<PlanarReflectionProfile>();
                    s_Default.hideFlags = HideFlags.HideAndDontSave;
                }

                return s_Default;
            }
        }

        public float ResolutionScale => m_ResolutionScale;
        public PlanarReflectionFormat Format => m_Format;
        public PlanarReflectionMsaa Msaa => m_Msaa;
        public bool IsMsaaRequested => m_Msaa != PlanarReflectionMsaa.Off;
        public int LayerMask => m_LayerMask.value;
        public float ClipOffset => m_ClipOffset;
        public PlanarReflectionBlurMode BlurMode => m_BlurMode;
        public int BlurIterations => m_BlurIterations;
        public bool RenderShadows => m_RenderShadows;
        public float FarClip => m_FarClip;
        public float LodBiasScale => m_LodBiasScale;
        public int RendererIndex => m_RendererIndex;

        /// <summary>The far clip for the reflection camera: the source far clip unless a shorter one is set.</summary>
        public float GetFarClip(float nearClip, float sourceFarClip)
        {
            if (m_FarClip <= 0f)
                return sourceFarClip;

            float minimum = nearClip + 0.1f;
            return Mathf.Clamp(m_FarClip, minimum, Mathf.Max(sourceFarClip, minimum));
        }

        public float GetLodBias(float currentLodBias)
        {
            return currentLodBias * m_LodBiasScale;
        }

        internal PlanarReflectionTextureSpec GetMainTextureSpec(int sourceWidth, int sourceHeight)
        {
            return new PlanarReflectionTextureSpec(
                Mathf.Max(1, Mathf.RoundToInt(sourceWidth * m_ResolutionScale)),
                Mathf.Max(1, Mathf.RoundToInt(sourceHeight * m_ResolutionScale)),
                k_MainDepthBits,
                k_MainSamples,
                GetRenderTextureFormat(),
                m_BlurMode == PlanarReflectionBlurMode.Mips,
                false);
        }

        internal PlanarReflectionTextureSpec GetBlurredTextureSpec(in PlanarReflectionTextureSpec main, bool useCompute)
        {
            return new PlanarReflectionTextureSpec(
                Mathf.Max(1, main.Width / 2),
                Mathf.Max(1, main.Height / 2),
                0,
                1,
                useCompute ? RenderTextureFormat.ARGBHalf : main.Format,
                false,
                useCompute);
        }

        /// <summary>x: fade start, y: 1 / fade range, z: max roughness LOD, w: blur blend (0 or 1).</summary>
        public Vector4 GetShaderParams(bool hasMips, bool hasBlurred)
        {
            bool isFadeEnabled = m_FadeEndDistance > m_FadeStartDistance;
            float fadeStart = isFadeEnabled ? m_FadeStartDistance : k_FadeDisabledStart;
            float fadeInverseRange = isFadeEnabled ? 1f / (m_FadeEndDistance - m_FadeStartDistance) : 0f;

            return new Vector4(
                fadeStart,
                fadeInverseRange,
                hasMips ? m_RoughnessLodRange : 0f,
                hasBlurred ? 1f : 0f);
        }

        RenderTextureFormat GetRenderTextureFormat()
        {
            switch (m_Format)
            {
                case PlanarReflectionFormat.HdrHalf: return RenderTextureFormat.ARGBHalf;
                case PlanarReflectionFormat.Ldr: return RenderTextureFormat.Default;
                default: return RenderTextureFormat.DefaultHDR;
            }
        }
    }
}
