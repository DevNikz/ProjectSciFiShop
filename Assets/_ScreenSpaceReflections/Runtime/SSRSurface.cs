// BLK-08  Opt-in surface registry. The mask pass draws registered meshes itself (GPU Resident Drawer-safe:
// no override shader/material is applied to batched renderers).
using System.Collections.Generic;
using UnityEngine;

namespace SSRFeature
{
    [DisallowMultipleComponent, ExecuteAlways, AddComponentMenu("Rendering/SSR Surface")]
    public sealed class SSRSurface : MonoBehaviour
    {
        [Tooltip("Use the value below instead of reading the material's smoothness.")]
        public bool overrideSmoothness;
        [Range(0f, 1f)] public float smoothness = 0.8f;
        [Range(0f, 1f), Tooltip("Per-surface multiplier on the feature's reflectivity.")]
        public float reflectivityScale = 1f;

        internal MeshRenderer Renderer { get; private set; }
        internal MeshFilter Filter { get; private set; }

        void OnEnable()
        {
            Renderer = GetComponent<MeshRenderer>();
            Filter = GetComponent<MeshFilter>();
            if (Renderer == null || Filter == null)
            {
                Debug.LogWarning("[SSR] SSRSurface needs a MeshRenderer + MeshFilter (skinned meshes are unsupported).", this);
                return;
            }
            SSRSurfaceRegistry.Add(this);
        }

        void OnDisable() => SSRSurfaceRegistry.Remove(this);
    }

    internal static class SSRSurfaceRegistry
    {
        static readonly List<SSRSurface> s_Items = new List<SSRSurface>();
        public static IReadOnlyList<SSRSurface> Items => s_Items;
        public static int Count => s_Items.Count;
        public static void Add(SSRSurface s) { if (!s_Items.Contains(s)) s_Items.Add(s); }
        public static void Remove(SSRSurface s) => s_Items.Remove(s);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => s_Items.Clear();
    }
}
