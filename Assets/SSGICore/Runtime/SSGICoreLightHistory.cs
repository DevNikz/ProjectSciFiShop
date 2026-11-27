using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGICore
{
    /// <summary>Snapshot of the light properties that change how much light a screen region receives.</summary>
    public struct SSGICoreLightSnapshot
    {
        public Vector3 position;
        public Vector3 forward;
        public Color color;     // linear, includes intensity
        public float range;
        public float spotAngle;
        public float scale;     // GI scale from SSGICoreLight
    }

    /// <summary>Pure math for light change detection (kept free of Unity rendering types so it can be unit tested).</summary>
    public static class SSGICoreLightMath
    {
        /// <summary>
        /// 0 = identical, 1 = changed enough that history in the light's range should be dropped. Each term reaches 1 at a
        /// per-frame change that is clearly visible: 2% of the range moved, 1 degree turned, 5% brighter, 5% GI scale.
        /// </summary>
        public static float ComputeDelta(in SSGICoreLightSnapshot a, in SSGICoreLightSnapshot b)
        {
            float range = Mathf.Max(Mathf.Max(a.range, b.range), 0.01f);
            float move = Vector3.Distance(a.position, b.position) / (0.02f * range);
            float turn = Vector3.Angle(a.forward, b.forward);
            float la = Luma(a.color);
            float lb = Luma(b.color);
            float bright = Mathf.Abs(la - lb) / (0.05f * Mathf.Max(Mathf.Max(la, lb), 1e-3f));
            float shape = Mathf.Abs(a.range - b.range) / (0.02f * range) + Mathf.Abs(a.spotAngle - b.spotAngle);
            float scale = Mathf.Abs(a.scale - b.scale) / 0.05f;
            return Mathf.Clamp01(move + turn + bright + shape + scale);
        }

        static float Luma(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }
    }

    /// <summary>
    /// Per-camera CPU-side light state for the Light Response pass: the GI scale of every additional light (in URP's
    /// additional-light index order), the main light's scale, and the "dirty" regions where lights changed since last frame
    /// (moved, brightened, toggled, spawned or removed). Holds no GPU resources.
    /// </summary>
    public sealed class SSGICoreLightHistory : CameraHistoryItem
    {
        public const int MaxLights = 256;      // URP's Forward+ additional light limit
        public const int MaxDirty = 16;

        // read by the pass
        public readonly Vector4[] scale4 = new Vector4[MaxLights / 4];
        public readonly Vector4[] dirtySphere = new Vector4[MaxDirty];   // xyz centre (world), w radius
        public readonly Vector4[] dirtyAmount = new Vector4[MaxDirty / 4];
        public int dirtyCount;
        public float mainScale = 1f;
        public float mainDelta;
        public bool hasWork;

        struct Dirty { public Vector3 center; public float radius; public float amount; }

        readonly Dictionary<int, SSGICoreLightSnapshot> m_Prev = new Dictionary<int, SSGICoreLightSnapshot>();
        readonly Dictionary<int, SSGICoreLightSnapshot> m_Cur = new Dictionary<int, SSGICoreLightSnapshot>();
        readonly List<Dirty> m_Dirty = new List<Dirty>();
        SSGICoreLightSnapshot m_PrevMain;
        int m_PrevMainId;
        int m_LastFrame = -1000;

        public override void Reset()
        {
            m_Prev.Clear();
            m_Cur.Clear();
            m_PrevMainId = 0;
            m_LastFrame = -1000;
        }

        /// <summary>Rebuilds the scale array and the dirty regions from this frame's visible lights.</summary>
        public void Update(UniversalLightData lightData, int frame)
        {
            if (frame == m_LastFrame)
                return;

            bool previousValid = frame - m_LastFrame == 1;
            m_LastFrame = frame;

            for (int i = 0; i < scale4.Length; i++)
                scale4[i] = Vector4.one;

            m_Cur.Clear();
            m_Dirty.Clear();
            mainScale = 1f;
            mainDelta = 0f;
            bool anyScale = false;

            var lights = lightData.visibleLights;
            int mainIndex = lightData.mainLightIndex;
            int additional = 0;
            int mainId = 0;
            SSGICoreLightSnapshot mainSnap = default;

            for (int i = 0; i < lights.Length; i++)
            {
                VisibleLight vl = lights[i];
                Light light = vl.light;
                float scale = SSGICoreLightRegistry.GetScale(light);
                SSGICoreLightSnapshot snap = Snapshot(vl, scale);

                if (i == mainIndex)
                {
                    mainScale = scale;
                    mainSnap = snap;
                    mainId = light != null ? light.GetInstanceID() : 1;
                    anyScale |= !Mathf.Approximately(scale, 1f);
                    continue;
                }

                if (additional < MaxLights)
                {
                    scale4[additional >> 2][additional & 3] = scale;
                    anyScale |= !Mathf.Approximately(scale, 1f);
                }
                additional++;

                // Only point and spot lights have a finite range to flush; additional directional lights are not tracked.
                if (light == null || vl.lightType == LightType.Directional)
                    continue;

                m_Cur[light.GetInstanceID()] = snap;
            }

            if (previousValid)
            {
                foreach (KeyValuePair<int, SSGICoreLightSnapshot> kv in m_Cur)
                {
                    if (m_Prev.TryGetValue(kv.Key, out SSGICoreLightSnapshot prev))
                    {
                        float delta = SSGICoreLightMath.ComputeDelta(prev, kv.Value);
                        if (delta > 0.01f)
                        {
                            AddDirty(prev, delta);        // where it used to light
                            AddDirty(kv.Value, delta);    // where it lights now
                        }
                    }
                    else
                    {
                        AddDirty(kv.Value, 1f);           // spawned or became visible
                    }
                }

                foreach (KeyValuePair<int, SSGICoreLightSnapshot> kv in m_Prev)
                {
                    if (!m_Cur.ContainsKey(kv.Key))
                        AddDirty(kv.Value, 1f);           // removed, disabled or no longer visible
                }

                if (mainId != 0)
                    mainDelta = m_PrevMainId == mainId ? SSGICoreLightMath.ComputeDelta(m_PrevMain, mainSnap) : 1f;
                else if (m_PrevMainId != 0)
                    mainDelta = 1f;
            }

            BuildDirtyArrays();

            m_PrevMain = mainSnap;
            m_PrevMainId = mainId;
            m_Prev.Clear();
            foreach (KeyValuePair<int, SSGICoreLightSnapshot> kv in m_Cur)
                m_Prev[kv.Key] = kv.Value;

            hasWork = anyScale || dirtyCount > 0 || mainDelta > 0.001f;
        }

        void AddDirty(in SSGICoreLightSnapshot s, float amount)
        {
            m_Dirty.Add(new Dirty { center = s.position, radius = Mathf.Max(s.range, 0.01f), amount = amount });
        }

        void BuildDirtyArrays()
        {
            if (m_Dirty.Count > MaxDirty)
            {
                SSGICoreLog.WarnOnce("lightDirty", "More than " + MaxDirty + " lights changed in one frame; only the strongest changes flush temporal history.");
                m_Dirty.Sort((a, b) => b.amount.CompareTo(a.amount));
            }

            dirtyCount = Mathf.Min(m_Dirty.Count, MaxDirty);
            for (int i = 0; i < dirtyAmount.Length; i++)
                dirtyAmount[i] = Vector4.zero;
            for (int i = 0; i < dirtyCount; i++)
            {
                Dirty d = m_Dirty[i];
                dirtySphere[i] = new Vector4(d.center.x, d.center.y, d.center.z, d.radius);
                dirtyAmount[i >> 2][i & 3] = d.amount;
            }
        }

        static SSGICoreLightSnapshot Snapshot(in VisibleLight vl, float scale)
        {
            Matrix4x4 m = vl.localToWorldMatrix;
            return new SSGICoreLightSnapshot
            {
                position = m.GetColumn(3),
                forward = m.GetColumn(2),
                color = vl.finalColor,
                range = vl.range,
                spotAngle = vl.spotAngle,
                scale = scale,
            };
        }
    }
}
