using NUnit.Framework;
using UnityEngine;

namespace Studio.PlanarReflections.Tests
{
    public class PlanarReflectionMathTests
    {
        const float k_Tolerance = 1e-4f;

        static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(k_Tolerance),
                $"Expected {expected} but was {actual}");
        }

        [Test]
        public void GetReflectionMatrix_PlaneThroughOrigin_FlipsY()
        {
            Matrix4x4 matrix = PlanarReflectionMath.GetReflectionMatrix(new Vector4(0f, 1f, 0f, 0f));
            AssertVector(new Vector3(1f, -2f, 3f), matrix.MultiplyPoint(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void GetReflectionMatrix_OffsetPlane_MirrorsAroundPlane()
        {
            // Plane y = 1  =>  n = (0,1,0), d = -1.
            Matrix4x4 matrix = PlanarReflectionMath.GetReflectionMatrix(new Vector4(0f, 1f, 0f, -1f));
            AssertVector(new Vector3(0f, -1f, 0f), matrix.MultiplyPoint(new Vector3(0f, 3f, 0f)));
        }

        [Test]
        public void GetReflectionMatrix_AppliedTwice_ReturnsOriginalPoint()
        {
            Vector3 normal = new Vector3(1f, 2f, 3f).normalized;
            var plane = new Vector4(normal.x, normal.y, normal.z, -0.75f);
            Matrix4x4 matrix = PlanarReflectionMath.GetReflectionMatrix(plane);

            var point = new Vector3(-2f, 5f, 1f);
            AssertVector(point, matrix.MultiplyPoint(matrix.MultiplyPoint(point)));
        }

        [Test]
        public void GetCameraSpacePlane_IdentityView_ReturnsWorldPlane()
        {
            Vector4 plane = PlanarReflectionMath.GetCameraSpacePlane(
                Matrix4x4.identity, new Vector3(0f, 2f, 0f), Vector3.up, 0f);

            Assert.That(plane.x, Is.EqualTo(0f).Within(k_Tolerance));
            Assert.That(plane.y, Is.EqualTo(1f).Within(k_Tolerance));
            Assert.That(plane.z, Is.EqualTo(0f).Within(k_Tolerance));
            Assert.That(plane.w, Is.EqualTo(-2f).Within(k_Tolerance));
        }

        [Test]
        public void GetCameraSpacePlane_ClipOffset_MovesPlaneAlongNormal()
        {
            Vector4 plane = PlanarReflectionMath.GetCameraSpacePlane(
                Matrix4x4.identity, Vector3.zero, Vector3.up, 0.5f);

            Assert.That(plane.w, Is.EqualTo(-0.5f).Within(k_Tolerance));
        }
    }
}
