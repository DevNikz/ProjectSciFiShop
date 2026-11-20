using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Studio.PlanarReflections.Editor
{
    /// <summary>Read-only report of the URP renderer list and every profile's budget renderer index.</summary>
    internal static class PlanarReflectionSetupValidator
    {
        [MenuItem("Tools/Planar Reflections/Validate Setup")]
        static void Validate()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null)
            {
                Debug.LogWarning("[PlanarReflections] The active render pipeline is not URP.");
                return;
            }

            var serialized = new SerializedObject(asset);
            SerializedProperty rendererList = serialized.FindProperty("m_RendererDataList");
            SerializedProperty defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            if (rendererList == null || defaultIndex == null)
            {
                Debug.LogWarning("[PlanarReflections] Could not read the renderer list from the URP asset.");
                return;
            }

            var report = new StringBuilder("[PlanarReflections] Setup report\n");
            report.AppendLine($"URP asset: {asset.name} | MSAA {asset.msaaSampleCount}x | Upscaling {asset.upscalingFilter}");

            bool hasIssue = false;
            for (int i = 0; i < rendererList.arraySize; i++)
                hasIssue |= ReportRenderer(report, rendererList, i, defaultIndex.intValue);

            string[] guids = AssetDatabase.FindAssets("t:PlanarReflectionProfile");
            report.AppendLine($"Profiles: {guids.Length}");
            for (int i = 0; i < guids.Length; i++)
                hasIssue |= ReportProfile(report, AssetDatabase.GUIDToAssetPath(guids[i]), rendererList);

            if (hasIssue)
                Debug.LogWarning(report.ToString());
            else
                Debug.Log(report.ToString());
        }

        static bool ReportRenderer(StringBuilder report, SerializedProperty rendererList, int index, int defaultIndex)
        {
            string marker = index == defaultIndex ? " (default)" : string.Empty;
            var data = rendererList.GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;

            if (data == null)
            {
                report.AppendLine($"  [{index}]{marker} missing or not a Universal Renderer");
                return true;
            }

            report.AppendLine(
                $"  [{index}]{marker} {data.name}: {data.renderingMode}, depth priming {data.depthPrimingMode}, " +
                $"{data.rendererFeatures.Count} feature(s)");
            return false;
        }

        static bool ReportProfile(StringBuilder report, string path, SerializedProperty rendererList)
        {
            var profile = AssetDatabase.LoadAssetAtPath<PlanarReflectionProfile>(path);
            if (profile == null)
                return false;

            int index = profile.RendererIndex;
            string line = $"  {path}: renderer {index}, shadows {(profile.RenderShadows ? "on" : "off")}, " +
                          $"far clip {(profile.FarClip > 0f ? profile.FarClip + " m" : "source")}, " +
                          $"LOD bias x{profile.LodBiasScale:0.##}";

            bool isCoarseLod = profile.LodBiasScale < 1f;
            if (isCoarseLod)
                line += "  <- LOD bias below 1 can drop LOD groups (often characters) out of the reflection";

            if (index >= rendererList.arraySize)
            {
                report.AppendLine(line + "  <- index is outside the renderer list");
                return true;
            }

            if (index < 0)
            {
                report.AppendLine(line);
                return isCoarseLod;
            }

            var data = rendererList.GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
            bool isHeavy = data != null
                && (data.depthPrimingMode != DepthPrimingMode.Disabled || data.rendererFeatures.Count > 0);

            report.AppendLine(isHeavy ? line + "  <- budget renderer should have no features and depth priming disabled" : line);
            return isHeavy || isCoarseLod;
        }
    }
}
