using NUnit.Framework;
using UnityEngine;

namespace SSGICore.Tests
{
    public class SSGICoreSmokeTests
    {
        [Test]
        public void Shaders_LoadFromResources_AndKernelsAreSupported()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders unsupported on this platform.");

            string[] names = { "Normals", "Hzb", "Trace", "Temporal", "Filter", "Upsample", "Restir", "Cache" };
            foreach (string n in names)
            {
                var cs = Resources.Load<ComputeShader>("SSGICore/SSGICore_" + n);
                Assert.IsNotNull(cs, "Missing compute shader " + n);
            }
            Assert.IsNotNull(Resources.Load<Shader>("SSGICore/SSGICore_Composite"));
        }

        [Test]
        public void Volume_DefaultsAreInactive()
        {
            var v = ScriptableObject.CreateInstance<SSGICoreVolume>();
            Assert.IsFalse(v.IsActive());
            Object.DestroyImmediate(v);
        }

        [Test]
        public void Volume_DenoiserModeDefaultsToLegacy()
        {
            var v = ScriptableObject.CreateInstance<SSGICoreVolume>();
            Assert.AreEqual(SSGICoreDenoiserMode.Legacy, v.denoiserMode.value);
            Object.DestroyImmediate(v);
        }

        [Test]
        public void Volume_RestirIsOffByDefault()
        {
            var v = ScriptableObject.CreateInstance<SSGICoreVolume>();
            Assert.IsFalse(v.restir.value);
            Assert.IsTrue(v.restirSpatial.value);
            Object.DestroyImmediate(v);
        }

        [Test]
        public void Volume_RadianceCacheIsOffByDefault()
        {
            var v = ScriptableObject.CreateInstance<SSGICoreVolume>();
            Assert.IsFalse(v.radianceCache.value);
            Object.DestroyImmediate(v);
        }
    }
}
