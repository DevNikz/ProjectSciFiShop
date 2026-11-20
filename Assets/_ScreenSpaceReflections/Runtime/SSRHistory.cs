// BLK-04  Per-camera ping-pong history. Owns RTHandle lifetime. Knows nothing about shaders.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SSRFeature
{
    internal sealed class SSRHistory : IDisposable
    {
        sealed class Entry
        {
            public RTHandle a, b;
            public int w, h, flip, lastFrame = -100, cutGen;
        }

        readonly Dictionary<Camera, Entry> _map = new Dictionary<Camera, Entry>();
        readonly List<Camera> _dead = new List<Camera>();

        /// <summary>Call once per camera per frame. Returns true if <paramref name="read"/> holds last frame's valid result.</summary>
        public bool Acquire(Camera cam, int w, int h, out RTHandle read, out RTHandle write)
        {
            Purge();
            if (!_map.TryGetValue(cam, out var e)) { e = new Entry { cutGen = SSRAccessibility.CutGeneration }; _map[cam] = e; }

            bool valid = true;
            if (e.a == null || e.w != w || e.h != h)
            {
                Release(e);
                e.a = Alloc("_SSRHistoryA", w, h);
                e.b = Alloc("_SSRHistoryB", w, h);
                e.w = w; e.h = h; valid = false;
            }
            if (Time.frameCount - e.lastFrame > 1) valid = false;          // camera skipped frames => stale
            if (e.cutGen != SSRAccessibility.CutGeneration) { valid = false; e.cutGen = SSRAccessibility.CutGeneration; }

            e.flip ^= 1;
            read  = e.flip == 0 ? e.a : e.b;
            write = e.flip == 0 ? e.b : e.a;
            e.lastFrame = Time.frameCount;
            return valid;
        }

        static RTHandle Alloc(string name, int w, int h) =>
            RTHandles.Alloc(w, h, colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                            filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, name: name);

        static void Release(Entry e) { e.a?.Release(); e.b?.Release(); e.a = e.b = null; }

        void Purge()
        {
            _dead.Clear();
            foreach (var kv in _map) if (kv.Key == null) _dead.Add(kv.Key);
            foreach (var k in _dead) { Release(_map[k]); _map.Remove(k); }
        }

        public void Dispose()
        {
            foreach (var kv in _map) Release(kv.Value);
            _map.Clear();
        }
    }
}
