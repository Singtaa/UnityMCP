using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityMcp {
    public static class Tools_Component {
        // MARK: List
        public static ToolResult List(JObject args) {
            var id = args.Value<string>("target");
            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            var comps = go.GetComponents<Component>();
            var list = new List<object>();

            foreach (var c in comps) {
                if (c == null) {
                    list.Add(new { type = "(Missing Script)", instanceId = 0, enabled = (bool?)null });
                    continue;
                }
                list.Add(new {
                    type = c.GetType().FullName,
                    instanceId = c.GetInstanceID(),
                    enabled = (c is Behaviour b) ? b.enabled : (bool?)null
                });
            }

            return ToolResultUtil.Text(JsonConvert.SerializeObject(list, Formatting.Indented));
        }

        // MARK: Add
        public static ToolResult Add(JObject args) {
            var id = args.Value<string>("target");
            var typeName = args.Value<string>("type");

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);
            if (string.IsNullOrEmpty(typeName)) return ToolResultUtil.Text("Missing param: type", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            var type = FindComponentType(typeName);
            if (type == null) {
                return ToolResultUtil.Text($"Unknown component type: {typeName}", true);
            }

            var comp = Undo.AddComponent(go, type);
            if (comp == null) {
                return ToolResultUtil.Text($"Failed to add component: {typeName}", true);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                type = comp.GetType().FullName,
                instanceId = comp.GetInstanceID()
            }));
        }

        // MARK: Remove
        public static ToolResult Remove(JObject args) {
            var id = args.Value<string>("target");
            var typeName = args.Value<string>("type");
            var compInstanceId = args.Value<int?>("componentInstanceId");

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            Component toRemove = null;

            if (compInstanceId.HasValue) {
                toRemove = go.GetComponents<Component>()
                    .FirstOrDefault(c => c != null && c.GetInstanceID() == compInstanceId.Value);
            } else if (!string.IsNullOrEmpty(typeName)) {
                var type = FindComponentType(typeName);
                if (type != null) {
                    toRemove = go.GetComponent(type);
                }
            } else {
                return ToolResultUtil.Text("Missing param: type or componentInstanceId", true);
            }

            if (toRemove == null) {
                return ToolResultUtil.Text("Component not found.", true);
            }

            if (toRemove is Transform) {
                return ToolResultUtil.Text("Cannot remove Transform component.", true);
            }

            var scene = go.scene;
            Undo.DestroyObjectImmediate(toRemove);
            EditorSceneManager.MarkSceneDirty(scene);

            return ToolResultUtil.Text("Component removed.");
        }

        // MARK: SetEnabled
        public static ToolResult SetEnabled(JObject args) {
            var id = args.Value<string>("target");
            var typeName = args.Value<string>("type");
            var compInstanceId = args.Value<int?>("componentInstanceId");
            var enabled = args.Value<bool?>("enabled") ?? true;

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            Component comp = null;

            if (compInstanceId.HasValue) {
                comp = go.GetComponents<Component>()
                    .FirstOrDefault(c => c != null && c.GetInstanceID() == compInstanceId.Value);
            } else if (!string.IsNullOrEmpty(typeName)) {
                var type = FindComponentType(typeName);
                if (type != null) {
                    comp = go.GetComponent(type);
                }
            } else {
                return ToolResultUtil.Text("Missing param: type or componentInstanceId", true);
            }

            if (comp == null) {
                return ToolResultUtil.Text("Component not found.", true);
            }

            if (comp is Behaviour behaviour) {
                Undo.RecordObject(behaviour, "[MCP] SetEnabled");
                behaviour.enabled = enabled;
                EditorSceneManager.MarkSceneDirty(go.scene);
                return ToolResultUtil.Text($"Set enabled={enabled}: {comp.GetType().Name}");
            }

            return ToolResultUtil.Text("Component is not a Behaviour and cannot be enabled/disabled.", true);
        }

        // MARK: GetProperties
        public static ToolResult GetProperties(JObject args) {
            var id = args.Value<string>("target");
            var typeName = args.Value<string>("type");
            var compInstanceId = args.Value<int?>("componentInstanceId");

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            Component comp = null;

            if (compInstanceId.HasValue) {
                comp = go.GetComponents<Component>()
                    .FirstOrDefault(c => c != null && c.GetInstanceID() == compInstanceId.Value);
            } else if (!string.IsNullOrEmpty(typeName)) {
                var type = FindComponentType(typeName);
                if (type != null) {
                    comp = go.GetComponent(type);
                }
            } else {
                return ToolResultUtil.Text("Missing param: type or componentInstanceId", true);
            }

            if (comp == null) {
                return ToolResultUtil.Text("Component not found.", true);
            }

            var so = new SerializedObject(comp);
            var props = new Dictionary<string, object>();
            var iter = so.GetIterator();

            // Use Next(true) to enter all children including array elements
            bool enterChildren = true;
            while (iter.Next(enterChildren)) {
                // Skip script reference
                if (iter.propertyPath == "m_Script") {
                    enterChildren = false;
                    continue;
                }
                props[iter.propertyPath] = GetSerializedValue(iter);
                enterChildren = true;
            }

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                type = comp.GetType().FullName,
                instanceId = comp.GetInstanceID(),
                properties = props
            }, Formatting.Indented));
        }

        // MARK: SetProperty
        public static ToolResult SetProperty(JObject args) {
            var id = args.Value<string>("target");
            var typeName = args.Value<string>("type");
            var compInstanceId = args.Value<int?>("componentInstanceId");
            var propertyPath = args.Value<string>("property");
            var valueToken = args["value"];

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);
            if (string.IsNullOrEmpty(propertyPath)) return ToolResultUtil.Text("Missing param: property", true);
            if (valueToken == null) return ToolResultUtil.Text("Missing param: value", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            Component comp = null;

            if (compInstanceId.HasValue) {
                comp = go.GetComponents<Component>()
                    .FirstOrDefault(c => c != null && c.GetInstanceID() == compInstanceId.Value);
            } else if (!string.IsNullOrEmpty(typeName)) {
                var type = FindComponentType(typeName);
                if (type != null) {
                    comp = go.GetComponent(type);
                }
            } else {
                return ToolResultUtil.Text("Missing param: type or componentInstanceId", true);
            }

            if (comp == null) {
                return ToolResultUtil.Text("Component not found.", true);
            }

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);

            if (prop == null) {
                return ToolResultUtil.Text($"Property not found: {propertyPath}", true);
            }

            Undo.RecordObject(comp, "[MCP] SetProperty");

            if (!SetSerializedValue(prop, valueToken, out var setErr)) {
                return ToolResultUtil.Text(setErr, true);
            }

            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text($"Set {propertyPath} on {comp.GetType().Name}");
        }

        // MARK: Helpers
        static Type FindComponentType(string name) {
            if (string.IsNullOrEmpty(name)) return null;

            // Common Unity types shorthand
            var unityShorthands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["Rigidbody"] = "UnityEngine.Rigidbody",
                ["Rigidbody2D"] = "UnityEngine.Rigidbody2D",
                ["BoxCollider"] = "UnityEngine.BoxCollider",
                ["SphereCollider"] = "UnityEngine.SphereCollider",
                ["CapsuleCollider"] = "UnityEngine.CapsuleCollider",
                ["MeshCollider"] = "UnityEngine.MeshCollider",
                ["BoxCollider2D"] = "UnityEngine.BoxCollider2D",
                ["CircleCollider2D"] = "UnityEngine.CircleCollider2D",
                ["AudioSource"] = "UnityEngine.AudioSource",
                ["AudioListener"] = "UnityEngine.AudioListener",
                ["Camera"] = "UnityEngine.Camera",
                ["Light"] = "UnityEngine.Light",
                ["MeshRenderer"] = "UnityEngine.MeshRenderer",
                ["MeshFilter"] = "UnityEngine.MeshFilter",
                ["SkinnedMeshRenderer"] = "UnityEngine.SkinnedMeshRenderer",
                ["Animator"] = "UnityEngine.Animator",
                ["Animation"] = "UnityEngine.Animation",
                ["Canvas"] = "UnityEngine.Canvas",
                ["CanvasRenderer"] = "UnityEngine.CanvasRenderer",
                ["RectTransform"] = "UnityEngine.RectTransform",
                ["Image"] = "UnityEngine.UI.Image",
                ["Text"] = "UnityEngine.UI.Text",
                ["Button"] = "UnityEngine.UI.Button",
            };

            if (unityShorthands.TryGetValue(name, out var fullName)) {
                name = fullName;
            }

            // Search all assemblies
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
                case SerializedPropertyType.Rect: return new[] { prop.rectValue.x, prop.rectValue.y, prop.rectValue.width, prop.rectValue.height };
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null
                        ? new { instanceId = prop.objectReferenceValue.GetInstanceID(), name = prop.objectReferenceValue.name }
                        : null;
                default: return $"({prop.propertyType})";
            }
        }

        static bool SetSerializedValue(SerializedProperty prop, JToken value, out string error) {
            error = null;
            try {
                switch (prop.propertyType) {
                    case SerializedPropertyType.Integer:
                        prop.intValue = value.Value<int>();
                        return true;
                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.Value<bool>();
                        return true;
                    case SerializedPropertyType.Float:
                        prop.floatValue = value.Value<float>();
                        return true;
                    case SerializedPropertyType.String:
                        prop.stringValue = value.Value<string>();
                        return true;
                    case SerializedPropertyType.Vector2:
                        var v2 = value as JArray;
                        if (v2 != null && v2.Count >= 2) {
                            prop.vector2Value = new Vector2(v2[0].Value<float>(), v2[1].Value<float>());
                            return true;
                        }
                        error = "Vector2 requires [x, y] array.";
                        return false;
                    case SerializedPropertyType.Vector3:
                        var v3 = value as JArray;
                        if (v3 != null && v3.Count >= 3) {
                            prop.vector3Value = new Vector3(v3[0].Value<float>(), v3[1].Value<float>(), v3[2].Value<float>());
                            return true;
                        }
                        error = "Vector3 requires [x, y, z] array.";
                        return false;
                    case SerializedPropertyType.Color:
                        var c = value as JArray;
                        if (c != null && c.Count >= 3) {
                            prop.colorValue = new Color(
                                c[0].Value<float>(),
                                c[1].Value<float>(),
                                c[2].Value<float>(),
                                c.Count >= 4 ? c[3].Value<float>() : 1f
                            );
                            return true;
                        }
                        error = "Color requires [r, g, b] or [r, g, b, a] array.";
                        return false;
                    case SerializedPropertyType.Enum:
                        var enumStr = value.Value<string>();
                        var idx = Array.IndexOf(prop.enumNames, enumStr);
                        if (idx >= 0) {
                            prop.enumValueIndex = idx;
                            return true;
                        }
                        error = $"Invalid enum value. Valid: {string.Join(", ", prop.enumNames)}";
                        return false;
                    case SerializedPropertyType.ObjectReference:
                        // Accept null to clear reference
                        if (value == null || value.Type == JTokenType.Null) {
                            prop.objectReferenceValue = null;
                            return true;
                        }
                        // Accept instanceId as integer
                        if (value.Type == JTokenType.Integer) {
                            var instanceId = value.Value<int>();
                            var obj = EditorUtility.EntityIdToObject(instanceId);
                            if (obj == null) {
                                error = $"No object found with instanceId: {instanceId}";
                                return false;
                            }
                            prop.objectReferenceValue = obj;
                            return true;
                        }
                        // Accept object with instanceId property
                        if (value.Type == JTokenType.Object) {
                            var idToken = value["instanceId"];
                            if (idToken != null && idToken.Type == JTokenType.Integer) {
                                var instanceId = idToken.Value<int>();
                                var obj = EditorUtility.EntityIdToObject(instanceId);
                                if (obj == null) {
                                    error = $"No object found with instanceId: {instanceId}";
                                    return false;
                                }
                                prop.objectReferenceValue = obj;
                                return true;
                            }
                            // Accept object with assetPath property
                            var pathToken = value["assetPath"];
                            if (pathToken != null && pathToken.Type == JTokenType.String) {
                                var assetPath = pathToken.Value<string>();
                                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                                if (obj == null) {
                                    error = $"No asset found at path: {assetPath}";
                                    return false;
                                }
                                prop.objectReferenceValue = obj;
                                return true;
                            }
                        }
                        // Accept string as asset path
                        if (value.Type == JTokenType.String) {
                            var assetPath = value.Value<string>();
                            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                            if (obj == null) {
                                error = $"No asset found at path: {assetPath}";
                                return false;
                            }
                            prop.objectReferenceValue = obj;
                            return true;
                        }
                        error = "ObjectReference requires instanceId (int), assetPath (string), or {instanceId: int} / {assetPath: string}";
                        return false;
                    default:
                        error = $"Cannot set property type: {prop.propertyType}";
                        return false;
                }
            } catch (Exception e) {
                error = e.Message;
                return false;
            }
        }
    }
}
