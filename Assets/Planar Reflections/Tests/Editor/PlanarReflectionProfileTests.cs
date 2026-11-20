using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Studio.PlanarReflections.Tests
{
    public class PlanarReflectionProfileTests
    {
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

        void SetFloat(string propertyName, float value)
        {
            var serialized = new SerializedObject(m_Profile);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        void SetEnum(string propertyName, int value)
        {
            var serialized = new SerializedObject(m_Profile);
            serialized.FindProperty(propertyName).enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void GetShaderParams_FadeDisabledByDefault_ReturnsNoFade()
        {
            Vector4 shaderParams = m_Profile.GetShaderParams(false, false);

            Assert.GreaterOrEqual(shaderParams.x, 1e8f);
            Assert.AreEqual(0f, shaderParams.y);
            Assert.AreEqual(0f, shaderParams.z);
            Assert.AreEqual(0f, shaderParams.w);
        }

        [Test]
        public void GetShaderParams_FadeEnabled_ReturnsStartAndInverseRange()
        {
            SetFloat("m_FadeStartDistance", 10f);
            SetFloat("m_FadeEndDistance", 30f);

            Vector4 shaderParams = m_Profile.GetShaderParams(true, true);

            Assert.AreEqual(10f, shaderParams.x, 1e-4f);
            Assert.AreEqual(0.05f, shaderParams.y, 1e-4f);
            Assert.AreEqual(4f, shaderParams.z, 1e-4f);
            Assert.AreEqual(1f, shaderParams.w);
        }

        [Test]
        public void GetMainTextureSpec_ResolutionScale_ScalesAndClampsToOne()
        {
            SetFloat("m_ResolutionScale", 0.5f);

            PlanarReflectionTextureSpec spec = m_Profile.GetMainTextureSpec(1920, 1080);
            Assert.AreEqual(960, spec.Width);
            Assert.AreEqual(540, spec.Height);

            PlanarReflectionTextureSpec tiny = m_Profile.GetMainTextureSpec(1, 1);
            Assert.AreEqual(1, tiny.Width);
            Assert.AreEqual(1, tiny.Height);
        }

        [Test]
        public void GetMainTextureSpec_DefaultMipsMode_UsesMipsAndDepth()
        {
            PlanarReflectionTextureSpec spec = m_Profile.GetMainTextureSpec(1280, 720);

            Assert.IsTrue(spec.UseMips);
            Assert.AreEqual(24, spec.DepthBits);
            Assert.AreEqual(1, spec.Samples);
        }

        [Test]
        public void GetMainTextureSpec_MsaaRequested_StillSingleSampleAndFlagged()
        {
            SetEnum("m_Msaa", 2);

            PlanarReflectionTextureSpec spec = m_Profile.GetMainTextureSpec(1280, 720);

            Assert.AreEqual(1, spec.Samples);
            Assert.IsTrue(m_Profile.IsMsaaRequested);
        }

        [Test]
        public void GetBlurredTextureSpec_HalfResolution_AndComputeNeedsRandomWriteHalfFormat()
        {
            PlanarReflectionTextureSpec main = m_Profile.GetMainTextureSpec(1280, 720);

            PlanarReflectionTextureSpec raster = m_Profile.GetBlurredTextureSpec(main, false);
            Assert.AreEqual(640, raster.Width);
            Assert.AreEqual(360, raster.Height);
            Assert.IsFalse(raster.RandomWrite);
            Assert.AreEqual(main.Format, raster.Format);

            PlanarReflectionTextureSpec compute = m_Profile.GetBlurredTextureSpec(main, true);
            Assert.IsTrue(compute.RandomWrite);
            Assert.AreEqual(RenderTextureFormat.ARGBHalf, compute.Format);
            Assert.AreEqual(0, compute.DepthBits);
        }
    }
}
