using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Studio.PlanarReflections
{
    /// <summary>
    /// One hidden camera per (reflector, source camera) so URP keeps stable per-camera
    /// targets when the Game and Scene views have different sizes.
    /// </summary>
    internal static class PlanarReflectionCameraPool
    {
        const int k_RendererIndexUnset = int.MinValue;

        sealed class Entry : IPlanarReflectionPoolEntry
        {
            public Camera Camera;
            public int AppliedRendererIndex = k_RendererIndexUnset;
            public int LastUsedFrame { get; set; }

            public void Dispose()
            {
                if (Camera == null)
                    return;

                s_OwnedCameras.Remove(Camera);
                PlanarReflectionUtility.SafeDestroy(Camera.gameObject);
                Camera = null;
            }
        }

        static readonly Dictionary<PlanarReflectionKey, Entry> s_Entries = new Dictionary<PlanarReflectionKey, Entry>();
        static readonly HashSet<Camera> s_OwnedCameras = new HashSet<Camera>();

        public static bool IsOwned(Camera camera)
        {
            return s_OwnedCameras.Contains(camera);
        }

        public static Camera Acquire(in PlanarReflectionKey key, string ownerName)
        {
            if (!s_Entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry();
                s_Entries[key] = entry;
            }

            if (entry.Camera == null)
            {
                entry.Camera = CreateHiddenCamera(ownerName);
                entry.AppliedRendererIndex = k_RendererIndexUnset;
                s_OwnedCameras.Add(entry.Camera);
            }

            entry.LastUsedFrame = Time.frameCount;
            return entry.Camera;
        }

        /// <summary>Sets the URP renderer once per camera and again only when the index changes.</summary>
        public static void ApplyRendererIndex(in PlanarReflectionKey key, int rendererIndex)
        {
            if (!s_Entries.TryGetValue(key, out Entry entry)
                || entry.Camera == null
                || entry.AppliedRendererIndex == rendererIndex)
                return;

            entry.AppliedRendererIndex = rendererIndex;
            entry.Camera.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);
            WarnIfRendererFallsBack(rendererIndex);
        }

        // URP silently uses the default renderer for an index it cannot resolve.
        static void WarnIfRendererFallsBack(int rendererIndex)
        {
            UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
            if (asset == null || rendererIndex < 0)
                return;

            if (asset.GetRenderer(rendererIndex) != asset.GetRenderer(-1))
                return;

            PlanarReflectionLog.WarnOnce(
                $"renderer-index-{rendererIndex}",
                $"Renderer index {rendererIndex} is invalid or is the default renderer, so the reflection uses the default renderer. " +
                "Add a budget renderer to the URP asset's renderer list (Tools > Planar Reflections > Validate Setup).");
        }

        public static void ReleaseReflector(int reflectorId)
        {
            PlanarReflectionPoolUtility.ReleaseReflector(s_Entries, reflectorId);
        }

        public static void PruneUnused(int currentFrame, int maxAgeFrames)
        {
            PlanarReflectionPoolUtility.PruneUnused(s_Entries, currentFrame, maxAgeFrames);
        }

        static Camera CreateHiddenCamera(string ownerName)
        {
            var go = new GameObject($"PlanarReflectionCamera ({ownerName})")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Camera camera = go.AddComponent<Camera>();
            camera.enabled = false;

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.volumeLayerMask = 0;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.renderShadows = true;

            return camera;
        }
    }
}
