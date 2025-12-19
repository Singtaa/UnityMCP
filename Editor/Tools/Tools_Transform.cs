using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityMcp {
    static class Tools_Transform {
        // MARK: Get
        public static ToolResult Get(JObject args) {
            var id = args.Value<string>("target");
            var space = args.Value<string>("space")?.ToLowerInvariant() ?? "world";

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            var t = go.transform;
            object data;

            if (space == "local") {
                data = new {
                    space = "local",
                    position = V3(t.localPosition),
                    rotation = V3(t.localEulerAngles),
                    scale = V3(t.localScale)
                };
            } else {
                data = new {
                    space = "world",
                    position = V3(t.position),
                    rotation = V3(t.eulerAngles),
                    lossyScale = V3(t.lossyScale)
                };
            }

            return ToolResultUtil.Text(JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        // MARK: Set
        public static ToolResult Set(JObject args) {
            var id = args.Value<string>("target");
            var space = args.Value<string>("space")?.ToLowerInvariant() ?? "local";

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            var t = go.transform;
            Undo.RecordObject(t, "[MCP] Transform.Set");

            var pos = args["position"] as JArray;
            var rot = args["rotation"] as JArray;
            var scale = args["scale"] as JArray;

            if (space == "local") {
                if (pos != null) t.localPosition = ToV3(pos);
                if (rot != null) t.localEulerAngles = ToV3(rot);
                if (scale != null) t.localScale = ToV3(scale);
            } else {
                if (pos != null) t.position = ToV3(pos);
                if (rot != null) t.eulerAngles = ToV3(rot);
                // World scale can't be set directly; warn if attempted
                if (scale != null) {
                    return ToolResultUtil.Text("Cannot set world scale directly. Use space=local.", true);
                }
            }

            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text("Transform updated.");
        }

        // MARK: Translate
        public static ToolResult Translate(JObject args) {
            var id = args.Value<string>("target");
            var delta = args["delta"] as JArray;
            var space = args.Value<string>("space")?.ToLowerInvariant() ?? "self";

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);
            if (delta == null || delta.Count < 3) return ToolResultUtil.Text("Missing param: delta (requires [x,y,z])", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            var t = go.transform;
            Undo.RecordObject(t, "[MCP] Transform.Translate");

            var d = ToV3(delta);
            var s = space == "world" ? Space.World : Space.Self;
            t.Translate(d, s);

            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                position = V3(t.position),
                localPosition = V3(t.localPosition)
            }));
        }

        // MARK: Rotate
        public static ToolResult Rotate(JObject args) {
            var id = args.Value<string>("target");
            var euler = args["euler"] as JArray;
            var space = args.Value<string>("space")?.ToLowerInvariant() ?? "self";

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);
            if (euler == null || euler.Count < 3) return ToolResultUtil.Text("Missing param: euler (requires [x,y,z])", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            var t = go.transform;
            Undo.RecordObject(t, "[MCP] Transform.Rotate");

            var e = ToV3(euler);
            var s = space == "world" ? Space.World : Space.Self;
            t.Rotate(e, s);

            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                rotation = V3(t.eulerAngles),
                localRotation = V3(t.localEulerAngles)
            }));
        }

        // MARK: LookAt
        public static ToolResult LookAt(JObject args) {
            var id = args.Value<string>("target");
            var point = args["point"] as JArray;
            var targetId = args.Value<string>("lookAtTarget");

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            Vector3 lookPoint;

            if (point != null && point.Count >= 3) {
                lookPoint = ToV3(point);
            } else if (!string.IsNullOrEmpty(targetId)) {
                if (!GameObjectResolver.TryResolve(targetId, out var lookGo, out var lookErr)) {
                    return ToolResultUtil.Text(lookErr, true);
                }
                lookPoint = lookGo.transform.position;
            } else {
                return ToolResultUtil.Text("Missing param: point [x,y,z] or lookAtTarget", true);
            }

            var t = go.transform;
            Undo.RecordObject(t, "[MCP] Transform.LookAt");
            t.LookAt(lookPoint);

            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                rotation = V3(t.eulerAngles)
            }));
        }

        // MARK: Reset
        public static ToolResult Reset(JObject args) {
            var id = args.Value<string>("target");
            var resetPosition = args.Value<bool?>("position") ?? true;
            var resetRotation = args.Value<bool?>("rotation") ?? true;
            var resetScale = args.Value<bool?>("scale") ?? true;

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            var t = go.transform;
            Undo.RecordObject(t, "[MCP] Transform.Reset");

            if (resetPosition) t.localPosition = Vector3.zero;
            if (resetRotation) t.localRotation = Quaternion.identity;
            if (resetScale) t.localScale = Vector3.one;

            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text("Transform reset.");
        }

        // MARK: Helpers
        static float[] V3(Vector3 v) => new[] { v.x, v.y, v.z };

        static Vector3 ToV3(JArray arr) {
            if (arr == null || arr.Count < 3) return Vector3.zero;
            return new Vector3(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>());
        }
    }
}
