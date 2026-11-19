using NUnit.Framework;
using UnityEngine;

namespace Studio.PlanarReflections.Tests
{
    public class PlanarReflectorHiddenTests
    {
        GameObject m_PlaneObject;
        PlanarReflector m_Reflector;
        Renderer m_Renderer;

        [SetUp]
        public void SetUp()
        {
            m_PlaneObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            m_Reflector = m_PlaneObject.AddComponent<PlanarReflector>();
            m_Renderer = m_PlaneObject.GetComponent<Renderer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_PlaneObject);
        }

        [Test]
        public void SetHiddenForReflection_HidesThenRestores()
        {
            m_Reflector.SetHiddenForReflection(true);
            Assert.IsTrue(m_Renderer.forceRenderingOff);

            m_Reflector.SetHiddenForReflection(false);
            Assert.IsFalse(m_Renderer.forceRenderingOff);
        }

        [Test]
        public void SetHiddenForReflection_KeepsRendererAlreadyForcedOff()
        {
            m_Renderer.forceRenderingOff = true;

            m_Reflector.SetHiddenForReflection(true);
            m_Reflector.SetHiddenForReflection(false);

            Assert.IsTrue(m_Renderer.forceRenderingOff);
        }

        [Test]
        public void SetHiddenForReflection_DoesNotChangeGameObjectLayer()
        {
            int layer = m_PlaneObject.layer;

            m_Reflector.SetHiddenForReflection(true);
            m_Reflector.SetHiddenForReflection(false);

            Assert.AreEqual(layer, m_PlaneObject.layer);
        }
    }
}
