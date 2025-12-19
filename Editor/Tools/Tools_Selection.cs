using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp {
    static class Tools_Selection {
        public static ToolResult Get(JObject args) {
            var selected = Selection.gameObjects;
            var list = new List<object>();

            foreach (var go in selected) {
                if (go == null) continue;
                list.Add(new {
                    instanceId = go.GetInstanceID(),
                    name = go.name,
                    path = GameObjectResolver.GetQualifiedPath(go)
                });
            }

            var result = new {
                count = list.Count,
                activeObject = Selection.activeGameObject != null
                    ? new {
                        instanceId = Selection.activeGameObject.GetInstanceID(),
                        name = Selection.activeGameObject.name,
                        path = GameObjectResolver.GetQualifiedPath(Selection.activeGameObject)
                    }
                    : null,
                objects = list
            };

            return ToolResultUtil.Text(JsonConvert.SerializeObject(result, Formatting.Indented));
        }

        public static ToolResult Set(JObject args) {
            var targets = args["targets"] as JArray;

            if (targets == null || targets.Count == 0) {
                Selection.activeGameObject = null;
                return ToolResultUtil.Text("Selection cleared.");
            }

            var gos = new List<GameObject>();
            var errors = new List<string>();

            foreach (var tok in targets) {
                var id = tok.Value<string>();
                if (GameObjectResolver.TryResolve(id, out var go, out var err)) {
                    gos.Add(go);
                } else {
                    errors.Add($"{id}: {err}");
                }
            }

            Selection.objects = gos.ToArray();

            if (errors.Count > 0) {
                return ToolResultUtil.Text($"Selected {gos.Count} object(s). Errors: {string.Join("; ", errors)}");
            }

            return ToolResultUtil.Text($"Selected {gos.Count} object(s).");
        }

        public static ToolResult Focus(JObject args) {
            var id = args.Value<string>("target");

            if (string.IsNullOrEmpty(id)) {
                // Focus on current selection
                if (Selection.activeGameObject != null) {
                    SceneView.lastActiveSceneView?.FrameSelected();
                    return ToolResultUtil.Text("Focused on current selection.");
                }
                return ToolResultUtil.Text("No selection to focus on.", true);
            }

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();

            return ToolResultUtil.Text($"Focused on: {GameObjectResolver.GetQualifiedPath(go)}");
        }
    }
}
