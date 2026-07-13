using UnityEditor;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// Version-portable object-id helpers. Unity 6.4 deprecates the InstanceID APIs in favor
    /// of 64-bit EntityId (hard compile errors on 6.5+), while older supported versions
    /// (2022.3+) only have InstanceID. Ids cross the MCP wire as JSON numbers (long) and are
    /// only meaningful within the current editor session.
    /// </summary>
    public static class EntityIdCompat {
        public static long GetId(Object obj) {
#if UNITY_6000_4_OR_NEWER
            return (long)EntityId.ToULong(obj.GetEntityId());
#else
            return obj.GetInstanceID();
#endif
        }

        public static Object IdToObject(long id) {
#if UNITY_6000_4_OR_NEWER
            return EditorUtility.EntityIdToObject(EntityId.FromULong((ulong)id));
#elif UNITY_6000_3_OR_NEWER
            // EntityIdToObject exists and InstanceIDToObject already warns on 6000.3
            return EditorUtility.EntityIdToObject((int)id);
#else
            return EditorUtility.InstanceIDToObject((int)id);
#endif
        }
    }
}
