using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SSGICore
{
    /// <summary>Loads the compute shaders and composite material from Resources/SSGICore.</summary>
    internal static class SSGICoreResources
    {
        public static ComputeShader Normals, Hzb, Trace, Temporal, Filter, Upsample, Restir, Cache;
        public static Material Composite;

        /// <summary>1x1x1 stand-in bound to the radiance cache slot while the cache is off (the shader never reads it then).</summary>
        public static RTHandle DummyVolume;
        public static int KNormalsHzb1, KHzbDownMulti, KTrace, KTraceRestir, KTemporal, KAtrous, KUpsample, KRestirSpatial, KCacheInject, KCacheResolve;

        public static bool TryLoad(out string error)
        {
            error = null;
            if (Normals != null && Hzb != null && Trace != null && Temporal != null && Filter != null && Upsample != null && Restir != null && Cache != null && Composite != null && DummyVolume != null)
                return true;

            if (!SystemInfo.supportsComputeShaders)
            {
                error = "Compute shaders are not supported on this device. The fragment fallback (phase 7) is not implemented yet.";
                return false;
            }

            Normals = Resources.Load<ComputeShader>("SSGICore/SSGICore_Normals");
            Hzb = Resources.Load<ComputeShader>("SSGICore/SSGICore_Hzb");
            Trace = Resources.Load<ComputeShader>("SSGICore/SSGICore_Trace");
            Temporal = Resources.Load<ComputeShader>("SSGICore/SSGICore_Temporal");
            Filter = Resources.Load<ComputeShader>("SSGICore/SSGICore_Filter");
            Upsample = Resources.Load<ComputeShader>("SSGICore/SSGICore_Upsample");
            Restir = Resources.Load<ComputeShader>("SSGICore/SSGICore_Restir");
            Cache = Resources.Load<ComputeShader>("SSGICore/SSGICore_Cache");
            Shader composite = Resources.Load<Shader>("SSGICore/SSGICore_Composite");

            if (Normals == null || Hzb == null || Trace == null || Temporal == null || Filter == null || Upsample == null || Restir == null || Cache == null || composite == null)
            {
                error = "Shaders are missing from Resources/SSGICore. Re-import the SSGICore package.";
                return false;
            }

            KNormalsHzb1 = Normals.FindKernel("NormalsAndHzb1");
            KHzbDownMulti = Hzb.FindKernel("HzbDownMulti");
            KTrace = Trace.FindKernel("Trace");
            KTraceRestir = Trace.FindKernel("TraceRestir");
            KTemporal = Temporal.FindKernel("Temporal");
            KAtrous = Filter.FindKernel("Atrous");
            KUpsample = Upsample.FindKernel("Upsample");
            KRestirSpatial = Restir.FindKernel("RestirSpatial");
            KCacheInject = Cache.FindKernel("CacheInject");
            KCacheResolve = Cache.FindKernel("CacheResolve");

            if (!Normals.IsSupported(KNormalsHzb1) || !Hzb.IsSupported(KHzbDownMulti) || !Trace.IsSupported(KTrace) ||
                !Temporal.IsSupported(KTemporal) || !Filter.IsSupported(KAtrous) || !Upsample.IsSupported(KUpsample) ||
                !Trace.IsSupported(KTraceRestir) || !Restir.IsSupported(KRestirSpatial) ||
                !Cache.IsSupported(KCacheInject) || !Cache.IsSupported(KCacheResolve) ||
                !composite.isSupported)
            {
                error = "One or more SSGICore shaders are not supported on this GPU / graphics API.";
                return false;
            }

            if (DummyVolume == null)
            {
                DummyVolume = RTHandles.Alloc(1, 1, 1, DepthBits.None, GraphicsFormat.R16G16B16A16_SFloat,
                    dimension: TextureDimension.Tex3D, name: "SSGICore_DummyVolume");
                if (DummyVolume == null)
                {
                    error = "Could not allocate the 3D placeholder texture on this GPU / graphics API.";
                    return false;
                }
            }

            Composite = new Material(composite) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }
    }
}
