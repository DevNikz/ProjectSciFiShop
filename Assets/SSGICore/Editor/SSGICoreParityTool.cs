using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SSGICore.Editor
{
    /// <summary>
    /// Parity gate for the lossless optimisation phases. Renders the main camera with a fixed noise frame,
    /// temporal accumulation off and the GI debug view, then compares against a stored reference.
    /// Use a static scene (no animation) and enable HDR in the URP asset so the GI values are not clamped.
    /// </summary>
    public static class SSGICoreParityTool
    {
        const string kDir = "Assets/SSGICore/Docs/Parity";
        const string kReference = kDir + "/reference.ssgiparity";
        const int kFixedFrame = 1234;
        const int kWidth = 1920;
        const int kHeight = 1080;
        const int kWarmupRenders = 3; // history is only available from the second render of a camera

        [MenuItem("Tools/SSGICore/Parity/Capture Reference")]
        static void CaptureReference()
        {
            if (!TryCapture(out Color[] pixels))
                return;

            Directory.CreateDirectory(kDir);
            using (var w = new BinaryWriter(File.Open(kReference, FileMode.Create)))
            {
                w.Write(kWidth);
                w.Write(kHeight);
                foreach (Color c in pixels)
                {
                    w.Write(c.r);
                    w.Write(c.g);
                    w.Write(c.b);
                }
            }
            AssetDatabase.Refresh();
            Debug.Log("[SSGICore] Parity reference captured: " + kReference);
        }

        [MenuItem("Tools/SSGICore/Parity/Compare With Reference")]
        static void CompareWithReference()
        {
            if (!File.Exists(kReference))
            {
                Debug.LogWarning("[SSGICore] No parity reference found. Run 'Capture Reference' on the baseline build first.");
                return;
            }
            if (!TryCapture(out Color[] candidate))
                return;

            Color[] reference;
            using (var r = new BinaryReader(File.OpenRead(kReference)))
            {
                int w = r.ReadInt32();
                int h = r.ReadInt32();
                if (w != kWidth || h != kHeight)
                {
                    Debug.LogWarning("[SSGICore] Parity reference resolution does not match; recapture it.");
                    return;
                }
                reference = new Color[w * h];
                for (int i = 0; i < reference.Length; i++)
                    reference[i] = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), 1f);
            }

            float rmse = SSGICoreParity.RelativeRmse(reference, candidate);
            string verdict = rmse <= SSGICoreParity.GateRelativeRmse ? "PASS" : "FAIL";
            Debug.Log("[SSGICore] Parity " + verdict + ": relative RMSE " + (rmse * 100f).ToString("F3") +
                      "% (gate " + (SSGICoreParity.GateRelativeRmse * 100f).ToString("F1") + "%)");
        }

        [MenuItem("Tools/SSGICore/Parity/Capture Reference", true)]
        [MenuItem("Tools/SSGICore/Parity/Compare With Reference", true)]
        static bool ValidatePlaying() { return Application.isPlaying; }

        static bool TryCapture(out Color[] pixels)
        {
            pixels = null;
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[SSGICore] Parity capture needs a camera tagged MainCamera.");
                return false;
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var ssgi = profile.Add<SSGICoreVolume>(true);
            ssgi.enable.Override(true);
            ssgi.quality.Override(SSGICoreQualityPreset.Custom);
            ssgi.temporal.Override(false);
            ssgi.lightingSource.Override(SSGICoreLightingSource.CurrentFrame);
            ssgi.debugView.Override(SSGICoreDebugView.GI);

            var go = new GameObject("SSGICore Parity Volume") { hideFlags = HideFlags.HideAndDontSave };
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            volume.sharedProfile = profile;

            var rt = new RenderTexture(kWidth, kHeight, 24, RenderTextureFormat.ARGBHalf);
            RenderTexture previousTarget = cam.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            SSGICoreTestHooks.FrameOverride = kFixedFrame;
            try
            {
                cam.targetTexture = rt;
                for (int i = 0; i < kWarmupRenders; i++)
                    cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(kWidth, kHeight, TextureFormat.RGBAFloat, false, true);
                tex.ReadPixels(new Rect(0, 0, kWidth, kHeight), 0, 0);
                pixels = tex.GetPixels();
                Object.DestroyImmediate(tex);
            }
            finally
            {
                SSGICoreTestHooks.FrameOverride = -1;
                cam.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(profile);
            }
            return pixels != null;
        }
    }
}
