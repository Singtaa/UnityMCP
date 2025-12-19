using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp {
    public static class Tools_Prefab {
        // MARK: Load
        /// <summary>
        /// Load a prefab asset for inspection/editing. Returns the prefab's root GameObject info
        /// and component list. For editing, use unity_component_get_properties/set_property with
        /// the returned instanceId.
        /// </summary>
        public static ToolResult Load(JObject args) {
            var path = args.Value<string>("path");
            if (string.IsNullOrEmpty(path)) return ToolResultUtil.Text("Missing param: path", true);

            // Normalize path
            if (!path.StartsWith("Assets/") && !path.StartsWith("Assets\\")) {
                path = "Assets/" + path;
            }

            // Load the prefab asset
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) {
                return ToolResultUtil.Text($"Prefab not found at path: {path}", true);
            }

            // Get components on root
            var comps = prefab.GetComponents<Component>();
            var compList = new List<object>();
            foreach (var c in comps) {
                if (c == null) {
                    compList.Add(new { type = "(Missing Script)", instanceId = 0, enabled = (bool?)null });
                    continue;
                }
                compList.Add(new {
                    type = c.GetType().FullName,
                    instanceId = c.GetInstanceID(),
                    enabled = (c is Behaviour b) ? b.enabled : (bool?)null
                });
            }

            // Get child count
            var childCount = prefab.transform.childCount;
            var children = new List<object>();
            for (int i = 0; i < childCount; i++) {
                var child = prefab.transform.GetChild(i);
                children.Add(new {
                    name = child.name,
                    instanceId = child.gameObject.GetInstanceID()
                });
            }

            var result = new {
                path = path,
                name = prefab.name,
                instanceId = prefab.GetInstanceID(),
                components = compList,
                childCount = childCount,
                children = children,
                isPrefabAsset = true
            };

            return ToolResultUtil.Text(JsonConvert.SerializeObject(result, Formatting.Indented));
        }

        // MARK: Save
        /// <summary>
        /// Save changes to a prefab asset. This should be called after modifying prefab
        /// components/properties via unity_component_set_property.
        /// </summary>
        public static ToolResult Save(JObject args) {
            var path = args.Value<string>("path");
            var instanceId = args.Value<int?>("instanceId");

            GameObject prefabRoot = null;

            // Try to find the prefab by instanceId first
            if (instanceId.HasValue) {
                var obj = EditorUtility.InstanceIDToObject(instanceId.Value);
                if (obj is GameObject go) {
                    prefabRoot = go;
                    // Get the asset path from the object
                    var assetPath = AssetDatabase.GetAssetPath(go);
                    if (!string.IsNullOrEmpty(assetPath)) {
                        path = assetPath;
                    }
                }
            }

            // If no instanceId or couldn't find by instanceId, try by path
            if (prefabRoot == null && !string.IsNullOrEmpty(path)) {
                if (!path.StartsWith("Assets/") && !path.StartsWith("Assets\\")) {
                    path = "Assets/" + path;
                }
                prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            if (prefabRoot == null) {
                return ToolResultUtil.Text("Prefab not found. Provide valid path or instanceId.", true);
            }

            if (string.IsNullOrEmpty(path)) {
                path = AssetDatabase.GetAssetPath(prefabRoot);
            }

            // Check if it's actually a prefab asset
            if (!PrefabUtility.IsPartOfPrefabAsset(prefabRoot)) {
                return ToolResultUtil.Text("The specified object is not a prefab asset.", true);
            }

            try {
                // Mark the prefab dirty and save
                EditorUtility.SetDirty(prefabRoot);
                AssetDatabase.SaveAssetIfDirty(prefabRoot);

                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    message = "Prefab saved successfully.",
                    path = path,
                    instanceId = prefabRoot.GetInstanceID()
                }));
            } catch (Exception e) {
                return ToolResultUtil.Text($"Failed to save prefab: {e.Message}", true);
            }
        }

        // MARK: GetHierarchy
        /// <summary>
        /// Get the full hierarchy of a prefab, including all children and their components.
        /// </summary>
        public static ToolResult GetHierarchy(JObject args) {
            var path = args.Value<string>("path");
            var instanceId = args.Value<int?>("instanceId");
            var maxDepth = args.Value<int?>("maxDepth") ?? 10;

            GameObject prefabRoot = null;

            if (instanceId.HasValue) {
                var obj = EditorUtility.InstanceIDToObject(instanceId.Value);
                if (obj is GameObject go) {
                    prefabRoot = go;
                }
            }

            if (prefabRoot == null && !string.IsNullOrEmpty(path)) {
                if (!path.StartsWith("Assets/") && !path.StartsWith("Assets\\")) {
                    path = "Assets/" + path;
                }
                prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            if (prefabRoot == null) {
                return ToolResultUtil.Text("Prefab not found. Provide valid path or instanceId.", true);
            }

            var hierarchy = BuildHierarchy(prefabRoot.transform, 0, maxDepth);

            var result = new {
                path = AssetDatabase.GetAssetPath(prefabRoot),
                hierarchy = hierarchy
            };

            return ToolResultUtil.Text(JsonConvert.SerializeObject(result, Formatting.Indented));
        }

        static object BuildHierarchy(Transform t, int depth, int maxDepth) {
            var comps = t.GetComponents<Component>();
            var compList = comps.Where(c => c != null).Select(c => new {
                type = c.GetType().Name,
                instanceId = c.GetInstanceID()
            }).ToList();

            var children = new List<object>();
            if (depth < maxDepth) {
                for (int i = 0; i < t.childCount; i++) {
                    children.Add(BuildHierarchy(t.GetChild(i), depth + 1, maxDepth));
                }
            }

            return new {
                name = t.name,
                instanceId = t.gameObject.GetInstanceID(),
                components = compList,
                children = children
            };
        }

        // MARK: FindComponent
        /// <summary>
        /// Find a component within a prefab by path (e.g., "Child/GrandChild") and type.
        /// </summary>
        public static ToolResult FindComponent(JObject args) {
            var prefabPath = args.Value<string>("prefabPath");
            var childPath = args.Value<string>("childPath");
            var typeName = args.Value<string>("type");

            if (string.IsNullOrEmpty(prefabPath)) return ToolResultUtil.Text("Missing param: prefabPath", true);
            if (string.IsNullOrEmpty(typeName)) return ToolResultUtil.Text("Missing param: type", true);

            if (!prefabPath.StartsWith("Assets/") && !prefabPath.StartsWith("Assets\\")) {
                prefabPath = "Assets/" + prefabPath;
            }

            var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabRoot == null) {
                return ToolResultUtil.Text($"Prefab not found at path: {prefabPath}", true);
            }

            // Find the target transform
            Transform target = prefabRoot.transform;
            if (!string.IsNullOrEmpty(childPath)) {
                target = prefabRoot.transform.Find(childPath);
                if (target == null) {
                    return ToolResultUtil.Text($"Child not found at path: {childPath}", true);
                }
            }

            // Find component by type name
            var type = FindComponentType(typeName);
            if (type == null) {
                return ToolResultUtil.Text($"Unknown component type: {typeName}", true);
            }

            var comp = target.GetComponent(type);
            if (comp == null) {
                return ToolResultUtil.Text($"Component {typeName} not found on {target.name}", true);
            }

            // Get properties
            var so = new SerializedObject(comp);
            var props = new Dictionary<string, object>();
            var iter = so.GetIterator();

            if (iter.NextVisible(true)) {
                do {
                    props[iter.propertyPath] = GetSerializedValue(iter);
                } while (iter.NextVisible(false));
            }

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                prefabPath = prefabPath,
                childPath = childPath ?? "",
                componentType = comp.GetType().FullName,
                instanceId = comp.GetInstanceID(),
                properties = props
            }, Formatting.Indented));
        }

        // MARK: Helpers
        static Type FindComponentType(string name) {
            if (string.IsNullOrEmpty(name)) return null;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    foreach (var t in asm.GetTypes()) {
                        if (!typeof(Component).IsAssignableFrom(t)) continue;
                        if (t.IsAbstract) continue;

                        if (t.FullName != null && t.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                            return t;
                        }
                        if (t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                            return t;
                        }
                    }
                } catch {
                    // Some assemblies may throw on GetTypes()
                }
            }

            return null;
        }

        static object GetSerializedValue(SerializedProperty prop) {
            switch (prop.propertyType) {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.Float: return prop.floatValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Enum: return prop.enumNames[prop.enumValueIndex];
                case SerializedPropertyType.Vector2: return new[] { prop.vector2Value.x, prop.vector2Value.y };
                case SerializedPropertyType.Vector3: return new[] { prop.vector3Value.x, prop.vector3Value.y, prop.vector3Value.z };
                case SerializedPropertyType.Vector4: return new[] { prop.vector4Value.x, prop.vector4Value.y, prop.vector4Value.z, prop.vector4Value.w };
                case SerializedPropertyType.Color: return new[] { prop.colorValue.r, prop.colorValue.g, prop.colorValue.b, prop.colorValue.a };
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null
                        ? new { instanceId = prop.objectReferenceValue.GetInstanceID(), name = prop.objectReferenceValue.name }
                        : null;
                default: return $"({prop.propertyType})";
            }
        }
    }
}
