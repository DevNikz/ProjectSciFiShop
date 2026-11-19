using UnityEngine;

namespace Studio.PlanarReflections
{
    public static class PlanarReflectionMath
    {
        /// <summary>
        /// Reflection matrix for a plane (n.x, n.y, n.z, d) where n is a unit normal
        /// and n.p + d = 0 for points p on the plane.
        /// </summary>
        public static Matrix4x4 GetReflectionMatrix(Vector4 plane)
        {
            Matrix4x4 matrix = Matrix4x4.identity;

            matrix.m00 = 1f - 2f * plane.x * plane.x;
            matrix.m01 = -2f * plane.x * plane.y;
            matrix.m02 = -2f * plane.x * plane.z;
            matrix.m03 = -2f * plane.w * plane.x;

            matrix.m10 = -2f * plane.y * plane.x;
            matrix.m11 = 1f - 2f * plane.y * plane.y;
            matrix.m12 = -2f * plane.y * plane.z;
            matrix.m13 = -2f * plane.w * plane.y;

            matrix.m20 = -2f * plane.z * plane.x;
            matrix.m21 = -2f * plane.z * plane.y;
            matrix.m22 = 1f - 2f * plane.z * plane.z;
            matrix.m23 = -2f * plane.w * plane.z;

            return matrix;
        }

        /// <summary>
        /// Returns the projection with its far plane moved to farClip. Only the depth terms change,
        /// so FOV, aspect, and lens shift are kept. The input must not already be oblique.
        /// </summary>
        public static Matrix4x4 GetProjectionWithFarClip(
            Matrix4x4 projection, float nearClip, float farClip, bool isOrthographic)
        {
            float range = farClip - nearClip;

            if (isOrthographic)
            {
                projection.m22 = -2f / range;
                projection.m23 = -(farClip + nearClip) / range;
            }
            else
            {
                projection.m22 = -(farClip + nearClip) / range;
                projection.m23 = -2f * farClip * nearClip / range;
            }

            return projection;
        }

        /// <summary>
        /// World-space plane point and normal to a camera-space plane, for
        /// Camera.CalculateObliqueMatrix. The plane is moved along the normal by clipOffset.
        /// </summary>
        public static Vector4 GetCameraSpacePlane(
            Matrix4x4 worldToCamera, Vector3 position, Vector3 normal, float clipOffset)
        {
            Vector3 offsetPosition = position + normal * clipOffset;
            Vector3 cameraPosition = worldToCamera.MultiplyPoint(offsetPosition);
            Vector3 cameraNormal = worldToCamera.MultiplyVector(normal).normalized;

            return new Vector4(
                cameraNormal.x,
                cameraNormal.y,
                cameraNormal.z,
                -Vector3.Dot(cameraPosition, cameraNormal));
        }
    }
}
