using System;
using System.Collections.Generic;

namespace Studio.PlanarReflections
{
    /// <summary>Identifies one reflection view: a reflector as seen by one source camera.</summary>
    internal readonly struct PlanarReflectionKey : IEquatable<PlanarReflectionKey>
    {
        public readonly int ReflectorId;
        public readonly int CameraId;

        public PlanarReflectionKey(int reflectorId, int cameraId)
        {
            ReflectorId = reflectorId;
            CameraId = cameraId;
        }

        public bool Equals(PlanarReflectionKey other)
        {
            return ReflectorId == other.ReflectorId && CameraId == other.CameraId;
        }

        public override bool Equals(object obj)
        {
            return obj is PlanarReflectionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked { return (ReflectorId * 397) ^ CameraId; }
        }
    }

    internal interface IPlanarReflectionPoolEntry
    {
        int LastUsedFrame { get; }
        void Dispose();
    }

    internal static class PlanarReflectionPoolUtility
    {
        static readonly List<PlanarReflectionKey> s_KeyScratch = new List<PlanarReflectionKey>();

        public static void ReleaseReflector<T>(Dictionary<PlanarReflectionKey, T> entries, int reflectorId)
            where T : class, IPlanarReflectionPoolEntry
        {
            s_KeyScratch.Clear();

            foreach (KeyValuePair<PlanarReflectionKey, T> pair in entries)
            {
                if (pair.Key.ReflectorId == reflectorId)
                    s_KeyScratch.Add(pair.Key);
            }

            DisposeAndRemoveScratchKeys(entries);
        }

        public static void PruneUnused<T>(Dictionary<PlanarReflectionKey, T> entries, int currentFrame, int maxAgeFrames)
            where T : class, IPlanarReflectionPoolEntry
        {
            s_KeyScratch.Clear();

            foreach (KeyValuePair<PlanarReflectionKey, T> pair in entries)
            {
                if (currentFrame - pair.Value.LastUsedFrame > maxAgeFrames)
                    s_KeyScratch.Add(pair.Key);
            }

            DisposeAndRemoveScratchKeys(entries);
        }

        static void DisposeAndRemoveScratchKeys<T>(Dictionary<PlanarReflectionKey, T> entries)
            where T : class, IPlanarReflectionPoolEntry
        {
            for (int i = 0; i < s_KeyScratch.Count; i++)
            {
                PlanarReflectionKey key = s_KeyScratch[i];
                if (!entries.TryGetValue(key, out T entry))
                    continue;

                entry.Dispose();
                entries.Remove(key);
            }

            s_KeyScratch.Clear();
        }
    }
}
