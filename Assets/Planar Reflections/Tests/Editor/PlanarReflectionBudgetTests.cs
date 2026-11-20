using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Studio.PlanarReflections.Tests
{
    public class PlanarReflectionBudgetTests
    {
        const float k_Tolerance = 1e-4f;

        PlanarReflectionProfile m_Profile;

        [SetUp]
        public void SetUp()
        {
            m_Profile = ScriptableObject.CreateInstance<PlanarReflectionProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Profile);
        }

        void SetFarClip(float value)
        {
            var serialized = new SerializedObject(m_Profile);
            serialized.FindProperty("m_FarClip").floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AssertMatrix(Matrix4x4 expected, Matrix4x4 actual)
        {
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(expected[i], actual[i], k_Tolerance, $"element {i}");
        }

        [Test]
        public void Defaults_ShadowsOff_DefaultRenderer_LodBiasUnchanged()
        {
            Assert.IsFalse(m_Profile.RenderShadows);
            Assert.AreEqual(-1, m_Profile.RendererIndex);
            Assert.AreEqual(2f, m_Profile.GetLodBias(2f), k_Tolerance);
        }

        [Test]
        public void GetFarClip_Unset_UsesSourceFarClip()
        {
            Assert.AreEqual(1000f, m_Profile.GetFarClip(0.3f, 1000f), k_Tolerance);
        }

        [Test]
        public void GetFarClip_ShorterThanSource_UsesProfileValue()
        {
            SetFarClip(50f);
            Assert.AreEqual(50f, m_Profile.GetFarClip(0.3f, 1000f), k_Tolerance);
        }

        [Test]
        public void GetFarClip_LongerThanSource_ClampsToSource()
        {
            SetFarClip(5000f);
            Assert.AreEqual(1000f, m_Profile.GetFarClip(0.3f, 1000f), k_Tolerance);
        }

        [Test]
        public void GetFarClip_BelowNearClip_ClampsToNearPlusMargin()
        {
            SetFarClip(0.1f);
            Assert.AreEqual(0.4f, m_Profile.GetFarClip(0.3f, 1000f), k_Tolerance);
        }

        [Test]
        public void GetProjectionWithFarClip_Perspective_MatchesRebuiltProjection()
        {
            Matrix4x4 source = Matrix4x4.Perspective(60f, 1.5f, 0.3f, 1000f);
            Matrix4x4 expected = Matrix4x4.Perspective(60f, 1.5f, 0.3f, 50f);

            AssertMatrix(expected, PlanarReflectionMath.GetProjectionWithFarClip(source, 0.3f, 50f, false));
        }

        [Test]
        public void GetProjectionWithFarClip_Orthographic_MatchesRebuiltProjection()
        {
            Matrix4x4 source = Matrix4x4.Ortho(-5f, 5f, -3f, 3f, 0.3f, 1000f);
            Matrix4x4 expected = Matrix4x4.Ortho(-5f, 5f, -3f, 3f, 0.3f, 50f);

            AssertMatrix(expected, PlanarReflectionMath.GetProjectionWithFarClip(source, 0.3f, 50f, true));
        }
    }
}
