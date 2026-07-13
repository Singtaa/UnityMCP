using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// Version-portable wrapper for UnityEngine.Object.FindObjectsByType. The FindObjectsSortMode
    /// overloads are obsolete on Unity 6.4+ (slated to become hard errors), but their
    /// parameterless replacements only exist on 6000.4+.
    /// </summary>
    public static class FindCompat {
        public static T[] FindObjectsByType<T>() where T : Object {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>();
#else
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#endif
        }
    }
}
