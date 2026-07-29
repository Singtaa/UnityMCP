using System.Collections.Generic;
using Newtonsoft.Json;

namespace UnityMcp {
    /// <summary>
    /// Serializes arbitrary invocation results (reflection invokes, eval returns) into
    /// JSON-friendly shapes: primitives pass through, Unity objects become
    /// {type, name, instanceId}, collections are truncated at 100 items, and complex
    /// objects fall back to depth-limited JSON.
    /// </summary>
    public static class ResultSerializer {
        public static object Serialize(object result) {
            if (result == null) return null;

            var type = result.GetType();

            // Primitives and strings serialize directly
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return result;

            // Enums as their string representation
            if (type.IsEnum)
                return result.ToString();

            // Unity Objects - return basic info
            if (result is UnityEngine.Object unityObj) {
                return new {
                    type = type.FullName,
                    name = unityObj.name,
                    instanceId = EntityIdCompat.GetIdString(unityObj)
                };
            }

            // Arrays and collections
            if (result is System.Collections.IEnumerable enumerable && !(result is string)) {
                var items = new List<object>();
                foreach (var item in enumerable) {
                    items.Add(Serialize(item));
                    if (items.Count >= 100) {
                        items.Add("... (truncated)");
                        break;
                    }
                }
                return items;
            }

            // Try JSON serialization for complex objects
            try {
                return JsonConvert.DeserializeObject(
                    JsonConvert.SerializeObject(result, new JsonSerializerSettings {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        MaxDepth = 3
                    })
                );
            } catch {
                // Fallback to string representation
                return new {
                    type = type.FullName,
                    toString = result.ToString()
                };
            }
        }
    }
}
