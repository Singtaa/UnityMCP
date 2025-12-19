using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp {
    static class Tools_Editor {
        public static ToolResult ExecuteMenuItem(JObject args) {
            var menuPath = args.Value<string>("menuPath");

            if (string.IsNullOrEmpty(menuPath)) {
                return ToolResultUtil.Text("Missing param: menuPath", true);
            }

            var ok = EditorApplication.ExecuteMenuItem(menuPath);

            return ok
                ? ToolResultUtil.Text($"Executed: {menuPath}")
                : ToolResultUtil.Text($"Menu item not found or failed: {menuPath}", true);
        }

        public static ToolResult Notification(JObject args) {
            var message = args.Value<string>("message") ?? "";
            var duration = args.Value<float?>("duration") ?? 2f;

            if (SceneView.lastActiveSceneView != null) {
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent(message), duration);
                return ToolResultUtil.Text("Notification shown.");
            }

            return ToolResultUtil.Text("No active SceneView to show notification.", true);
        }

        public static ToolResult Log(JObject args) {
            var message = args.Value<string>("message") ?? "";
            var type = args.Value<string>("type")?.ToLowerInvariant() ?? "log";

            switch (type) {
                case "warning":
                    Debug.LogWarning($"[MCP] {message}");
                    break;
                case "error":
                    Debug.LogError($"[MCP] {message}");
                    break;
                default:
                    Debug.Log($"[MCP] {message}");
                    break;
            }

            return ToolResultUtil.Text("Logged.");
        }

        public static ToolResult GetEditorState(JObject args) {
            var state = new {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                applicationPath = EditorApplication.applicationPath,
                applicationContentsPath = EditorApplication.applicationContentsPath,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                systemLanguage = Application.systemLanguage.ToString(),
                timeSinceStartup = EditorApplication.timeSinceStartup
            };

            return ToolResultUtil.Text(JsonConvert.SerializeObject(state, Formatting.Indented));
        }

        public static ToolResult Pause(JObject args) {
            var pause = args.Value<bool?>("pause") ?? !EditorApplication.isPaused;
            EditorApplication.isPaused = pause;
            return ToolResultUtil.Text($"Editor paused: {pause}");
        }

        public static ToolResult Step(JObject args) {
            if (!EditorApplication.isPlaying) {
                return ToolResultUtil.Text("Cannot step when not in play mode.", true);
            }
            EditorApplication.Step();
            return ToolResultUtil.Text("Stepped one frame.");
        }
    }
}
