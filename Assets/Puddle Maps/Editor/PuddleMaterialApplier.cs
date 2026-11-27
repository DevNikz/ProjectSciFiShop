// Assigns a baked puddle set to a Stylized Lit material using its existing slots. No compile-time dependency on the
// Stylized Lit folder (it has no asmdef): keywords are recomputed by reflection, with a manual fallback.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PuddleMaps
{
    public static class PuddleMaterialApplier
    {
        public const string StylizedShaderName = "Stylized/Master Lit";

        static Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static void Collect(List<string> list, Material m, string prop, Texture2D incoming)
        {
            if (!m.HasProperty(prop)) return;
            var cur = m.GetTexture(prop);
            if (cur != null && cur != incoming) list.Add($"{prop} ({cur.name})");
        }

        /// <summary>Slots that already hold a different texture and would be overwritten.</summary>
        public static List<string> FindOverwrites(Material m, PuddleBakeResult r, bool height, bool darken)
        {
            var l = new List<string>();
            if (m == null) return l;
            Collect(l, m, "_MetallicGlossMap", Load(r.smoothPath));
            Collect(l, m, "_BumpMap", Load(r.normalPath));
            if (height) Collect(l, m, "_ParallaxMap", Load(r.dataPath));
            if (darken)
            {
                Collect(l, m, "_DetailMask", Load(r.dataPath));
                Collect(l, m, "_DetailAlbedoMap", Load(r.wetPath));
            }
            return l;
        }

        static void SetTex(Material m, string prop, Texture2D t)
        {
            if (m.HasProperty(prop)) m.SetTexture(prop, t); else Debug.LogWarning($"[Puddle] Material has no {prop}; skipped.");
        }

        static void SetFloat(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        public static bool Apply(Material m, PuddleBakeResult r, bool height, bool darken, bool enableSSR)
        {
            if (m == null || m.shader == null || m.shader.name != StylizedShaderName)
            {
                Debug.LogWarning($"[Puddle] Select a material using '{StylizedShaderName}'.");
                return false;
            }
            Undo.RecordObject(m, "Apply Puddle Maps");

            SetTex(m, "_MetallicGlossMap", Load(r.smoothPath));
            SetFloat(m, "_Smoothness", 1f);                 // smoothness = map alpha * _Smoothness
            SetFloat(m, "_SmoothnessInvert", 0f);
            SetFloat(m, "_SmoothnessTextureChannel", 0f);   // metallic-map alpha
            SetTex(m, "_BumpMap", Load(r.normalPath));
            SetFloat(m, "_BumpScale", 1f);
            if (height) SetTex(m, "_ParallaxMap", Load(r.dataPath));
            if (darken)
            {
                SetTex(m, "_DetailMask", Load(r.dataPath));          // samples .a = puddle mask
                SetTex(m, "_DetailAlbedoMap", Load(r.wetPath));
                SetFloat(m, "_DetailAlbedoMapScale", 1f);
            }
            if (enableSSR) { SetFloat(m, "_SSRReflectionEnabled", 1f); SetFloat(m, "_SSRReflectionIntensity", 1f); }

            UpdateKeywords(m, height, darken);
            EditorUtility.SetDirty(m);
            return true;
        }

        static void UpdateKeywords(Material m, bool height, bool darken)
        {
            try
            {
                var t = Type.GetType("StylizedLit.Editor.StylizedLitShaderGUI, Assembly-CSharp");
                var mi = t?.GetMethod("UpdateKeywords", BindingFlags.Public | BindingFlags.Static);
                if (mi != null) { mi.Invoke(null, new object[] { m }); return; }
            }
            catch (Exception e) { Debug.LogWarning($"[Puddle] UpdateKeywords failed ({e.Message}); setting keywords directly."); }

            Debug.LogWarning("[Puddle] StylizedLitShaderGUI.UpdateKeywords not found; setting keywords directly.");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_NORMALMAP");
            if (height) m.EnableKeyword("_PARALLAXMAP");
            if (darken) m.EnableKeyword("_DETAIL_MULX2");
            if (m.HasProperty("_SSRReflectionEnabled") && m.GetFloat("_SSRReflectionEnabled") > 0.5f) m.EnableKeyword("_SSR_REFLECTION_ON");
        }
    }
}
