using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp {
    /// <summary>
    /// Registry and dispatcher for MCP tools.
    /// Maps tool names to handlers and executes tool calls from the TCP client.
    /// </summary>
    public static class ToolRegistry {
        // MARK: Types
        public delegate ToolResult Handler(JObject args);

        sealed class HierarchyEntry {
            public string scene;
            public string path;
            public bool activeSelf;
            public bool activeInHierarchy;
            public int instanceId;
        }

        sealed class ProjectEntry {
            public string path;
            public string kind;
        }

        // MARK: State
        static bool _init;
        static Dictionary<string, Handler> _handlers;

        public static int Count => _handlers?.Count ?? 0;

        public static void EnsureInitialized() {
            if (_init) return;
            _init = true;

            _handlers = new Dictionary<string, Handler>(StringComparer.Ordinal) {
                // Bridge diagnostics
                ["unity.bridge.ping"] = Ping,

                // Playmode
                ["unity.playmode.enter"] = EnterPlayMode,
                ["unity.playmode.exit"] = ExitPlayMode,

                // Console
                ["unity.console.logs"] = ConsoleLogs,

                // Hierarchy (legacy)
                ["unity.hierarchy.list"] = HierarchyList,

                // Project files
                ["unity.project.listFiles"] = ProjectListFiles,
                ["unity.project.readText"] = ProjectReadText,
                ["unity.project.writeText"] = ProjectWriteText,
                ["unity.project.deleteFile"] = ProjectDeleteFile,

                // Scripts
                ["unity.scripts.status"] = ScriptsStatus,
                ["unity.scripts.recompile"] = ScriptsRecompile,

                // Assets
                ["unity.assets.refresh"] = AssetsRefresh,
                ["unity.assets.import"] = AssetsImport,

                // Scene
                ["unity.scene.list"] = Tools_Scene.List,
                ["unity.scene.save"] = Tools_Scene.Save,
                ["unity.scene.load"] = Tools_Scene.Load,
                ["unity.scene.new"] = Tools_Scene.New,
                ["unity.scene.close"] = Tools_Scene.Close,

                // GameObject
                ["unity.gameobject.create"] = Tools_GameObject.Create,
                ["unity.gameobject.delete"] = Tools_GameObject.Delete,
                ["unity.gameobject.find"] = Tools_GameObject.Find,
                ["unity.gameobject.setActive"] = Tools_GameObject.SetActive,
                ["unity.gameobject.setParent"] = Tools_GameObject.SetParent,
                ["unity.gameobject.rename"] = Tools_GameObject.Rename,
                ["unity.gameobject.duplicate"] = Tools_GameObject.Duplicate,

                // Component
                ["unity.component.list"] = Tools_Component.List,
                ["unity.component.add"] = Tools_Component.Add,
                ["unity.component.remove"] = Tools_Component.Remove,
                ["unity.component.setEnabled"] = Tools_Component.SetEnabled,
                ["unity.component.getProperties"] = Tools_Component.GetProperties,
                ["unity.component.setProperty"] = Tools_Component.SetProperty,

                // Transform
                ["unity.transform.get"] = Tools_Transform.Get,
                ["unity.transform.set"] = Tools_Transform.Set,
                ["unity.transform.translate"] = Tools_Transform.Translate,
                ["unity.transform.rotate"] = Tools_Transform.Rotate,
                ["unity.transform.lookAt"] = Tools_Transform.LookAt,
                ["unity.transform.reset"] = Tools_Transform.Reset,

                // Selection
                ["unity.selection.get"] = Tools_Selection.Get,
                ["unity.selection.set"] = Tools_Selection.Set,
                ["unity.selection.focus"] = Tools_Selection.Focus,

                // Editor
                ["unity.editor.executeMenuItem"] = Tools_Editor.ExecuteMenuItem,
                ["unity.editor.notification"] = Tools_Editor.Notification,
                ["unity.editor.log"] = Tools_Editor.Log,
                ["unity.editor.getState"] = Tools_Editor.GetEditorState,
                ["unity.editor.pause"] = Tools_Editor.Pause,
                ["unity.editor.step"] = Tools_Editor.Step,

                // Undo
                ["unity.undo.perform"] = Tools_Undo.PerformUndo,
                ["unity.redo.perform"] = Tools_Undo.PerformRedo,
                ["unity.undo.getCurrentGroup"] = Tools_Undo.GetCurrentGroup,
                ["unity.undo.collapse"] = Tools_Undo.CollapseUndoOperations,
                ["unity.undo.setGroupName"] = Tools_Undo.SetCurrentGroupName,
                ["unity.undo.clearAll"] = Tools_Undo.ClearAll,

                // Test
                ["unity.test.list"] = Tools_Test.ListTests,
                ["unity.test.run"] = Tools_Test.RunTests,
                ["unity.test.runSync"] = Tools_Test.RunTestsSync,
                ["unity.test.getResults"] = Tools_Test.GetResults,
            };
        }

        public static void HandleBridgeCall(McpTcpClient client, string id, string tool, JObject args) {
            EnsureInitialized();

            ToolResult result;
            try {
                if (string.IsNullOrEmpty(tool) || !_handlers.TryGetValue(tool, out var handler) ||
                    handler == null) {
                    result = ToolResultUtil.Text($"Unknown tool: {tool}", true);
                } else {
                    result = handler(args ?? new JObject()) ?? ToolResultUtil.Text("Null tool result", true);
                }
            } catch (Exception e) {
                Debug.LogException(e);
                result = ToolResultUtil.Text($"Tool threw: {e.GetType().Name}: {e.Message}", true);
            }

            try {
                client.SendResponse(id, result);
            } catch (Exception e) {
                Debug.LogWarning($"[UnityMcp] failed sending response: {e.Message}");
            }
        }

        public static IEnumerable<string> GetToolNames() {
            EnsureInitialized();
            return _handlers.Keys;
        }

        // MARK: Args Helpers
        static string GetString(JObject args, string name, string def = "") {
            if (args == null) return def;
            var tok = args[name];
            return tok != null && tok.Type == JTokenType.String ? (string)tok : def;
        }

        static bool GetBool(JObject args, string name, bool def = false) {
            if (args == null) return def;
            var tok = args[name];
            if (tok == null) return def;
            if (tok.Type == JTokenType.Boolean) return (bool)tok;
            return def;
        }

        static int GetInt(JObject args, string name, int def) {
            if (args == null) return def;
            var tok = args[name];
            if (tok == null) return def;
            if (tok.Type == JTokenType.Integer) return (int)tok;
            if (tok.Type == JTokenType.Float) return (int)(float)tok;
            return def;
        }

        // MARK: Built-in Tools
        static ToolResult Ping(JObject args) {
            return ToolResultUtil.Text("pong");
        }

        static ToolResult EnterPlayMode(JObject args) {
            if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
            return ToolResultUtil.Text("Requested enter playmode.");
        }

        static ToolResult ExitPlayMode(JObject args) {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            return ToolResultUtil.Text("Requested exit playmode.");
        }

        static ToolResult ConsoleLogs(JObject args) {
            var maxEntries = GetInt(args, "maxEntries", 500);
            maxEntries = Mathf.Clamp(maxEntries, 1, 5000);

            if (ConsoleCapture.TryReadUnityConsole(maxEntries, out var text)) {
                return ToolResultUtil.Text(text);
            }

            var fallback = ConsoleCapture.GetFallbackText(maxEntries);
            return ToolResultUtil.Text(fallback);
        }

        static ToolResult HierarchyList(JObject args) {
            var list = new List<HierarchyEntry>(4096);

            for (var si = 0; si < SceneManager.sceneCount; si++) {
                var scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                foreach (var go in roots) {
                    Traverse(go.transform, scene.name, list);
                }
            }

            var json = JsonConvert.SerializeObject(list, Formatting.Indented);
            return ToolResultUtil.Text(json);
        }

        static void Traverse(Transform t, string sceneName, List<HierarchyEntry> list) {
            if (t == null) return;

            list.Add(new HierarchyEntry {
                scene = sceneName,
                path = GetTransformPath(t),
                activeSelf = t.gameObject.activeSelf,
                activeInHierarchy = t.gameObject.activeInHierarchy,
                instanceId = t.gameObject.GetInstanceID()
            });

            for (var i = 0; i < t.childCount; i++) {
                Traverse(t.GetChild(i), sceneName, list);
            }
        }

        static string GetTransformPath(Transform t) {
            var parts = new Stack<string>();
            var cur = t;
            while (cur != null) {
                parts.Push(cur.name);
                cur = cur.parent;
            }
            return string.Join("/", parts);
        }

        static ToolResult ProjectListFiles(JObject args) {
            var ignore = GitIgnoreCache.Get();
            var entries = new List<ProjectEntry>(4096);

            foreach (var e in ProjectPaths.EnumerateProjectEntries(ignore)) {
                entries.Add(new ProjectEntry { path = e.path, kind = e.kind });
            }

            var json = JsonConvert.SerializeObject(entries, Formatting.Indented);
            return ToolResultUtil.Text(json);
        }

        static ToolResult ProjectReadText(JObject args) {
            var rel = GetString(args, "path", null);
            if (string.IsNullOrEmpty(rel)) return ToolResultUtil.Text("Missing param: path", true);

            var ignore = GitIgnoreCache.Get();

            if (!ProjectPaths.TryResolveAllowedPath(rel, isDirectory: false, ignore, out var fullPath,
                    out var error)) {
                return ToolResultUtil.Text(error, true);
            }

            try {
                var bytes = System.IO.File.ReadAllBytes(fullPath);
                if (bytes.Length > 1024 * 1024)
                    return ToolResultUtil.Text("File too large (>1MB) for readText v1.", true);
                var text = System.Text.Encoding.UTF8.GetString(bytes);
                return ToolResultUtil.Text(text);
            } catch (Exception e) {
                return ToolResultUtil.Text($"Read failed: {e.Message}", true);
            }
        }

        static ToolResult ProjectWriteText(JObject args) {
            var rel = GetString(args, "path", null);
            var text = GetString(args, "text", null);
            var createDirs = GetBool(args, "createDirs", false);

            if (string.IsNullOrEmpty(rel)) return ToolResultUtil.Text("Missing param: path", true);
            if (text == null) return ToolResultUtil.Text("Missing param: text", true);

            var ignore = GitIgnoreCache.Get();

            if (!ProjectPaths.TryResolveAllowedPath(rel, isDirectory: false, ignore, out var fullPath,
                    out var error)) {
                return ToolResultUtil.Text(error, true);
            }

            try {
                if (createDirs) {
                    var dir = System.IO.Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) {
                        System.IO.Directory.CreateDirectory(dir);
                    }
                }

                System.IO.File.WriteAllText(fullPath, text, new System.Text.UTF8Encoding(false));

                EditorApplication.delayCall += () => { ProjectPaths.ScheduleRefreshAndCompilation(rel); };

                return ToolResultUtil.Text("Wrote file. Refresh/compilation scheduled (fire-and-forget).");
            } catch (Exception e) {
                return ToolResultUtil.Text($"Write failed: {e.Message}", true);
            }
        }

        static ToolResult ProjectDeleteFile(JObject args) {
            var rel = GetString(args, "path", null);
            if (string.IsNullOrEmpty(rel)) return ToolResultUtil.Text("Missing param: path", true);

            var ignore = GitIgnoreCache.Get();

            if (!ProjectPaths.TryResolveAllowedPath(rel, isDirectory: false, ignore, out var fullPath,
                    out var error)) {
                return ToolResultUtil.Text(error, true);
            }

            try {
                if (!System.IO.File.Exists(fullPath))
                    return ToolResultUtil.Text("File does not exist.", true);

                if (ProjectPaths.IsUnderAssets(rel)) {
                    var assetPath = ProjectPaths.ToUnityAssetPath(rel);
                    var ok = AssetDatabase.DeleteAsset(assetPath);
                    if (!ok) return ToolResultUtil.Text("AssetDatabase.DeleteAsset failed.", true);
                } else {
                    System.IO.File.Delete(fullPath);
                    EditorApplication.delayCall += () => AssetDatabase.Refresh();
                }

                return ToolResultUtil.Text("Deleted file. Refresh scheduled.");
            } catch (Exception e) {
                return ToolResultUtil.Text($"Delete failed: {e.Message}", true);
            }
        }

        static ToolResult ScriptsStatus(JObject args) {
            var obj = new {
                isCompiling = EditorApplication.isCompiling,
                isPlaying = EditorApplication.isPlaying,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                isUpdating = EditorApplication.isUpdating
            };

            var json = JsonConvert.SerializeObject(obj, Formatting.Indented);
            return ToolResultUtil.Text(json);
        }

        static ToolResult ScriptsRecompile(JObject args) {
            if (EditorApplication.isCompiling) {
                return ToolResultUtil.Text("Already compiling. Please wait for current compilation to finish.");
            }

            if (EditorApplication.isPlaying) {
                return ToolResultUtil.Text("Cannot request recompilation while in Play Mode.", true);
            }

            CompilationPipeline.RequestScriptCompilation();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var result = new {
                message = "Script recompilation requested. Domain reload will occur when compilation completes.",
                note = "The MCP bridge will temporarily disconnect during domain reload and automatically reconnect afterward."
            };

            return ToolResultUtil.Text(JsonConvert.SerializeObject(result, Formatting.Indented));
        }

        static ToolResult AssetsRefresh(JObject args) {
            var importOptions = ImportAssetOptions.ForceSynchronousImport;

            AssetDatabase.Refresh(importOptions);

            var result = new {
                message = "Asset database refresh triggered.",
                isCompiling = EditorApplication.isCompiling
            };

            return ToolResultUtil.Text(JsonConvert.SerializeObject(result, Formatting.Indented));
        }

        static ToolResult AssetsImport(JObject args) {
            var pathsToken = args?["paths"];
            if (pathsToken == null || pathsToken.Type != JTokenType.Array) {
                return ToolResultUtil.Text("Missing or invalid param: paths (must be an array of asset paths)", true);
            }

            var forceUpdate = GetBool(args, "forceUpdate", false);
            var importOptions = forceUpdate ? ImportAssetOptions.ForceUpdate : ImportAssetOptions.Default;

            var paths = pathsToken.ToObject<string[]>() ?? Array.Empty<string>();
            if (paths.Length == 0) {
                return ToolResultUtil.Text("No paths provided", true);
            }

            var results = new List<object>();
            var successCount = 0;
            var failCount = 0;

            foreach (var path in paths) {
                if (string.IsNullOrEmpty(path)) {
                    results.Add(new { path = (string)null, success = false, error = "Empty path" });
                    failCount++;
                    continue;
                }

                var assetPath = path.Replace("\\", "/");
                if (!assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Assets\\")) {
                    assetPath = "Assets/" + assetPath;
                }

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid)) {
                    results.Add(new { path = assetPath, success = false, error = "Asset not found at path" });
                    failCount++;
                    continue;
                }

                try {
                    AssetDatabase.ImportAsset(assetPath, importOptions);
                    results.Add(new { path = assetPath, success = true, error = (string)null });
                    successCount++;
                } catch (Exception e) {
                    results.Add(new { path = assetPath, success = false, error = e.Message });
                    failCount++;
                }
            }

            var response = new {
                message = $"Imported {successCount} asset(s), {failCount} failed",
                forceUpdate = forceUpdate,
                successCount = successCount,
                failCount = failCount,
                results = results
            };

            return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
        }
    }
}
