// BLK-06  Glue only: material lifetime, camera filtering, input requests, enqueue. No rendering logic.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSRFeature
{
    [DisallowMultipleRendererFeature("Screen Space Reflections (Accessible)")]
    public sealed class SSRRendererFeature : ScriptableRendererFeature
    {
        public SSRSettings settings = new SSRSettings();

        [SerializeField, HideInInspector] Shader ssrShader;
        [SerializeField, HideInInspector] Shader maskShader;

        SSRRenderPass _pass;
        Material _ssrMat;

#if UNITY_EDITOR
        void OnValidate()
        {
            int version = settings.settingsVersion;
            settings.SyncCustomFromPreset();
            settings.Validate();
            if (version != settings.settingsVersion) UnityEditor.EditorUtility.SetDirty(this);   // persist the migrated Resolution Scale
        }

        void Reset()
        {
            ssrShader = Shader.Find("Hidden/SSR/Reflections");
            maskShader = Shader.Find("Hidden/SSR/Mask");
        }
#endif

        public override void Create()
        {
            settings.Validate();
            Teardown();

            if (ssrShader == null) ssrShader = Shader.Find("Hidden/SSR/Reflections");   // editor fallback; serialized ref is what ships
            if (maskShader == null) maskShader = Shader.Find("Hidden/SSR/Mask");
            if (ssrShader == null) { Debug.LogError("[SSR] Reflections shader missing."); return; }

            _ssrMat = CoreUtils.CreateEngineMaterial(ssrShader);
            _pass = new SSRRenderPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || _ssrMat == null) return;

            var cd = renderingData.cameraData;
            if (cd.cameraType == CameraType.Preview || cd.cameraType == CameraType.Reflection) return;
            if (cd.cameraType == CameraType.SceneView && !settings.applyInSceneView) return;
            if (cd.xr.enabled) return;                 // v1: no XR (TEXTURE2D_X arrays / single-pass) [GAP]
            if (cd.camera.orthographic) return;        // v1: perspective only

            SSRResolvedParams p = SSRAccessibility.Resolve(settings, SSRAccessibility.Current, Time.unscaledDeltaTime);
            if (!p.enabled) return;

            _pass.Setup(_ssrMat, maskShader, settings, p);
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal |
                                 ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Motion);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) { if (disposing) Teardown(); }

        void Teardown()
        {
            _pass?.Dispose(); _pass = null;
            CoreUtils.Destroy(_ssrMat); _ssrMat = null;
        }
    }
}
