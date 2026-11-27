// Writes the packed puddle set to PNG assets and sets importer flags. Errors: Debug.LogError("[Puddle] ...") and return false.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuddleMaps
{
    public struct PuddleBakeResult
    {
        public string smoothPath, normalPath, dataPath, wetPath;
    }

    public static class PuddleBaker
    {
        public static string[] TargetPaths(string folder, string baseName) => new[]
        {
            $"{folder}/{baseName}_Smoothness.png", $"{folder}/{baseName}_Normal.png",
            $"{folder}/{baseName}_PuddleData.png", $"{folder}/{baseName}_WetDarken.png",
        };

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static void WritePng(string assetPath, Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            try
            {
                tex.SetPixels32(px);
                string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
                File.WriteAllBytes(full, tex.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        static void Configure(string path, bool normalMap, bool srgb, bool tileable)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning($"[Puddle] No TextureImporter for {path}; import settings not applied."); return; }
            ti.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = srgb;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            ti.wrapMode = tileable ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }

        /// <summary>Returns false on cancel or failure. s must already be validated.</summary>
        public static bool Bake(PuddleMapSettings s, int size, string folder, string baseName,
                                Func<string, float, bool> progress, out PuddleBakeResult result)
        {
            result = default;
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets")) { Debug.LogError("[Puddle] Output folder must be inside Assets."); return false; }
            if (string.IsNullOrWhiteSpace(baseName)) { Debug.LogError("[Puddle] Base name is empty."); return false; }
            try
            {
                var b = PuddleGenerator.Generate(s, size, progress);
                if (b == null) return false;                                     // cancelled
                if (progress != null && progress("Packing and writing", 0.85f)) return false;
                PuddleGenerator.Pack(b, s, out var smooth, out var normal, out var data);

                EnsureFolder(folder);
                var p = TargetPaths(folder, baseName);
                WritePng(p[0], smooth, size, size);
                WritePng(p[1], normal, size, size);
                WritePng(p[2], data, size, size);
                var wet = new Color32[16]; var texel = PuddleGenerator.WetDarkenTexel(s);
                for (int i = 0; i < 16; i++) wet[i] = texel;
                WritePng(p[3], wet, 4, 4);

                AssetDatabase.Refresh();
                for (int i = 0; i < p.Length; i++) AssetDatabase.ImportAsset(p[i], ImportAssetOptions.ForceSynchronousImport);
                Configure(p[0], false, false, s.tileable);
                Configure(p[1], true, false, s.tileable);
                Configure(p[2], false, false, s.tileable);
                Configure(p[3], false, false, true);
                result = new PuddleBakeResult { smoothPath = p[0], normalPath = p[1], dataPath = p[2], wetPath = p[3] };
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Puddle] Bake failed: {e.Message}");
                return false;
            }
        }
    }
}
