using UnityEngine;
using UnityEngine.Rendering;

namespace Studio.PlanarReflections
{
    /// <summary>
    /// Runs after the mirrored camera renders. With a context (the reflection camera's own, from
    /// endCameraRendering) the commands are ordered inside that camera's submit. Without one they go
    /// through Graphics.ExecuteCommandBuffer and queue right behind the reflection render.
    /// </summary>
    internal static class PlanarReflectionBlur
    {
        const string k_ShaderPath = "PlanarReflections/KawaseBlur";
        const string k_ComputePath = "PlanarReflections/KawaseBlurCompute";
        const int k_ThreadGroupSize = 8;

        static readonly float[] s_KawaseOffsets = { 0f, 1f, 2f, 2f, 3f, 3f };
        static readonly int s_BlurOffsetId = Shader.PropertyToID("_BlurOffset");
        static readonly int s_SrcTexelSizeId = Shader.PropertyToID("_BlurSrcTexelSize");
        static readonly int s_TempId = Shader.PropertyToID("_PlanarReflectionBlurTemp");
        static readonly int s_SrcId = Shader.PropertyToID("_BlurSrc");
        static readonly int s_DstId = Shader.PropertyToID("_BlurDst");
        static readonly int s_SrcSizeId = Shader.PropertyToID("_BlurSrcSize");
        static readonly int s_DstSizeId = Shader.PropertyToID("_BlurDstSize");

        static CommandBuffer s_Commands;
        static Material s_Material;
        static ComputeShader s_Compute;
        static int s_Kernel = -1;

        public static int MaxIterations => s_KawaseOffsets.Length;

        public static bool ShouldUseCompute(PlanarReflectionBlurBackend requested)
        {
            if (requested == PlanarReflectionBlurBackend.Raster)
                return false;

            bool canUseCompute = SystemInfo.supportsComputeShaders && TryLoadCompute();

            if (requested == PlanarReflectionBlurBackend.Compute)
            {
                if (!canUseCompute)
                    PlanarReflectionLog.WarnOnce("compute-unavailable", "Compute blur was requested but is unavailable. Using raster blur.");

                return canUseCompute;
            }

            return canUseCompute && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12;
        }

        /// <summary>Iteration i writes into the final result when it has the same parity as the last one.</summary>
        public static bool DoesIterationWriteResult(int iterationIndex, int iterationCount)
        {
            return (iterationCount - 1 - iterationIndex) % 2 == 0;
        }

        public static void GenerateMips(RenderTexture texture, ScriptableRenderContext? context = null)
        {
            CommandBuffer cmd = BeginCommands();
            cmd.GenerateMips(texture);
            ExecuteCommands(cmd, context);
        }

        public static void Blur(
            RenderTexture source, RenderTexture result, int iterations, bool useCompute,
            ScriptableRenderContext? context = null)
        {
            if (!useCompute && !TryLoadMaterial())
            {
                PlanarReflectionLog.WarnOnce("blur-shader-missing", $"Blur shader '{k_ShaderPath}' not found. Blur is skipped.");
                return;
            }

            int count = Mathf.Clamp(iterations, 1, MaxIterations);
            CommandBuffer cmd = BeginCommands();

            if (count > 1)
                cmd.GetTemporaryRT(s_TempId, result.descriptor, FilterMode.Bilinear);

            var temp = new RenderTargetIdentifier(s_TempId);
            var resultId = new RenderTargetIdentifier(result);

            for (int i = 0; i < count; i++)
            {
                bool writesResult = DoesIterationWriteResult(i, count);
                RenderTargetIdentifier destination = writesResult ? resultId : temp;
                RenderTargetIdentifier input = i == 0
                    ? new RenderTargetIdentifier(source)
                    : (writesResult ? temp : resultId);

                var inputSize = i == 0
                    ? new Vector2Int(source.width, source.height)
                    : new Vector2Int(result.width, result.height);

                RecordKawaseIteration(cmd, input, destination, inputSize, new Vector2Int(result.width, result.height),
                    s_KawaseOffsets[i], useCompute);
            }

            if (count > 1)
                cmd.ReleaseTemporaryRT(s_TempId);

            ExecuteCommands(cmd, context);
        }

        static void RecordKawaseIteration(
            CommandBuffer cmd, RenderTargetIdentifier input, RenderTargetIdentifier destination,
            Vector2Int inputSize, Vector2Int outputSize, float offset, bool useCompute)
        {
            if (!useCompute)
            {
                cmd.SetGlobalFloat(s_BlurOffsetId, offset);
                cmd.SetGlobalVector(s_SrcTexelSizeId, new Vector4(1f / inputSize.x, 1f / inputSize.y, inputSize.x, inputSize.y));
                cmd.Blit(input, destination, s_Material, 0);
                return;
            }

            cmd.SetComputeTextureParam(s_Compute, s_Kernel, s_SrcId, input);
            cmd.SetComputeTextureParam(s_Compute, s_Kernel, s_DstId, destination);
            cmd.SetComputeVectorParam(s_Compute, s_SrcSizeId, new Vector4(inputSize.x, inputSize.y, 1f / inputSize.x, 1f / inputSize.y));
            cmd.SetComputeVectorParam(s_Compute, s_DstSizeId, new Vector4(outputSize.x, outputSize.y, 1f / outputSize.x, 1f / outputSize.y));
            cmd.SetComputeFloatParam(s_Compute, s_BlurOffsetId, offset);
            cmd.DispatchCompute(
                s_Compute, s_Kernel,
                Mathf.CeilToInt(outputSize.x / (float)k_ThreadGroupSize),
                Mathf.CeilToInt(outputSize.y / (float)k_ThreadGroupSize),
                1);
        }

        static bool TryLoadMaterial()
        {
            if (s_Material != null)
                return true;

            Shader shader = Resources.Load<Shader>(k_ShaderPath);
            if (shader == null)
                return false;

            s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        static bool TryLoadCompute()
        {
            if (s_Compute != null)
                return true;

            s_Compute = Resources.Load<ComputeShader>(k_ComputePath);
            if (s_Compute == null)
                return false;

            s_Kernel = s_Compute.FindKernel("KawaseIteration");
            return true;
        }

        static CommandBuffer BeginCommands()
        {
            s_Commands ??= new CommandBuffer { name = "PlanarReflectionBlur" };
            s_Commands.Clear();
            return s_Commands;
        }

        static void ExecuteCommands(CommandBuffer cmd, ScriptableRenderContext? context)
        {
            if (context.HasValue)
                context.Value.ExecuteCommandBuffer(cmd);
            else
                Graphics.ExecuteCommandBuffer(cmd);

            cmd.Clear();
        }
    }
}
