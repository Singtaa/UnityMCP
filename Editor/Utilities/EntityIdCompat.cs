using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// Version-portable object-id helpers. Unity 6.4 deprecates the InstanceID APIs in favor
    /// of 64-bit EntityId (hard compile errors on 6.5+), while older supported versions
    /// (2022.3+) only have InstanceID. Ids cross the MCP wire as JSON STRINGS: EntityId values
    /// exceed JavaScript's 2^53 safe-integer range, so a raw JSON number gets corrupted by the
    /// Node relay's JSON.parse. Inputs accept both string and number (small legacy ids survive
    /// the double round-trip). Ids are only meaningful within the current editor session.
    /// </summary>
    public static class EntityIdCompat {
        /// <summary>Id as a string for tool output. See class remarks for why not a number.</summary>
        public static string GetIdString(Object obj) => GetId(obj).ToString();

        /// <summary>Parse an id from a tool argument token (string or number). Returns null if absent or malformed.</summary>
        public static long? ParseId(JToken token) {
            if (token == null) return null;
            switch (token.Type) {
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.String:
                    return long.TryParse(token.Value<string>(), out var id) ? id : (long?)null;
                default:
                    return null;
            }
        }
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
