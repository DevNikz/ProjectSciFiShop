using NUnit.Framework;
using UnityEngine;

namespace SSGICore.Tests
{
    public class SSGICoreLightRuntimeTests
    {
        [Test]
        public void LightShader_IsPresent()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders unsupported on this platform.");
            Assert.IsNotNull(Resources.Load<ComputeShader>("SSGICore/SSGICore_Light"));
        }

        [Test]
        public void SpawnToggleDespawn_UpdatesRegistry()
        {
            int before = SSGICoreLightRegistry.Count;
            var go = new GameObject("SSGICoreSpawnTest");
            var light = go.AddComponent<Light>();
            var comp = go.AddComponent<SSGICoreLight>();
            Assert.AreEqual(before + 1, SSGICoreLightRegistry.Count);

            comp.giIntensity = 2f;
            Assert.AreEqual(2f, SSGICoreLightRegistry.GetScale(light), 1e-6f);

            Object.DestroyImmediate(go);
            Assert.AreEqual(before, SSGICoreLightRegistry.Count);
        }
    }
}
