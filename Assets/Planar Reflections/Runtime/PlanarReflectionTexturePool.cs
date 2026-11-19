using System.Collections.Generic;
using UnityEngine;

namespace Studio.PlanarReflections
{
    internal static class PlanarReflectionTexturePool
    {
        const int k_NeverRendered = -1;

        sealed class Entry : IPlanarReflectionPoolEntry
        {
            public RenderTexture Texture;
            public PlanarReflectionTextureSpec TextureSpec;
            public RenderTexture Blurred;
            public PlanarReflectionTextureSpec BlurredSpec;
            public int LastRenderFrame = k_NeverRendered;
            public int LastUsedFrame { get; set; }

            public void Dispose()
            {
                ReleaseTexture(ref Texture);
                ReleaseTexture(ref Blurred);
            }
        }

        static readonly Dictionary<PlanarReflectionKey, Entry> s_Entries = new Dictionary<PlanarReflectionKey, Entry>();

        public static RenderTexture GetOrCreate(
            in PlanarReflectionKey key, in PlanarReflectionTextureSpec spec, string ownerName)
        {
            Entry entry = GetOrAddEntry(key);

            RenderTexture previous = entry.Texture;
            RenderTexture texture = EnsureTexture(
                ref entry.Texture, ref entry.TextureSpec, spec, $"PlanarReflectionRT ({ownerName}, cam {key.CameraId})", key.ReflectorId);

            if (texture != previous)
                entry.LastRenderFrame = k_NeverRendered;

            return texture;
        }

        public static RenderTexture GetOrCreateBlurred(
            in PlanarReflectionKey key, in PlanarReflectionTextureSpec spec, string ownerName)
        {
            Entry entry = GetOrAddEntry(key);

            return EnsureTexture(
                ref entry.Blurred, ref entry.BlurredSpec, spec, $"PlanarReflectionBlur ({ownerName}, cam {key.CameraId})", key.ReflectorId);
        }

        public static void ReleaseBlurred(in PlanarReflectionKey key)
        {
            if (s_Entries.TryGetValue(key, out Entry entry))
                ReleaseTexture(ref entry.Blurred);
        }

        /// <summary>True once the view has rendered. blurred is null when the blur texture is not in use.</summary>
        public static bool TryGet(in PlanarReflectionKey key, out RenderTexture texture, out RenderTexture blurred)
        {
            texture = null;
            blurred = null;

            if (!s_Entries.TryGetValue(key, out Entry entry)
                || entry.Texture == null
                || entry.LastRenderFrame == k_NeverRendered)
                return false;

            entry.LastUsedFrame = Time.frameCount;
            texture = entry.Texture;
            blurred = entry.Blurred;
            return true;
        }

        public static bool IsDue(in PlanarReflectionKey key, int updateInterval)
        {
            if (!s_Entries.TryGetValue(key, out Entry entry) || entry.LastRenderFrame == k_NeverRendered)
                return true;

            return Time.frameCount - entry.LastRenderFrame >= updateInterval;
        }

        public static void MarkRendered(in PlanarReflectionKey key)
        {
            if (s_Entries.TryGetValue(key, out Entry entry))
                entry.LastRenderFrame = Time.frameCount;
        }

        public static void ReleaseReflector(int reflectorId)
        {
            PlanarReflectionPoolUtility.ReleaseReflector(s_Entries, reflectorId);
        }

        public static void PruneUnused(int currentFrame, int maxAgeFrames)
        {
            PlanarReflectionPoolUtility.PruneUnused(s_Entries, currentFrame, maxAgeFrames);
        }

        static Entry GetOrAddEntry(in PlanarReflectionKey key)
        {
            if (!s_Entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry();
                s_Entries[key] = entry;
            }

            entry.LastUsedFrame = Time.frameCount;
            return entry;
        }

        static RenderTexture EnsureTexture(
            ref RenderTexture texture, ref PlanarReflectionTextureSpec current,
            in PlanarReflectionTextureSpec desired, string textureName, int reflectorId)
        {
            if (texture != null && current.Equals(desired))
                return texture;

            ReleaseTexture(ref texture);
            texture = CreateTexture(desired, textureName, reflectorId);
            current = desired;
            return texture;
        }

        static RenderTexture CreateTexture(in PlanarReflectionTextureSpec spec, string textureName, int reflectorId)
        {
            var texture = new RenderTexture(spec.Width, spec.Height, spec.DepthBits, spec.Format)
            {
                name = textureName,
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = spec.UseMips,
                autoGenerateMips = false,
                antiAliasing = spec.Samples,
                enableRandomWrite = spec.RandomWrite,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (texture.Create())
                return texture;

            PlanarReflectionLog.WarnOnce(
                $"rt-create-{reflectorId}-{textureName}",
                $"Failed to create a {spec.Width}x{spec.Height} texture ({spec.Format}, {spec.Samples}x MSAA) for '{textureName}'.");
            PlanarReflectionUtility.SafeDestroy(texture);
            return null;
        }

        static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;

            texture.Release();
            PlanarReflectionUtility.SafeDestroy(texture);
            texture = null;
        }
    }
}
