using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Studio.PlanarReflections.Tests
{
    public class PlanarReflectorVisibilityTests
    {
        GameObject m_PlaneObject;
        GameObject m_CameraObject;
        PlanarReflector m_Reflector;
        Camera m_Camera;
        readonly Plane[] m_Frustum = new Plane[6];

        [SetUp]
        public void SetUp()
        {
            m_PlaneObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            m_Reflector = m_PlaneObject.AddComponent<PlanarReflector>();
            m_CameraObject = new GameObject("TestCamera", typeof(Camera));
            m_Camera = m_CameraObject.GetComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_PlaneObject);
            Object.DestroyImmediate(m_CameraObject);
        }

        bool IsVisibleFrom(Vector3 position, Vector3 eulerAngles)
        {
            m_CameraObject.transform.SetPositionAndRotation(position, Quaternion.Euler(eulerAngles));
            GeometryUtility.CalculateFrustumPlanes(m_Camera, m_Frustum);
            return m_Reflector.IsVisibleTo(m_Camera, m_Frustum);
        }

        [Test]
        public void IsVisibleTo_CameraAboveLookingDown_ReturnsTrue()
        {
            Assert.IsTrue(IsVisibleFrom(new Vector3(0f, 5f, 0f), new Vector3(90f, 0f, 0f)));
        }

        [Test]
        public void IsVisibleTo_CameraBelowPlane_ReturnsFalse()
        {
            Assert.IsFalse(IsVisibleFrom(new Vector3(0f, -5f, 0f), new Vector3(-90f, 0f, 0f)));
        }

        [Test]
        public void IsVisibleTo_CameraLookingAway_ReturnsFalse()
        {
            Assert.IsFalse(IsVisibleFrom(new Vector3(0f, 5f, 0f), new Vector3(-90f, 0f, 0f)));
        }

        [Test]
        public void IsVisibleTo_BeyondMaxDistance_ReturnsFalse()
        {
            var serialized = new SerializedObject(m_Reflector);
            serialized.FindProperty("m_MaxDistance").floatValue = 2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsFalse(IsVisibleFrom(new Vector3(0f, 5f, 0f), new Vector3(90f, 0f, 0f)));
        }

        [Test]
        public void GetScreenWeight_CloserCamera_ScoresHigher()
        {
            m_CameraObject.transform.position = new Vector3(0f, 2f, 0f);
            float near = m_Reflector.GetScreenWeight(m_Camera);

            m_CameraObject.transform.position = new Vector3(0f, 20f, 0f);
            float far = m_Reflector.GetScreenWeight(m_Camera);

            Assert.Greater(near, far);
        }
    }
}
