using System.Collections.Generic;
using UnityEngine;

namespace SSGICore
{
    /// <summary>
    /// Optional per-light GI control. Add it to a Light to scale (or disable) how much that light feeds SSGICore's bounce.
    /// A light without this component counts as scale 1. Enabling, disabling, spawning and destroying the component or the
    /// light are all picked up the next frame (the affected screen region flushes its temporal history).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("Rendering/SSGICore Light")]
    public sealed class SSGICoreLight : MonoBehaviour
    {
        [Tooltip("When off, this light contributes no bounce light to SSGICore (its direct light is unchanged).")]
        public bool giEnabled = true;

        [Tooltip("Multiplier on this light's contribution to the bounce. 1 = unchanged.")]
        [Range(0f, 4f)]
        public float giIntensity = 1f;

        Light m_Light;

        /// <summary>Effective GI scale of this light.</summary>
        public float Scale { get { return giEnabled ? Mathf.Max(giIntensity, 0f) : 0f; } }

        void OnEnable()
        {
            m_Light = GetComponent<Light>();
            SSGICoreLightRegistry.Register(m_Light, this);
        }

        void OnDisable()
        {
            SSGICoreLightRegistry.Unregister(m_Light);
        }
    }

    /// <summary>Lookup from a Light to its SSGICoreLight component. Filled by the components' OnEnable / OnDisable.</summary>
    public static class SSGICoreLightRegistry
    {
        static readonly Dictionary<Light, SSGICoreLight> s_Lights = new Dictionary<Light, SSGICoreLight>();

        public static int Count { get { return s_Lights.Count; } }

        internal static void Register(Light light, SSGICoreLight component)
        {
            if (light != null)
                s_Lights[light] = component;
        }

        internal static void Unregister(Light light)
        {
            if (light != null)
                s_Lights.Remove(light);
        }

        /// <summary>GI scale of a light: the component's scale, or 1 when the light has none.</summary>
        public static float GetScale(Light light)
        {
            if (light != null && s_Lights.TryGetValue(light, out SSGICoreLight component) && component != null)
                return component.Scale;
            return 1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            s_Lights.Clear();
        }
    }
}
