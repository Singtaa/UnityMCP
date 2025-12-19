using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp {
    /// <summary>
    /// Registry and dispatcher for MCP resources.
    /// Resources provide read-only access to Unity state like console logs, hierarchy, etc.
    /// </summary>
    public static class ResourceRegistry {
        public delegate ResourceResult ResourceHandler(string uri, JObject args);

        static bool _init;
        static Dictionary<string, ResourceHandler> _handlers;

        public static int Count => _handlers?.Count ?? 0;

        public static void EnsureInitialized() {
            if (_init) return;
            _init = true;

            _handlers = new Dictionary<string, ResourceHandler>(StringComparer.Ordinal) {
                // Console logs
                ["unity.resource.console.logs"] = ReadConsoleLogs,

                // Scene hierarchy
                ["unity.resource.hierarchy"] = ReadHierarchy,

                // Test results
                ["unity.resource.tests.results"] = ReadTestResults,

                // Project files
                ["unity.resource.project.files"] = ReadProjectFiles,
            };
        }

        public static void HandleResourceCall(McpTcpClient client, string id, string tool, JObject args) {
            EnsureInitialized();

            ResourceResult result;
            try {
                var uri = args?.Value<string>("uri") ?? "";

                if (string.IsNullOrEmpty(tool) || !_handlers.TryGetValue(tool, out var handler) ||
                    handler == null) {
                    result = ResourceResultUtil.Error(uri, $"Unknown resource: {tool}");
                } else {
                    result = handler(uri, args ?? new JObject()) ?? ResourceResultUtil.Error(uri, "Null resource result");
                }
            } catch (Exception e) {
                Debug.LogException(e);
                result = ResourceResultUtil.Error("", $"Resource read failed: {e.GetType().Name}: {e.Message}");
            }

            try {
                client.SendResourceResponse(id, result);
            } catch (Exception e) {
                Debug.LogWarning($"[UnityMcp] failed sending resource response: {e.Message}");
            }
        }

        public static IEnumerable<string> GetResourceNames() {
            EnsureInitialized();
            return _handlers.Keys;
        }

        // MARK: Resource Handlers

        static ResourceResult ReadConsoleLogs(string uri, JObject args) {
            var maxEntries = args?.Value<int?>("maxEntries") ?? 500;
            maxEntries = Mathf.Clamp(maxEntries, 1, 5000);

            string logsText;
            if (ConsoleCapture.TryReadUnityConsole(maxEntries, out var text)) {
                logsText = text;
            } else {
                logsText = ConsoleCapture.GetFallbackText(maxEntries);
            }

            return ResourceResultUtil.Text("unity://console/logs", logsText);
        }

        static ResourceResult ReadHierarchy(string uri, JObject args) {
            var sceneName = args?.Value<string>("scene");
            var list = new List<object>(4096);

            for (var si = 0; si < SceneManager.sceneCount; si++) {
                var scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded) continue;

                // Filter by scene name if provided
                if (!string.IsNullOrEmpty(sceneName) &&
                    !scene.name.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                var roots = scene.GetRootGameObjects();
                foreach (var go in roots) {
                    TraverseHierarchy(go.transform, scene.name, list);
                }
            }

            var resultUri = string.IsNullOrEmpty(sceneName)
                ? "unity://hierarchy"
                : $"unity://hierarchy/{sceneName}";

            return ResourceResultUtil.Json(resultUri, JsonConvert.SerializeObject(new {
                sceneCount = SceneManager.sceneCount,
                objectCount = list.Count,
                hierarchy = list
            }, Formatting.Indented));
        }

        static void TraverseHierarchy(Transform t, string sceneName, List<object> list) {
            if (t == null) return;

            list.Add(new {
                scene = sceneName,
                path = GameObjectResolver.GetPath(t.gameObject),
                name = t.name,
                activeSelf = t.gameObject.activeSelf,
                activeInHierarchy = t.gameObject.activeInHierarchy,
                instanceId = t.gameObject.GetInstanceID(),
                childCount = t.childCount
            });

            for (var i = 0; i < t.childCount; i++) {
                TraverseHierarchy(t.GetChild(i), sceneName, list);
            }
        }

        static ResourceResult ReadTestResults(string uri, JObject args) {
            // Get test results from Tools_Test
            var result = Tools_Test.GetResults(args);

            return new ResourceResult {
                contents = new[] {
                    new ResourceContent {
                        uri = "unity://tests/results",
                        mimeType = "application/json",
                        text = result.content[0].text
                    }
                },
                isError = result.isError
            };
        }

        static ResourceResult ReadProjectFiles(string uri, JObject args) {
            var path = args?.Value<string>("path");
            var ignore = GitIgnoreCache.Get();

            if (!string.IsNullOrEmpty(path)) {
                // Read specific file
                if (!ProjectPaths.TryResolveAllowedPath(path, isDirectory: false, ignore, out var fullPath,
                        out var error)) {
                    return ResourceResultUtil.Error($"unity://project/files/{path}", error);
                }

                try {
                    var bytes = System.IO.File.ReadAllBytes(fullPath);
                    if (bytes.Length > 1024 * 1024) {
                        return ResourceResultUtil.Error($"unity://project/files/{path}", "File too large (>1MB)");
                    }
                    var text = System.Text.Encoding.UTF8.GetString(bytes);
                    return ResourceResultUtil.Text($"unity://project/files/{path}", text);
                } catch (Exception e) {
                    return ResourceResultUtil.Error($"unity://project/files/{path}", e.Message);
                }
            }

            // List all files
            var entries = new List<object>(4096);
            foreach (var e in ProjectPaths.EnumerateProjectEntries(ignore)) {
                entries.Add(new { path = e.path, kind = e.kind });
            }

            return ResourceResultUtil.Json("unity://project/files", JsonConvert.SerializeObject(new {
                projectRoot = ProjectPaths.ProjectRoot,
                fileCount = entries.Count,
                files = entries
            }, Formatting.Indented));
        }
    }
}
