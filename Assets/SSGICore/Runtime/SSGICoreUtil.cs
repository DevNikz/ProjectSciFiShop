using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace SSGICore
{
    public static class SSGICoreUtil
    {
        public const int HzbLevels = 5;

        /// <summary>Size of one axis of HZB level (level 1 = half resolution).</summary>
        public static int HzbLevelSize(int fullSize, int level)
        {
            return Mathf.Max(1, (fullSize + (1 << level) - 1) >> level);
        }

        public static int LowSize(int fullSize, int downsample)
        {
            return Mathf.Max(1, (fullSize + downsample - 1) / downsample);
        }

        public static int GroupCount(int size) { return (size + 7) / 8; }

        public static int GroupCount(int size, int groupSize) { return (size + groupSize - 1) / groupSize; }

        /// <summary>
        /// Packs HZB levels 1..HzbLevels into one atlas: level 1 at the origin, levels 2.. stacked to its right.
        /// Fills rects[level] = (x offset, y offset, width, height); rects[0] is the full-resolution depth buffer.
        /// rects must hold HzbLevels + 1 entries. Returns the atlas size.
        /// </summary>
        public static Vector2Int ComputeHzbAtlas(int fullW, int fullH, Vector4[] rects)
        {
            int w1 = HzbLevelSize(fullW, 1);
            int h1 = HzbLevelSize(fullH, 1);
            rects[0] = new Vector4(0f, 0f, fullW, fullH);
            rects[1] = new Vector4(0f, 0f, w1, h1);

            int stackedHeight = 0;
            for (int level = 2; level <= HzbLevels; level++)
            {
                int w = HzbLevelSize(fullW, level);
                int h = HzbLevelSize(fullH, level);
                rects[level] = new Vector4(w1, stackedHeight, w, h);
                stackedHeight += h;
            }
            return new Vector2Int(w1 + HzbLevelSize(fullW, 2), Mathf.Max(h1, stackedHeight));
        }

        /// <summary>
        /// For a perspective projection the view ray through the z = -1 plane is affine in ndc. Returns it as
        /// a = (origin.xy, perNdcX.xy) and b = (perNdcY.xy, 0, 0); see ViewPosFromPixel in SSGICore_Common.hlsl.
        /// </summary>
        public static void ComputeRayTerms(Matrix4x4 invProj, out Vector4 a, out Vector4 b)
        {
            Vector2 origin = RayAtNdc(invProj, 0f, 0f);
            Vector2 dx = RayAtNdc(invProj, 1f, 0f) - origin;
            Vector2 dy = RayAtNdc(invProj, 0f, 1f) - origin;
            a = new Vector4(origin.x, origin.y, dx.x, dx.y);
            b = new Vector4(dy.x, dy.y, 0f, 0f);
        }

        static Vector2 RayAtNdc(Matrix4x4 invProj, float ndcX, float ndcY)
        {
            Vector4 h = invProj * new Vector4(ndcX, ndcY, 0.5f, 1f);
            Vector3 r = new Vector3(h.x, h.y, h.z) / h.w;
            float s = 1f / -r.z;
            return new Vector2(r.x * s, r.y * s);
        }

        static GraphicsFormat s_NormalsFormat;
        static bool s_NormalsFormatResolved;

        /// <summary>Packed resolved-normals format: R10G10B10A2 when it supports UAV stores, RGBA16F otherwise.</summary>
        public static GraphicsFormat NormalsFormat
        {
            get
            {
                if (!s_NormalsFormatResolved)
                {
                    s_NormalsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormatUsage.LoadStore)
                        ? GraphicsFormat.A2B10G10R10_UNormPack32
                        : GraphicsFormat.R16G16B16A16_SFloat;
                    s_NormalsFormatResolved = true;
                }
                return s_NormalsFormat;
            }
        }

        /// <summary>Same layout as Unity's _ZBufferParams.</summary>
        public static Vector4 ComputeZParams(float near, float far, bool reversedZ)
        {
            if (reversedZ)
            {
                float x = -1f + far / near;
                return new Vector4(x, 1f, x / far, 1f / far);
            }
            else
            {
                float x = 1f - far / near;
                float y = far / near;
                return new Vector4(x, y, x / far, y / far);
            }
        }
    }

    /// <summary>Hooks used by the parity tooling and tests.</summary>
    public static class SSGICoreTestHooks
    {
        /// <summary>When >= 0, replaces Time.frameCount as the noise frame index so captures are deterministic.</summary>
        public static int FrameOverride = -1;
    }

    /// <summary>Image comparison used by the parity gate.</summary>
    public static class SSGICoreParity
    {
        public const float GateRelativeRmse = 0.01f;

        /// <summary>RMSE of per-pixel luminance difference, divided by the mean luminance of the reference.</summary>
        public static float RelativeRmse(Color[] reference, Color[] candidate)
        {
            if (reference == null || candidate == null || reference.Length != candidate.Length || reference.Length == 0)
                return float.PositiveInfinity;

            double sumSq = 0.0, sumRef = 0.0;
            for (int i = 0; i < reference.Length; i++)
            {
                float lr = Luma(reference[i]);
                float lc = Luma(candidate[i]);
                double d = lr - lc;
                sumSq += d * d;
                sumRef += lr;
            }
            double meanRef = sumRef / reference.Length;
            double rmse = System.Math.Sqrt(sumSq / reference.Length);
            return (float)(rmse / System.Math.Max(meanRef, 1e-6));
        }

        static float Luma(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }
    }

    internal static class SSGICoreLog
    {
        static readonly HashSet<string> s_Seen = new HashSet<string>();

        public static void WarnOnce(string key, string message)
        {
            if (s_Seen.Add(key))
                Debug.LogWarning("[SSGICore] " + message);
        }
    }

    internal static class SSGICoreIds
    {
        static int ID(string n) { return Shader.PropertyToID(n); }

        // frame constants
        public static readonly int View = ID("_SSGI_View");
        public static readonly int InvView = ID("_SSGI_InvView");
        public static readonly int PrevView = ID("_SSGI_PrevView");
        public static readonly int Proj = ID("_SSGI_Proj");
        public static readonly int InvProj = ID("_SSGI_InvProj");
        public static readonly int PrevViewProj = ID("_SSGI_PrevViewProj");
        public static readonly int ZParams = ID("_SSGI_ZParams");
        public static readonly int FullSize = ID("_SSGI_FullSize");
        public static readonly int LowSize = ID("_SSGI_LowSize");
        public static readonly int AmbientCube = ID("_SSGI_AmbientCube");
        public static readonly int RayA = ID("_SSGI_RayA");
        public static readonly int RayB = ID("_SSGI_RayB");
        public static readonly int HzbRects = ID("_SSGI_HzbRects");
        public static readonly int Enhanced = ID("_SSGI_Enhanced");
        public static readonly int ResValid = ID("_SSGI_ResValid");
        public static readonly int CacheEnabled = ID("_SSGI_CacheEnabled");
        public static readonly int CacheBase = ID("_SSGI_CacheBase");
        public static readonly int CacheDelta = ID("_SSGI_CacheDelta");
        public static readonly int CacheTex = ID("_SSGI_CacheTex");
        public static readonly int CachePrev = ID("_SSGI_CachePrev");
        public static readonly int CacheCur = ID("_SSGI_CacheCur");
        public static readonly int CacheAcc = ID("_SSGI_CacheAcc");
        public static readonly int ResMaxM = ID("_SSGI_ResMaxM");
        public static readonly int ResSpatial = ID("_SSGI_ResSpatial");
        public static readonly int ResAPrev = ID("_SSGI_ResAPrev");
        public static readonly int ResBPrev = ID("_SSGI_ResBPrev");
        public static readonly int ResCPrev = ID("_SSGI_ResCPrev");
        public static readonly int ResAOut = ID("_SSGI_ResAOut");
        public static readonly int ResBOut = ID("_SSGI_ResBOut");
        public static readonly int ResCOut = ID("_SSGI_ResCOut");
        public static readonly int ResAIn = ID("_SSGI_ResAIn");
        public static readonly int ResBIn = ID("_SSGI_ResBIn");
        public static readonly int ResCIn = ID("_SSGI_ResCIn");
        public static readonly int AgeIn = ID("_SSGI_AgeIn");
        public static readonly int HistFbOut = ID("_SSGI_HistFbOut");
        public static readonly int Near = ID("_SSGI_Near");
        public static readonly int Thickness = ID("_SSGI_Thickness");
        public static readonly int MaxDistance = ID("_SSGI_MaxDistance");
        public static readonly int EdgeFade = ID("_SSGI_EdgeFade");
        public static readonly int PrevWeight = ID("_SSGI_PrevWeight");
        public static readonly int Firefly = ID("_SSGI_FireflyClamp");
        public static readonly int MotionReject = ID("_SSGI_MotionReject");
        public static readonly int FallbackIntensity = ID("_SSGI_FallbackIntensity");
        public static readonly int AlbedoLevel = ID("_SSGI_AlbedoLevel");
        public static readonly int TemporalClamp = ID("_SSGI_TemporalClamp");
        public static readonly int Downsample = ID("_SSGI_Downsample");
        public static readonly int RayCount = ID("_SSGI_RayCount");
        public static readonly int MaxSteps = ID("_SSGI_MaxSteps");
        public static readonly int Bounces = ID("_SSGI_Bounces");
        public static readonly int FrameIndex = ID("_SSGI_FrameIndex");
        public static readonly int NormalMode = ID("_SSGI_NormalMode");
        public static readonly int UsePrevColor = ID("_SSGI_UsePrevColor");
        public static readonly int HistoryValid = ID("_SSGI_HistoryValid");
        public static readonly int MaxAge = ID("_SSGI_MaxAge");
        public static readonly int HasMotion = ID("_SSGI_HasMotion");
        public static readonly int StepSize = ID("_SSGI_StepSize");

        // textures
        public static readonly int Depth = ID("_SSGI_Depth");
        public static readonly int Normals = ID("_SSGI_Normals");
        public static readonly int NormalsRes = ID("_SSGI_NormalsRes");
        public static readonly int NormalsOut = ID("_SSGI_NormalsOut");
        public static readonly int Motion = ID("_SSGI_Motion");
        public static readonly int Color = ID("_SSGI_Color");
        public static readonly int PrevColor = ID("_SSGI_PrevColor");
        public static readonly int HzbAtlas = ID("_SSGI_HzbAtlas");
        public static readonly int HzbOut = ID("_SSGI_HzbOut");
        public static readonly int TraceIn = ID("_SSGI_TraceIn");
        public static readonly int TraceOut = ID("_SSGI_TraceOut");
        public static readonly int LowGeo = ID("_SSGI_LowGeo");
        public static readonly int LowGeoOut = ID("_SSGI_LowGeoOut");
        public static readonly int HistColorPrev = ID("_SSGI_HistColorPrev");
        public static readonly int HistMomPrev = ID("_SSGI_HistMomPrev");
        public static readonly int HistGeoPrev = ID("_SSGI_HistGeoPrev");
        public static readonly int HistColorOut = ID("_SSGI_HistColorOut");
        public static readonly int HistMomOut = ID("_SSGI_HistMomOut");
        public static readonly int HistGeoOut = ID("_SSGI_HistGeoOut");
        public static readonly int FilterIn = ID("_SSGI_FilterIn");
        public static readonly int FilterOut = ID("_SSGI_FilterOut");
        public static readonly int GIOut = ID("_SSGI_GIOut");

        // composite
        public static readonly int CompGI = ID("_SSGICore_GI");
        public static readonly int CompOpaque = ID("_SSGICore_Opaque");
        public static readonly int CompDepth = ID("_SSGICore_Depth");
        public static readonly int CompRaw = ID("_SSGICore_Raw");
        public static readonly int CompAge = ID("_SSGICore_Age");
        public static readonly int CompNormals = ID("_SSGICore_Normals");
        public static readonly int CompParams = ID("_SSGICore_Params");
        public static readonly int CompSize = ID("_SSGICore_Size");
        public static readonly int UserGI = ID("_SSGICore_GITexture");
    }
}
