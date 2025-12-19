using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp {
    public static class Tools_GameObject {
        // MARK: Create
        public static ToolResult Create(JObject args) {
            var name = args.Value<string>("name") ?? "GameObject";
            var parentId = args.Value<string>("parent");
            var primitive = args.Value<string>("primitive");

            GameObject go;

            if (!string.IsNullOrEmpty(primitive) && Enum.TryParse<PrimitiveType>(primitive, true, out var pt)) {
                go = GameObject.CreatePrimitive(pt);
                go.name = name;
            } else {
                go = new GameObject(name);
            }

            Undo.RegisterCreatedObjectUndo(go, "[MCP] Create GameObject");

            if (!string.IsNullOrEmpty(parentId)) {
                if (GameObjectResolver.TryResolve(parentId, out var parent, out _)) {
                    Undo.SetTransformParent(go.transform, parent.transform, "[MCP] Set Parent");
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                }
            }

            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                instanceId = go.GetInstanceID(),
                path = GameObjectResolver.GetQualifiedPath(go)
            }));
        }

        // MARK: Delete
        public static ToolResult Delete(JObject args) {
            var id = args.Value<string>("target");
            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            var path = GameObjectResolver.GetQualifiedPath(go);
            var scene = go.scene;

            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);

            return ToolResultUtil.Text($"Deleted: {path}");
        }

        // MARK: Find
        public static ToolResult Find(JObject args) {
            var nameQuery = args.Value<string>("name");
            var tag = args.Value<string>("tag");
            var path = args.Value<string>("path");
            var maxResults = args.Value<int?>("maxResults") ?? 100;

            var results = new List<object>();

            // Direct path lookup
            if (!string.IsNullOrEmpty(path)) {
                if (GameObjectResolver.TryResolve(path, out var go, out _)) {
                    results.Add(MakeGoInfo(go));
                }
                return ToolResultUtil.Text(JsonConvert.SerializeObject(results, Formatting.Indented));
            }

            // Search all loaded scenes
            for (int i = 0; i < SceneManager.sceneCount && results.Count < maxResults; i++) {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects()) {
                    SearchRecursive(root.transform, nameQuery, tag, results, maxResults);
                }
            }

            return ToolResultUtil.Text(JsonConvert.SerializeObject(results, Formatting.Indented));
        }

        static void SearchRecursive(Transform t, string nameQuery, string tag, List<object> results, int max) {
            if (results.Count >= max) return;

            var go = t.gameObject;
            bool match = true;

            if (!string.IsNullOrEmpty(nameQuery)) {
                match = go.name.IndexOf(nameQuery, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (match && !string.IsNullOrEmpty(tag)) {
                match = go.CompareTag(tag);
            }

            if (match && (!string.IsNullOrEmpty(nameQuery) || !string.IsNullOrEmpty(tag))) {
                results.Add(MakeGoInfo(go));
            }

            for (int i = 0; i < t.childCount; i++) {
                SearchRecursive(t.GetChild(i), nameQuery, tag, results, max);
            }
        }

        static object MakeGoInfo(GameObject go) {
            return new {
                instanceId = go.GetInstanceID(),
                name = go.name,
                path = GameObjectResolver.GetQualifiedPath(go),
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,
                tag = go.tag,
                layer = go.layer
            };
        }

        // MARK: SetActive
        public static ToolResult SetActive(JObject args) {
            var id = args.Value<string>("target");
            var active = args.Value<bool?>("active") ?? true;

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            Undo.RecordObject(go, "[MCP] SetActive");
            go.SetActive(active);
            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text($"Set active={active}: {GameObjectResolver.GetQualifiedPath(go)}");
        }

        // MARK: SetParent
        public static ToolResult SetParent(JObject args) {
            var id = args.Value<string>("target");
            var parentId = args.Value<string>("parent");
            var worldPositionStays = args.Value<bool?>("worldPositionStays") ?? true;

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            Transform newParent = null;
            if (!string.IsNullOrEmpty(parentId)) {
                if (!GameObjectResolver.TryResolve(parentId, out var parentGo, out var parentErr)) {
                    return ToolResultUtil.Text(parentErr, true);
                }
                newParent = parentGo.transform;
            }

            Undo.SetTransformParent(go.transform, newParent, "[MCP] SetParent");
            go.transform.SetParent(newParent, worldPositionStays);
            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text($"Reparented: {GameObjectResolver.GetQualifiedPath(go)}");
        }

        // MARK: Rename
        public static ToolResult Rename(JObject args) {
            var id = args.Value<string>("target");
            var newName = args.Value<string>("name");

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);
            if (string.IsNullOrEmpty(newName)) return ToolResultUtil.Text("Missing param: name", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            Undo.RecordObject(go, "[MCP] Rename");
            go.name = newName;
            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                instanceId = go.GetInstanceID(),
                path = GameObjectResolver.GetQualifiedPath(go)
            }));
        }

        // MARK: Duplicate
        public static ToolResult Duplicate(JObject args) {
            var id = args.Value<string>("target");

            if (string.IsNullOrEmpty(id)) return ToolResultUtil.Text("Missing param: target", true);

            if (!GameObjectResolver.TryResolve(id, out var go, out var err)) {
                return ToolResultUtil.Text(err, true);
            }

            var clone = UnityEngine.Object.Instantiate(go, go.transform.parent);
            clone.name = go.name; // Remove "(Clone)" suffix
            Undo.RegisterCreatedObjectUndo(clone, "[MCP] Duplicate");
            EditorSceneManager.MarkSceneDirty(clone.scene);

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                instanceId = clone.GetInstanceID(),
                path = GameObjectResolver.GetQualifiedPath(clone)
            }));
        }
    }
}
