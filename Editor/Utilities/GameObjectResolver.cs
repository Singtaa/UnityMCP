using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp {
    /// <summary>
    /// Resolves GameObjects from various identifier formats.
    /// </summary>
    public static class GameObjectResolver {
        // MARK: Resolve
        /// <summary>
        /// Resolves a GameObject from various identifier formats:
        /// - "#12345" = instanceId
        /// - "SceneName:/Root/Child" = scene-qualified path
        /// - "Root/Child" = path in any loaded scene
        /// </summary>
        public static bool TryResolve(string identifier, out GameObject go, out string error) {
            go = null;
            error = null;

            if (string.IsNullOrEmpty(identifier)) {
                error = "Empty identifier.";
                return false;
            }

            // Try instanceId first (format: "#12345")
            if (identifier.StartsWith("#", StringComparison.Ordinal)) {
                var idStr = identifier.Substring(1);
                if (int.TryParse(idStr, out var id)) {
                    go = EditorUtility.EntityIdToObject(id) as GameObject;
                    if (go != null) return true;
                    error = $"No GameObject with instanceId {id}.";
                    return false;
                }
                error = $"Invalid instanceId format: {identifier}";
                return false;
            }

            // Try scene-qualified path: "SceneName:/Root/Child"
            var colonIdx = identifier.IndexOf(":/", StringComparison.Ordinal);
            if (colonIdx > 0) {
                var sceneName = identifier.Substring(0, colonIdx);
                var path = identifier.Substring(colonIdx + 2);
                return TryFindByScenePath(sceneName, path, out go, out error);
            }

            // Try path in any loaded scene
            return TryFindByPath(identifier, out go, out error);
        }

        static bool TryFindByScenePath(string sceneName, string path, out GameObject go, out string error) {
            go = null;
            error = null;

            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                if (!scene.name.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var root in scene.GetRootGameObjects()) {
                    if (TryFindInHierarchy(root.transform, path, out go)) return true;
                }
            }

            error = $"GameObject not found: {sceneName}:/{path}";
            return false;
        }

        static bool TryFindByPath(string path, out GameObject go, out string error) {
            go = null;
            error = null;

            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects()) {
                    if (TryFindInHierarchy(root.transform, path, out go)) return true;
                }
            }

            error = $"GameObject not found: {path}";
            return false;
        }

        static bool TryFindInHierarchy(Transform root, string path, out GameObject go) {
            go = null;

            var parts = path.Split('/');
            if (parts.Length == 0) return false;

            if (!root.name.Equals(parts[0], StringComparison.Ordinal)) return false;

            var current = root;
            for (int i = 1; i < parts.Length; i++) {
                var child = current.Find(parts[i]);
                if (child == null) return false;
                current = child;
            }

            go = current.gameObject;
            return true;
        }

        // MARK: Path
        public static string GetPath(GameObject go) {
            if (go == null) return "";
            var parts = new Stack<string>();
            var t = go.transform;
            while (t != null) {
                parts.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        public static string GetQualifiedPath(GameObject go) {
            if (go == null) return "";
            return $"{go.scene.name}:/{GetPath(go)}";
        }
    }
}
