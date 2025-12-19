using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityMcp {
    public static class Tools_Scene {
        public static ToolResult List(JObject args) {
            var list = new List<object>();

            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var s = SceneManager.GetSceneAt(i);
                list.Add(new {
                    name = s.name,
                    path = s.path,
                    buildIndex = s.buildIndex,
                    isLoaded = s.isLoaded,
                    isDirty = s.isDirty,
                    rootCount = s.isLoaded ? s.rootCount : 0
                });
            }

            return ToolResultUtil.Text(JsonConvert.SerializeObject(list, Formatting.Indented));
        }

        public static ToolResult Save(JObject args) {
            var sceneName = args.Value<string>("scene");

            if (string.IsNullOrEmpty(sceneName)) {
                EditorSceneManager.SaveOpenScenes();
                return ToolResultUtil.Text("All open scenes saved.");
            }

            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var s = SceneManager.GetSceneAt(i);
                if (s.name.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) {
                    EditorSceneManager.SaveScene(s);
                    return ToolResultUtil.Text($"Saved scene: {s.name}");
                }
            }

            return ToolResultUtil.Text($"Scene not found: {sceneName}", true);
        }

        public static ToolResult Load(JObject args) {
            var scenePath = args.Value<string>("path");
            var mode = args.Value<string>("mode")?.ToLowerInvariant() ?? "single";

            if (string.IsNullOrEmpty(scenePath)) {
                return ToolResultUtil.Text("Missing param: path", true);
            }

            var loadMode = mode == "additive" ? OpenSceneMode.Additive : OpenSceneMode.Single;

            try {
                var scene = EditorSceneManager.OpenScene(scenePath, loadMode);
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    name = scene.name,
                    path = scene.path,
                    isLoaded = scene.isLoaded
                }));
            } catch (Exception e) {
                return ToolResultUtil.Text($"Failed to load scene: {e.Message}", true);
            }
        }

        public static ToolResult New(JObject args) {
            var setup = args.Value<string>("setup")?.ToLowerInvariant() ?? "default";

            var newSceneSetup = setup == "empty"
                ? NewSceneSetup.EmptyScene
                : NewSceneSetup.DefaultGameObjects;

            var scene = EditorSceneManager.NewScene(newSceneSetup, NewSceneMode.Single);

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                name = scene.name,
                isLoaded = scene.isLoaded,
                isDirty = scene.isDirty
            }));
        }

        public static ToolResult Close(JObject args) {
            var sceneName = args.Value<string>("scene");
            var save = args.Value<bool?>("save") ?? false;

            if (string.IsNullOrEmpty(sceneName)) {
                return ToolResultUtil.Text("Missing param: scene", true);
            }

            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var s = SceneManager.GetSceneAt(i);
                if (s.name.Equals(sceneName, StringComparison.OrdinalIgnoreCase)) {
                    var removed = EditorSceneManager.CloseScene(s, !save);
                    return removed
                        ? ToolResultUtil.Text($"Closed scene: {sceneName}")
                        : ToolResultUtil.Text($"Failed to close scene: {sceneName}", true);
                }
            }

            return ToolResultUtil.Text($"Scene not found: {sceneName}", true);
        }
    }
}
