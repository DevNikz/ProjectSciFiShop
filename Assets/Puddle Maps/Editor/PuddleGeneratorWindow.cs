// Tools > Stylized > Puddle Map Generator. Live 256px preview (debounced), full parameter control, presets as starting
// points, bake to PNGs, apply to a Stylized Lit material.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuddleMaps
{
    public sealed class PuddleGeneratorWindow : EditorWindow
    {
        enum PreviewChannel { Mask, Height, Depth, Rim, Normal, Smoothness }
        const int kPreviewSize = 256;
        const double kDebounce = 0.15;

        [SerializeField] PuddleMapSettings settings = new PuddleMapSettings();
        [SerializeField] int resolution = 1024;
        [SerializeField] string outputFolder = "Assets/Puddle Maps/Generated";
        [SerializeField] string baseName = "Puddle";
        [SerializeField] Material targetMaterial;
        [SerializeField] bool assignHeight = true, darkenAlbedo = true, enableSSR = true;
        [SerializeField] PreviewChannel channel = PreviewChannel.Mask;
        [SerializeField] bool preview2x2 = true;

        Texture2D _preview;
        double _dirtyAt = -1;
        Vector2 _scroll;
        PuddleBakeResult _lastBake; bool _hasBake;

        [MenuItem("Tools/Stylized/Puddle Map Generator")]
        static void Open() => GetWindow<PuddleGeneratorWindow>("Puddle Maps").minSize = new Vector2(720, 520);

        void OnEnable()
        {
            if (settings.seed == 0) settings.seed = UnityEngine.Random.Range(1, 100000);
            EditorApplication.update += Tick;
            MarkDirty();
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            if (_preview != null) DestroyImmediate(_preview);
        }

        void MarkDirty() { _dirtyAt = EditorApplication.timeSinceStartup; }

        void Tick()
        {
            if (_dirtyAt < 0 || EditorApplication.timeSinceStartup - _dirtyAt < kDebounce) return;
            _dirtyAt = -1;
            RebuildPreview();
            Repaint();
        }

        void RebuildPreview()
        {
            try
            {
                var s = settings.Clone(); s.Validate();
                var b = PuddleGenerator.Generate(s, kPreviewSize);
                PuddleGenerator.Pack(b, s, out var smooth, out var normal, out var data);
                var px = new Color32[smooth.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    switch (channel)
                    {
                        case PreviewChannel.Normal: px[i] = normal[i]; break;
                        case PreviewChannel.Smoothness: px[i] = Gray(smooth[i].a); break;
                        case PreviewChannel.Height: px[i] = Gray(data[i].g); break;
                        case PreviewChannel.Depth: px[i] = Gray(data[i].r); break;
                        case PreviewChannel.Rim: px[i] = Gray(data[i].b); break;
                        default: px[i] = Gray(data[i].a); break;
                    }
                }
                if (_preview == null) _preview = new Texture2D(kPreviewSize, kPreviewSize, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                _preview.wrapMode = s.tileable ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                _preview.SetPixels32(px);
                _preview.Apply();
            }
            catch (Exception e) { Debug.LogError($"[Puddle] Preview failed: {e.Message}"); }
        }

        static Color32 Gray(byte v) => new Color32(v, v, v, 255);

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Width(360));
            DrawParameters();
            EditorGUILayout.EndScrollView();
            DrawPreview();
            EditorGUILayout.EndHorizontal();
        }

        void DrawParameters()
        {
            var s = settings;
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Presets (starting points)", EditorStyles.boldLabel);
            int col = 0;
            EditorGUILayout.BeginHorizontal();
            foreach (PuddlePreset p in Enum.GetValues(typeof(PuddlePreset)))
            {
                if (GUILayout.Button(ObjectNames.NicifyVariableName(p.ToString()))) s.ApplyPreset(p);
                if (++col % 2 == 0) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            s.kind = (PuddleKind)EditorGUILayout.EnumPopup("Kind", s.kind);
            EditorGUILayout.BeginHorizontal();
            s.seed = EditorGUILayout.IntField("Seed", s.seed);
            if (GUILayout.Button("Random", GUILayout.Width(64))) s.seed = UnityEngine.Random.Range(1, 100000);
            EditorGUILayout.EndHorizontal();
            s.tileable = EditorGUILayout.Toggle(new GUIContent("Tileable", "Seamless wrap. Forces lacunarity 2; cell and lane counts are integers."), s.tileable);

            EditorGUILayout.LabelField("Noise", EditorStyles.boldLabel);
            s.noiseScale = EditorGUILayout.IntSlider("Scale (cells)", s.noiseScale, 1, 32);
            s.octaves = EditorGUILayout.IntSlider("Octaves", s.octaves, 1, 8);
            using (new EditorGUI.DisabledScope(s.tileable)) s.lacunarity = EditorGUILayout.Slider("Lacunarity", s.lacunarity, 1.5f, 3f);
            s.gain = EditorGUILayout.Slider("Gain", s.gain, 0.2f, 0.8f);

            EditorGUILayout.LabelField("Water", EditorStyles.boldLabel);
            s.coverage = EditorGUILayout.Slider("Coverage", s.coverage, 0f, 1f);
            s.edgeSoftness = EditorGUILayout.Slider("Edge Softness", s.edgeSoftness, 0.005f, 0.3f);
            s.edgeNoise = EditorGUILayout.Slider("Edge Noise", s.edgeNoise, 0f, 1f);

            EditorGUILayout.LabelField("Kind settings", EditorStyles.boldLabel);
            switch (s.kind)
            {
                case PuddleKind.Pooled:
                    s.pooledDetail = EditorGUILayout.Slider("Detail", s.pooledDetail, 0f, 1f); break;
                case PuddleKind.Cracked:
                    s.cellCount = EditorGUILayout.IntSlider("Cells", s.cellCount, 2, 32);
                    s.crackWidth = EditorGUILayout.Slider("Crack Width", s.crackWidth, 0.02f, 0.5f);
                    s.crackDepth = EditorGUILayout.Slider("Crack Depth", s.crackDepth, 0f, 1f);
                    s.crackWobble = EditorGUILayout.Slider("Wobble", s.crackWobble, 0f, 1f); break;
                case PuddleKind.Ruts:
                    s.rutLanes = EditorGUILayout.IntSlider("Lanes", s.rutLanes, 1, 16);
                    s.rutSpacing = EditorGUILayout.Slider("Wheel Spacing", s.rutSpacing, 0.05f, 0.9f);
                    s.rutWidth = EditorGUILayout.Slider("Rut Width", s.rutWidth, 0.01f, 0.3f);
                    s.rutBreakup = EditorGUILayout.Slider("Break-up", s.rutBreakup, 0f, 1f);
                    s.rutWobble = EditorGUILayout.Slider("Wobble", s.rutWobble, 0f, 1f); break;
                case PuddleKind.Cobblestone:
                    s.cellCount = EditorGUILayout.IntSlider("Stones (cells)", s.cellCount, 2, 32);
                    s.gapWidth = EditorGUILayout.Slider("Gap Width", s.gapWidth, 0.01f, 0.5f);
                    s.gapSoftness = EditorGUILayout.Slider("Gap Softness", s.gapSoftness, 0.01f, 0.5f);
                    s.stoneVariation = EditorGUILayout.Slider("Stone Height Variation", s.stoneVariation, 0f, 1f); break;
                default:
                    EditorGUILayout.HelpBox("Scattered uses only the noise and water settings.", MessageType.None); break;
            }

            EditorGUILayout.LabelField("Surface", EditorStyles.boldLabel);
            s.normalStrength = EditorGUILayout.Slider("Normal Strength", s.normalStrength, 0f, 4f);
            s.rippleAmount = EditorGUILayout.Slider("Ripples", s.rippleAmount, 0f, 1f);
            s.dryGloss = EditorGUILayout.Slider("Dry Smoothness", s.dryGloss, 0f, 1f);
            s.wetAlbedoMultiplier = EditorGUILayout.Slider("Wet Albedo Multiplier", s.wetAlbedoMultiplier, 0.2f, 1f);

            if (EditorGUI.EndChangeCheck()) { s.Validate(); MarkDirty(); }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);
            resolution = EditorGUILayout.IntPopup("Resolution", resolution,
                new[] { "256", "512", "1024", "2048", "4096" }, new[] { 256, 512, 1024, 2048, 4096 });
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            baseName = EditorGUILayout.TextField("Base Name", baseName);
            if (resolution > 2048) EditorGUILayout.HelpBox("4096 takes a while and uses a lot of memory.", MessageType.Warning);

            targetMaterial = (Material)EditorGUILayout.ObjectField("Material (Stylized Lit)", targetMaterial, typeof(Material), false);
            assignHeight = EditorGUILayout.ToggleLeft("Assign height (parallax)", assignHeight);
            darkenAlbedo = EditorGUILayout.ToggleLeft("Darken albedo in puddles (uses detail slots)", darkenAlbedo);
            enableSSR = EditorGUILayout.ToggleLeft("Enable SSR on material", enableSSR);

            if (GUILayout.Button("Bake Maps")) DoBake(false);
            using (new EditorGUI.DisabledScope(targetMaterial == null))
                if (GUILayout.Button("Bake + Apply To Material")) DoBake(true);
            using (new EditorGUI.DisabledScope(targetMaterial == null || !_hasBake))
                if (GUILayout.Button("Apply Last Bake To Material")) DoApply();
        }

        void DrawPreview()
        {
            EditorGUILayout.BeginVertical();
            EditorGUI.BeginChangeCheck();
            channel = (PreviewChannel)EditorGUILayout.EnumPopup("Preview Channel", channel);
            preview2x2 = EditorGUILayout.ToggleLeft("Show 2x2 tiling (checks seams)", preview2x2);
            if (EditorGUI.EndChangeCheck()) MarkDirty();

            float side = Mathf.Max(64f, Mathf.Min(position.width - 390f, position.height - 70f));
            Rect r = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            if (_preview != null)
            {
                if (preview2x2) GUI.DrawTextureWithTexCoords(r, _preview, new Rect(0, 0, 2, 2));
                else GUI.DrawTexture(r, _preview, ScaleMode.ScaleToFit, false);
            }
            EditorGUILayout.EndVertical();
        }

        void DoBake(bool apply)
        {
            var s = settings.Clone(); s.Validate();
            int size = PuddleMapSettings.ClampResolution(resolution);
            if (Array.Exists(PuddleBaker.TargetPaths(outputFolder, baseName), File.Exists) &&
                !EditorUtility.DisplayDialog("Overwrite puddle maps?", $"Maps named {baseName}_* already exist in {outputFolder}. Overwrite?", "Overwrite", "Cancel"))
                return;

            bool ok;
            PuddleBakeResult res;
            try
            {
                ok = PuddleBaker.Bake(s, size, outputFolder, baseName,
                    (stage, t) => EditorUtility.DisplayCancelableProgressBar("Puddle Maps", stage, t), out res);
            }
            finally { EditorUtility.ClearProgressBar(); }

            if (!ok) return;
            _lastBake = res; _hasBake = true;
            Debug.Log($"[Puddle] Baked {size}x{size} set to {outputFolder}/{baseName}_*.png");
            if (apply) DoApply();
        }

        void DoApply()
        {
            if (targetMaterial == null || !_hasBake) return;
            var overwrites = PuddleMaterialApplier.FindOverwrites(targetMaterial, _lastBake, assignHeight, darkenAlbedo);
            if (overwrites.Count > 0 && !EditorUtility.DisplayDialog("Overwrite material slots?",
                    "These slots hold other textures and will be replaced:\n\n" + string.Join("\n", overwrites), "Replace", "Cancel"))
                return;
            if (PuddleMaterialApplier.Apply(targetMaterial, _lastBake, assignHeight, darkenAlbedo, enableSSR))
                Debug.Log($"[Puddle] Applied to {targetMaterial.name}");
        }
    }
}
