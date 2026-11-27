using NUnit.Framework;
using UnityEngine;

namespace SSGICore.Tests
{
    public class SSGICoreLightTests
    {
        static SSGICoreLightSnapshot Snap()
        {
            return new SSGICoreLightSnapshot
            {
                position = new Vector3(1f, 2f, 3f),
                forward = Vector3.forward,
                color = new Color(1f, 0.8f, 0.6f),
                range = 10f,
                spotAngle = 45f,
                scale = 1f,
            };
        }

        [Test]
        public void Delta_IsZeroForIdenticalLights()
        {
            var a = Snap();
            Assert.AreEqual(0f, SSGICoreLightMath.ComputeDelta(a, a), 1e-6f);
        }

        [Test]
        public void Delta_GrowsWithMovement_AndSaturates()
        {
            var a = Snap();
            var small = a; small.position += new Vector3(0.05f, 0f, 0f);   // 0.5% of the range
            var big = a;   big.position += new Vector3(2f, 0f, 0f);       // 20% of the range
            float ds = SSGICoreLightMath.ComputeDelta(a, small);
            float db = SSGICoreLightMath.ComputeDelta(a, big);
            Assert.Greater(ds, 0f);
            Assert.Less(ds, 0.5f);
            Assert.AreEqual(1f, db, 1e-6f);
        }

        [Test]
        public void Delta_ReactsToColorRotationAndGiScale()
        {
            var a = Snap();
            var dim = a;    dim.color *= 0.5f;
            var turned = a; turned.forward = Quaternion.Euler(0f, 5f, 0f) * Vector3.forward;
            var scaled = a; scaled.scale = 0f;
            Assert.AreEqual(1f, SSGICoreLightMath.ComputeDelta(a, dim), 1e-6f);
            Assert.AreEqual(1f, SSGICoreLightMath.ComputeDelta(a, turned), 1e-6f);
            Assert.AreEqual(1f, SSGICoreLightMath.ComputeDelta(a, scaled), 1e-6f);
        }

        [Test]
        public void Delta_IsSymmetric()
        {
            var a = Snap();
            var b = a; b.position += new Vector3(0.1f, 0f, 0f); b.color *= 1.02f;
            Assert.AreEqual(SSGICoreLightMath.ComputeDelta(a, b), SSGICoreLightMath.ComputeDelta(b, a), 1e-5f);
        }

        [Test]
        public void Registry_TracksScaleEnableAndDestroy()
        {
            var go = new GameObject("SSGICoreLightTest");
            try
            {
                var light = go.AddComponent<Light>();
                Assert.AreEqual(1f, SSGICoreLightRegistry.GetScale(light), "no component = scale 1");

                var comp = go.AddComponent<SSGICoreLight>();
                Assert.AreEqual(1f, SSGICoreLightRegistry.GetScale(light));

                comp.giIntensity = 0.25f;
                Assert.AreEqual(0.25f, SSGICoreLightRegistry.GetScale(light), 1e-6f);

                comp.giEnabled = false;
                Assert.AreEqual(0f, SSGICoreLightRegistry.GetScale(light), 1e-6f);

                comp.enabled = false; // unregisters: the light counts as unmodified again
                Assert.AreEqual(1f, SSGICoreLightRegistry.GetScale(light));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Volume_NewSettingsHaveSafeDefaults()
        {
            var v = ScriptableObject.CreateInstance<SSGICoreVolume>();
            Assert.IsTrue(v.lightResponse.value);
            Assert.AreEqual(0f, v.lightGiFloor.value);
            Assert.AreEqual(0.5f, v.lightChangeFlush.value);
            Assert.AreEqual(0.5f, v.contactOcclusion.value);
            Assert.IsTrue(v.leakReduction.value);
            Assert.IsTrue(v.adaptiveRays.value);
            Object.DestroyImmediate(v);
        }
    }
}
