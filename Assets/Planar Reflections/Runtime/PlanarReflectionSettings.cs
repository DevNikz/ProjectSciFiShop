using UnityEngine;

namespace Studio.PlanarReflections
{
    /// <summary>
    /// Project-wide settings. Create one and place it at
    /// Resources/PlanarReflections/PlanarReflectionSettings. Defaults are used when absent.
    /// </summary>
    [CreateAssetMenu(menuName = "Planar Reflections/Settings", fileName = "PlanarReflectionSettings")]
    public sealed class PlanarReflectionSettings : ScriptableObject
    {
        const string k_ResourcePath = "PlanarReflections/PlanarReflectionSettings";

        static PlanarReflectionSettings s_Instance;

        [Tooltip("Reflectors per source camera allowed to re-render. The rest keep their last image.")]
        [SerializeField, Min(0)] int m_MaxActiveReflectors = 4;

        [SerializeField] PlanarReflectionBlurBackend m_BlurBackend = PlanarReflectionBlurBackend.Auto;

        public static PlanarReflectionSettings Instance
        {
            get
            {
                if (s_Instance != null)
                    return s_Instance;

                s_Instance = Resources.Load<PlanarReflectionSettings>(k_ResourcePath);

                if (s_Instance == null)
                {
                    s_Instance = CreateInstance<PlanarReflectionSettings>();
                    s_Instance.hideFlags = HideFlags.HideAndDontSave;
                }

                return s_Instance;
            }
        }

        public int MaxActiveReflectors => m_MaxActiveReflectors;
        public PlanarReflectionBlurBackend BlurBackend => m_BlurBackend;
    }
}
