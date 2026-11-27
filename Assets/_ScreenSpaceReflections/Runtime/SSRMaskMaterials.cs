// BLK-08  One mask-shader material per source material; copies the smoothness-relevant values each frame.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SSRFeature
{
    internal sealed class SSRMaskMaterials : IDisposable
    {
        sealed class Entry { public Material mask; public int frame = -1; }

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        static readonly int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int SmoothnessChannel = Shader.PropertyToID("_SmoothnessTextureChannel");
        static readonly int Surface = Shader.PropertyToID("_Surface");
        static readonly int SpecGlossMap = Shader.PropertyToID("_SpecGlossMap");
        static readonly int SmoothnessInvert = Shader.PropertyToID("_SmoothnessInvert");

        readonly Dictionary<Material, Entry> _map = new Dictionary<Material, Entry>();
        readonly List<Material> _dead = new List<Material>();
        public Shader Shader { get; }

        public SSRMaskMaterials(Shader shader) { Shader = shader; }

        /// <summary>Returns null for transparent materials (they are not in the depth texture).</summary>
        public Material Get(Material src, float fallbackSmoothness)
        {
            if (src == null) return null;
            if (src.HasProperty(Surface) && src.GetFloat(Surface) > 0.5f) return null;

            if (!_map.TryGetValue(src, out var e)) { e = new Entry { mask = CoreUtils.CreateEngineMaterial(Shader) }; _map[src] = e; }
            if (e.frame != Time.frameCount) { Sync(src, e.mask, fallbackSmoothness); e.frame = Time.frameCount; }
            return e.mask;
        }

        static void Sync(Material src, Material dst, float fallback)
        {
            Texture bm = src.HasProperty(BaseMap) ? src.GetTexture(BaseMap) : null;
            dst.SetTexture(BaseMap, bm != null ? bm : Texture2D.whiteTexture);
            if (bm != null)
            {
                Vector2 sc = src.GetTextureScale(BaseMap), of = src.GetTextureOffset(BaseMap);
                dst.SetVector(BaseMapST, new Vector4(sc.x, sc.y, of.x, of.y));
            }
            else dst.SetVector(BaseMapST, new Vector4(1, 1, 0, 0));

            // Specular workflow (Stylized Lit / URP Lit) keeps smoothness in _SpecGlossMap; feed it through the mask's gloss slot.
            bool specular = src.IsKeywordEnabled("_SPECULAR_SETUP");
            int glossId = specular && src.HasProperty(SpecGlossMap) ? SpecGlossMap : MetallicGlossMap;
            Texture mg = src.HasProperty(glossId) ? src.GetTexture(glossId) : null;
            dst.SetTexture(MetallicGlossMap, mg != null ? mg : Texture2D.whiteTexture);

            dst.SetFloat(Smoothness, src.HasProperty(Smoothness) ? src.GetFloat(Smoothness) : fallback);

            bool albedoAlpha = src.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A")
                               || (src.HasProperty(SmoothnessChannel) && src.GetFloat(SmoothnessChannel) > 0.5f);
            dst.SetFloat(SmoothnessChannel, albedoAlpha ? 1f : 0f);

            // Invert only applies when a map/albedo alpha is the source (matches StylizedLit SampleMetallicSpecGloss); a constant must not invert.
            bool hasSource = albedoAlpha ? bm != null : mg != null;
            dst.SetFloat(SmoothnessInvert, hasSource && src.HasProperty(SmoothnessInvert) ? src.GetFloat(SmoothnessInvert) : 0f);
        }

        public void Purge()
        {
            _dead.Clear();
            foreach (var kv in _map) if (kv.Key == null) _dead.Add(kv.Key);
            foreach (var k in _dead) { CoreUtils.Destroy(_map[k].mask); _map.Remove(k); }
        }

        public void Dispose()
        {
            foreach (var kv in _map) CoreUtils.Destroy(kv.Value.mask);
            _map.Clear();
        }
    }
}
