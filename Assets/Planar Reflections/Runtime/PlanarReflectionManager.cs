using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Studio.PlanarReflections
{
    public static class PlanarReflectionManager
    {
        const int k_PruneIntervalFrames = 120;
        const int k_UnusedFramesBeforePrune = 600;

        struct Candidate
        {
            public PlanarReflector Reflector;
            public float Weight;
        }

        struct PendingBlur
        {
            public bool IsPending;
            public Camera ReflectionCamera;
            public PlanarReflector Reflector;
            public PlanarReflectionProfile Profile;
            public PlanarReflectionKey Key;
            public RenderTexture Target;
            public PlanarReflectionTextureSpec MainSpec;
            public bool UseComputeBlur;
        }

        static readonly List<PlanarReflector> s_Reflectors = new List<PlanarReflector>();
        static readonly List<Candidate> s_Candidates = new List<Candidate>();
        static readonly Plane[] s_FrustumPlanes = new Plane[6];
        static readonly Comparison<Candidate> s_ByWeightDescending = (a, b) => b.Weight.CompareTo(a.Weight);

        static readonly ProfilerMarker s_SubmitMarker = new ProfilerMarker("PlanarReflections.Render");

        static bool s_IsSubscribed;
        static bool s_IsSubmitting;
        static PendingBlur s_Pending;
        static int s_LastPruneFrame;

        public static void Register(PlanarReflector reflector)
        {
            if (s_Reflectors.Contains(reflector))
                return;

            s_Reflectors.Add(reflector);
            Subscribe();
        }

        public static void Unregister(PlanarReflector reflector)
        {
            s_Reflectors.Remove(reflector);

            int reflectorId = reflector.GetInstanceID();
            PlanarReflectionCameraPool.ReleaseReflector(reflectorId);
            PlanarReflectionTexturePool.ReleaseReflector(reflectorId);

            if (s_Reflectors.Count == 0)
                Unsubscribe();
        }

        static void Subscribe()
        {
            if (s_IsSubscribed)
                return;

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            s_IsSubscribed = true;
        }

        static void Unsubscribe()
        {
            if (!s_IsSubscribed)
                return;

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            s_IsSubscribed = false;
        }

        static void OnBeginCameraRendering(ScriptableRenderContext context, Camera sourceCamera)
        {
            if (s_IsSubmitting || !IsEligibleSource(sourceCamera))
                return;

            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            {
                PlanarReflectionLog.WarnOnce("not-urp", "The active render pipeline is not URP. Reflections are skipped.");
                return;
            }

            PruneUnusedViews();
            CollectVisibleReflectors(sourceCamera);

            PlanarReflectionSettings settings = PlanarReflectionSettings.Instance;
            bool useComputeBlur = PlanarReflectionBlur.ShouldUseCompute(settings.BlurBackend);

            for (int i = 0; i < s_Candidates.Count; i++)
            {
                bool isActive = i < settings.MaxActiveReflectors;
                ProcessReflector(s_Candidates[i].Reflector, sourceCamera, isActive, useComputeBlur);
            }
        }

        // Runs the blur inside the reflection camera's own context so it is ordered behind its render.
        static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!s_Pending.IsPending || camera != s_Pending.ReflectionCamera)
                return;

            PendingBlur job = s_Pending;
            s_Pending = default;
            ApplyBlur(job, context);
        }

        // Game and Scene view cameras. Preview cameras and our own hidden cameras are skipped.
        static bool IsEligibleSource(Camera camera)
        {
            bool isSupportedType = camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView;
            return isSupportedType && !PlanarReflectionCameraPool.IsOwned(camera);
        }

        static void CollectVisibleReflectors(Camera sourceCamera)
        {
            s_Candidates.Clear();
            GeometryUtility.CalculateFrustumPlanes(sourceCamera, s_FrustumPlanes);

            for (int i = 0; i < s_Reflectors.Count; i++)
            {
                PlanarReflector reflector = s_Reflectors[i];
                if (reflector == null || !reflector.IsVisibleTo(sourceCamera, s_FrustumPlanes))
                    continue;

                s_Candidates.Add(new Candidate
                {
                    Reflector = reflector,
                    Weight = reflector.GetScreenWeight(sourceCamera)
                });
            }

            s_Candidates.Sort(s_ByWeightDescending);
        }

        static void ProcessReflector(PlanarReflector reflector, Camera sourceCamera, bool isActive, bool useComputeBlur)
        {
            var key = new PlanarReflectionKey(reflector.GetInstanceID(), sourceCamera.GetInstanceID());

            bool isStpActive = UniversalRenderPipeline.asset.upscalingFilter == UpscalingFilterSelection.STP;

            // STP keeps a history per reflection camera, so a stale interval would ghost.
            int updateInterval = Application.isPlaying && !isStpActive ? reflector.UpdateInterval : 1;
            if (isActive && PlanarReflectionTexturePool.IsDue(key, updateInterval))
                TryRenderReflection(reflector, sourceCamera, key, useComputeBlur);

            BindLatest(reflector, key);
        }

        static void BindLatest(PlanarReflector reflector, in PlanarReflectionKey key)
        {
            if (!PlanarReflectionTexturePool.TryGet(key, out RenderTexture latest, out RenderTexture blurred))
            {
                reflector.BindReflection(Texture2D.blackTexture, Texture2D.blackTexture, Vector4.zero);
                return;
            }

            Vector4 shaderParams = reflector.Profile.GetShaderParams(latest.useMipMap, blurred != null);
            reflector.BindReflection(latest, blurred != null ? blurred : Texture2D.blackTexture, shaderParams);
        }

        static void TryRenderReflection(
            PlanarReflector reflector, Camera sourceCamera, in PlanarReflectionKey key, bool useComputeBlur)
        {
            PlanarReflectionProfile profile = reflector.Profile;

            PlanarReflectionTextureSpec mainSpec = profile.GetMainTextureSpec(sourceCamera.pixelWidth, sourceCamera.pixelHeight);
            RenderTexture target = PlanarReflectionTexturePool.GetOrCreate(key, mainSpec, reflector.name);
            if (target == null)
                return;

            Camera reflectionCamera = PlanarReflectionCameraPool.Acquire(key, reflector.name);
            ConfigureReflectionCamera(reflectionCamera, sourceCamera, reflector, profile, key);

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(reflectionCamera, request))
            {
                PlanarReflectionLog.WarnOnce(
                    "request-unsupported",
                    "The active pipeline does not support SingleCameraRequest. Reflections are skipped.");
                return;
            }

            // Only the Kawase blur runs inside the reflection camera's context. The mip chain is generated
            // after the request is submitted, as before, because GenerateMips needs the finished render.
            bool isContextBlur = profile.BlurMode == PlanarReflectionBlurMode.Blurred;

            var job = new PendingBlur
            {
                IsPending = isContextBlur,
                ReflectionCamera = reflectionCamera,
                Reflector = reflector,
                Profile = profile,
                Key = key,
                Target = target,
                MainSpec = mainSpec,
                UseComputeBlur = useComputeBlur
            };

            s_Pending = job;

            bool previousInvertCulling = GL.invertCulling;
            float previousLodBias = QualitySettings.lodBias;
            float reflectionLodBias = profile.GetLodBias(previousLodBias);
            bool changesLodBias = !Mathf.Approximately(reflectionLodBias, previousLodBias);

            GL.invertCulling = reflector.InvertCulling;
            if (changesLodBias)
                QualitySettings.lodBias = reflectionLodBias;

            reflector.SetHiddenForReflection(true);
            s_IsSubmitting = true;

            try
            {
                using (s_SubmitMarker.Auto())
                {
                    RenderPipeline.SubmitRenderRequest(reflectionCamera, request);
                }
            }
            finally
            {
                s_IsSubmitting = false;
                GL.invertCulling = previousInvertCulling;
                reflector.SetHiddenForReflection(false);

                if (changesLodBias)
                    QualitySettings.lodBias = previousLodBias;
            }

            if (isContextBlur && s_Pending.IsPending)
            {
                PlanarReflectionLog.WarnOnce(
                    "blur-no-end-callback",
                    "endCameraRendering did not fire for the reflection camera. Blur runs outside its context.");

                s_Pending = default;
                ApplyBlur(job, null);
            }
            else if (!isContextBlur)
            {
                ApplyBlur(job, null);
            }

            PlanarReflectionTexturePool.MarkRendered(key);
        }

        static void ApplyBlur(in PendingBlur job, ScriptableRenderContext? context)
        {
            switch (job.Profile.BlurMode)
            {
                case PlanarReflectionBlurMode.Mips:
                    PlanarReflectionTexturePool.ReleaseBlurred(job.Key);
                    PlanarReflectionBlur.GenerateMips(job.Target, context);
                    break;

                case PlanarReflectionBlurMode.Blurred:
                    PlanarReflectionTextureSpec blurredSpec = job.Profile.GetBlurredTextureSpec(job.MainSpec, job.UseComputeBlur);
                    RenderTexture blurred = PlanarReflectionTexturePool.GetOrCreateBlurred(job.Key, blurredSpec, job.Reflector.name);
                    if (blurred != null)
                        PlanarReflectionBlur.Blur(job.Target, blurred, job.Profile.BlurIterations, job.UseComputeBlur, context);
                    break;

                default:
                    PlanarReflectionTexturePool.ReleaseBlurred(job.Key);
                    break;
            }
        }

        static void PruneUnusedViews()
        {
            int frame = Time.frameCount;
            if (frame - s_LastPruneFrame < k_PruneIntervalFrames)
                return;

            s_LastPruneFrame = frame;
            PlanarReflectionCameraPool.PruneUnused(frame, k_UnusedFramesBeforePrune);
            PlanarReflectionTexturePool.PruneUnused(frame, k_UnusedFramesBeforePrune);
        }

        // STP and MSAA cannot combine, and STP needs the post pass and a fixed render scale (no dynamic resolution).
        // Other upscalers (FSR, bilinear, point) follow the URP asset render scale on their own.
        static void ConfigureUpscalingAndMsaa(Camera reflectionCamera, PlanarReflectionProfile profile)
        {
            UniversalRenderPipelineAsset urpAsset = UniversalRenderPipeline.asset;
            bool isStpActive = urpAsset.upscalingFilter == UpscalingFilterSelection.STP;

            UniversalAdditionalCameraData data = reflectionCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = isStpActive;
            data.antialiasing = AntialiasingMode.None;
            reflectionCamera.allowDynamicResolution = reflectionCamera.allowDynamicResolution && !isStpActive;

            bool canUseMsaa = !isStpActive && urpAsset.msaaSampleCount > 1;
            reflectionCamera.allowMSAA = profile.IsMsaaRequested && canUseMsaa;

            if (!profile.IsMsaaRequested || canUseMsaa)
                return;

            if (isStpActive)
                PlanarReflectionLog.WarnOnce("msaa-ignored-stp", "MSAA is ignored while STP upscaling is active.");
            else
                PlanarReflectionLog.WarnOnce("msaa-asset-off", "MSAA was requested but the URP asset MSAA is off. Reflection renders without MSAA.");
        }

        // Budget levers that URP exposes per camera. Forward+ has no per-camera light cap at runtime:
        // the visible-light limit is a compile-time ShaderConfig value, so the far clip is the lever.
        static void ApplyBudget(Camera reflectionCamera, in PlanarReflectionKey key, PlanarReflectionProfile profile)
        {
            reflectionCamera.GetUniversalAdditionalCameraData().renderShadows = profile.RenderShadows;
            PlanarReflectionCameraPool.ApplyRendererIndex(key, profile.RendererIndex);
        }

        static void ConfigureReflectionCamera(
            Camera reflectionCamera, Camera sourceCamera, PlanarReflector reflector,
            PlanarReflectionProfile profile, in PlanarReflectionKey key)
        {
            Vector3 normal = reflector.PlaneNormal;
            Vector3 position = reflector.PlanePosition;
            float clipOffset = profile.ClipOffset;

            var plane = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, position) - clipOffset);
            Matrix4x4 reflection = PlanarReflectionMath.GetReflectionMatrix(plane);

            reflectionCamera.CopyFrom(sourceCamera);
            reflectionCamera.cameraType = CameraType.Reflection;
            reflectionCamera.enabled = false;
            reflectionCamera.targetTexture = null;
            reflectionCamera.useOcclusionCulling = false;
            ConfigureUpscalingAndMsaa(reflectionCamera, profile);
            reflectionCamera.cullingMask = sourceCamera.cullingMask & profile.LayerMask;

            // Set before the matrices below: assigning a clip plane resets a custom projection.
            float nearClip = sourceCamera.nearClipPlane;
            float farClip = profile.GetFarClip(nearClip, sourceCamera.farClipPlane);
            reflectionCamera.farClipPlane = farClip;
            ApplyBudget(reflectionCamera, key, profile);

            Transform sourceTransform = sourceCamera.transform;
            reflectionCamera.transform.SetPositionAndRotation(
                reflection.MultiplyPoint(sourceTransform.position),
                Quaternion.LookRotation(
                    reflection.MultiplyVector(sourceTransform.forward),
                    reflection.MultiplyVector(sourceTransform.up)));

            reflectionCamera.ResetWorldToCameraMatrix();
            reflectionCamera.ResetProjectionMatrix();
            reflectionCamera.worldToCameraMatrix = sourceCamera.worldToCameraMatrix * reflection;

            Matrix4x4 projection = sourceCamera.projectionMatrix;
            if (!Mathf.Approximately(farClip, sourceCamera.farClipPlane))
                projection = PlanarReflectionMath.GetProjectionWithFarClip(projection, nearClip, farClip, sourceCamera.orthographic);

            reflectionCamera.projectionMatrix = projection;

            Vector4 clipPlane = PlanarReflectionMath.GetCameraSpacePlane(
                reflectionCamera.worldToCameraMatrix, position, normal, clipOffset);
            reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);
        }
    }
}
