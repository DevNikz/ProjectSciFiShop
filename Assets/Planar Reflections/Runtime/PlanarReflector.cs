using UnityEngine;

namespace Studio.PlanarReflections
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Planar Reflections/Planar Reflector")]
    public sealed class PlanarReflector : MonoBehaviour
    {
        static readonly int s_ReflectionTextureId = Shader.PropertyToID("_PlanarReflectionTex");
        static readonly int s_BlurTextureId = Shader.PropertyToID("_PlanarReflectionBlurTex");
        static readonly int s_ParamsId = Shader.PropertyToID("_PlanarReflectionParams");

        [Tooltip("Quality settings. Falls back to built-in defaults when empty.")]
        [SerializeField] PlanarReflectionProfile m_Profile;

        [Tooltip("Renderers that sample the reflection. Defaults to this object's renderers.")]
        [SerializeField] Renderer[] m_TargetRenderers;

        [Tooltip("Flip culling winding while rendering the mirrored camera. Turn off if the reflection looks inside-out.")]
        [SerializeField] bool m_InvertCulling = true;

        [Tooltip("Skip this reflector when the camera is farther than this from its bounds. 0 = unlimited.")]
        [SerializeField, Min(0f)] float m_MaxDistance = 100f;

        [Tooltip("Re-render every N frames per camera. 1 = every frame. Ignored in Edit mode.")]
        [SerializeField, Range(1, 8)] int m_UpdateInterval = 1;

        MaterialPropertyBlock m_Block;
        bool[] m_PreviousForceRenderingOff;

        public PlanarReflectionProfile Profile => m_Profile != null ? m_Profile : PlanarReflectionProfile.Default;
        public Vector3 PlanePosition => transform.position;
        public Vector3 PlaneNormal => transform.up;
        public bool InvertCulling => m_InvertCulling;
        public float MaxDistance => m_MaxDistance;
        public int UpdateInterval => m_UpdateInterval;

        void Reset()
        {
            m_TargetRenderers = GetComponentsInChildren<Renderer>();
        }

        void OnEnable()
        {
            if (m_TargetRenderers == null || m_TargetRenderers.Length == 0)
                m_TargetRenderers = GetComponentsInChildren<Renderer>();

            // Per-object motion makes URP draw the shader's MotionVectors pass (zero motion) for STP/TAA.
            for (int i = 0; i < m_TargetRenderers.Length; i++)
            {
                if (m_TargetRenderers[i] != null)
                    m_TargetRenderers[i].motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            }

            PlanarReflectionManager.Register(this);
        }

        void OnDisable()
        {
            PlanarReflectionManager.Unregister(this);
            BindReflection(Texture2D.blackTexture, Texture2D.blackTexture, Vector4.zero);
        }

        /// <summary>
        /// Hides this reflector's own renderers while the mirrored camera renders, so they never reflect
        /// themselves. This replaces excluding the reflector's layer, which also hid everything sharing it.
        /// </summary>
        public void SetHiddenForReflection(bool isHidden)
        {
            if (m_TargetRenderers == null)
                return;

            if (isHidden && (m_PreviousForceRenderingOff == null || m_PreviousForceRenderingOff.Length != m_TargetRenderers.Length))
                m_PreviousForceRenderingOff = new bool[m_TargetRenderers.Length];

            for (int i = 0; i < m_TargetRenderers.Length; i++)
            {
                Renderer targetRenderer = m_TargetRenderers[i];
                if (targetRenderer == null)
                    continue;

                if (isHidden)
                {
                    m_PreviousForceRenderingOff[i] = targetRenderer.forceRenderingOff;
                    targetRenderer.forceRenderingOff = true;
                }
                else if (m_PreviousForceRenderingOff != null && i < m_PreviousForceRenderingOff.Length)
                {
                    targetRenderer.forceRenderingOff = m_PreviousForceRenderingOff[i];
                }
            }
        }

        public Bounds GetWorldBounds()
        {
            var bounds = new Bounds(PlanePosition, Vector3.zero);
            bool hasBounds = false;

            if (m_TargetRenderers == null)
                return bounds;

            for (int i = 0; i < m_TargetRenderers.Length; i++)
            {
                Renderer targetRenderer = m_TargetRenderers[i];
                if (targetRenderer == null)
                    continue;

                if (hasBounds)
                {
                    bounds.Encapsulate(targetRenderer.bounds);
                }
                else
                {
                    bounds = targetRenderer.bounds;
                    hasBounds = true;
                }
            }

            return bounds;
        }

        /// <summary>Camera is in front of the plane, within max distance, and sees the bounds.</summary>
        public bool IsVisibleTo(Camera sourceCamera, Plane[] frustumPlanes)
        {
            Vector3 cameraPosition = sourceCamera.transform.position;
            if (Vector3.Dot(PlaneNormal, cameraPosition - PlanePosition) <= 0f)
                return false;

            Bounds bounds = GetWorldBounds();

            if (m_MaxDistance > 0f && bounds.SqrDistance(cameraPosition) > m_MaxDistance * m_MaxDistance)
                return false;

            return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
        }

        /// <summary>Approximate projected size: bigger and closer reflectors score higher.</summary>
        public float GetScreenWeight(Camera sourceCamera)
        {
            Bounds bounds = GetWorldBounds();
            float distance = Mathf.Sqrt(bounds.SqrDistance(sourceCamera.transform.position));
            return bounds.extents.magnitude / Mathf.Max(distance, 0.01f);
        }

        public void BindReflection(Texture reflection, Texture blurred, Vector4 shaderParams)
        {
            if (m_TargetRenderers == null)
                return;

            m_Block ??= new MaterialPropertyBlock();

            for (int i = 0; i < m_TargetRenderers.Length; i++)
            {
                Renderer targetRenderer = m_TargetRenderers[i];
                if (targetRenderer == null)
                    continue;

                targetRenderer.GetPropertyBlock(m_Block);
                m_Block.SetTexture(s_ReflectionTextureId, reflection);
                m_Block.SetTexture(s_BlurTextureId, blurred);
                m_Block.SetVector(s_ParamsId, shaderParams);
                targetRenderer.SetPropertyBlock(m_Block);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(10f, 0f, 10f));
            Gizmos.DrawLine(Vector3.zero, Vector3.up);
        }
    }
}
