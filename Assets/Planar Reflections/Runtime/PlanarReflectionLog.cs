using System.Collections.Generic;
using UnityEngine;

namespace Studio.PlanarReflections
{
    internal static class PlanarReflectionLog
    {
        const string k_Prefix = "[PlanarReflections] ";
        static readonly HashSet<string> s_LoggedKeys = new HashSet<string>();

        public static void WarnOnce(string key, string message, Object context = null)
        {
            if (!s_LoggedKeys.Add(key))
                return;

            Debug.LogWarning(k_Prefix + message, context);
        }
    }

    internal static class PlanarReflectionUtility
    {
        public static void SafeDestroy(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
