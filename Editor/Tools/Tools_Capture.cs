using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityMcp {
    public static class Tools_Capture {
        const int MIN_DIM = 32;
        const int MAX_DIM = 8192;

        static MethodInfo _updatePanels;
        static MethodInfo _renderOffscreenPanels;
        static MethodInfo _renderPanel;
        static PropertyInfo _panelSettingsPanelProp;

        static bool ResolveRuntimeUtilityMethods() {
            if (_updatePanels != null && _renderOffscreenPanels != null) return true;
            var asm = typeof(PanelSettings).Assembly;
            var t = asm.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            if (t == null) return false;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            _updatePanels ??= t.GetMethod("UpdatePanels", flags, null, Type.EmptyTypes, null);
            _renderOffscreenPanels ??= t.GetMethod("RenderOffscreenPanels", flags, null, Type.EmptyTypes, null);

            var baseRtPanel = asm.GetType("UnityEngine.UIElements.BaseRuntimePanel");
            if (baseRtPanel != null) {
                _renderPanel = t.GetMethod("RenderPanel",
                    flags, null, new[] { baseRtPanel, typeof(bool) }, null);
            }

            _panelSettingsPanelProp ??= typeof(PanelSettings).GetProperty("panel",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            return _updatePanels != null && _renderOffscreenPanels != null;
        }

        static object GetRuntimePanel(PanelSettings ps) {
            return _panelSettingsPanelProp?.GetValue(ps);
        }

        // In edit mode nothing runs the panel's repaint-phase updater (UIRRepaintUpdater),
        // so its RenderTreeManager may not exist and Render() silently draws nothing -
        // that was the blank-capture bug. The updater's Update() is idempotent: it creates
        // the render tree on first use and processes pending changes, exactly what the
        // play-mode frame loop does every frame, so calling it here is safe in both modes.
        static void EnsurePanelRenderTree(object runtimePanel) {
            if (runtimePanel == null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object renderer = null;
            for (var t = runtimePanel.GetType(); t != null; t = t.BaseType) {
                var f = t.GetField("panelRenderer", flags | BindingFlags.DeclaredOnly);
                if (f != null) {
                    renderer = f.GetValue(runtimePanel);
                    break;
                }
            }
            renderer?.GetType()
                .GetMethod("Update", flags, null, Type.EmptyTypes, null)
                ?.Invoke(renderer, null);
        }

        public static ToolResult CapturePanel(JObject args) {
            var panelPath = args.Value<string>("panelPath");
            var width = args.Value<int?>("width") ?? 1920;
            var height = args.Value<int?>("height") ?? 1080;
            width = Mathf.Clamp(width, MIN_DIM, MAX_DIM);
            height = Mathf.Clamp(height, MIN_DIM, MAX_DIM);

            PanelSettings panel;
            string resolvedPath;

            if (!string.IsNullOrEmpty(panelPath)) {
                panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
                if (panel == null) {
                    return ToolResultUtil.Error($"PanelSettings asset not found at: {panelPath}");
                }
                resolvedPath = panelPath;
            } else {
                if (!TryFindActivePanelInScene(out panel, out var foundOn)) {
                    return ToolResultUtil.Error(
                        "No active PanelSettings found in loaded scenes. Pass panelPath explicitly.");
                }
                resolvedPath = $"(from UIDocument on '{foundOn}')";
            }

            var origTarget = panel.targetTexture;
            RenderTexture rt = null;
            Texture2D tex = null;
            var prevActive = RenderTexture.active;

            try {
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) {
                    name = "UnityMcp_CapturePanel_RT"
                };
                rt.Create();

                panel.targetTexture = rt;
                MarkDocumentsDirtyForPanel(panel);

                if (!ResolveRuntimeUtilityMethods()) {
                    return ToolResultUtil.Error(
                        "UIElementsRuntimeUtility.UpdatePanels/RenderOffscreenPanels not available in this Unity version. " +
                        "Update the package or open an issue.");
                }
                // Update once so the panel notices the targetTexture change and re-registers as offscreen.
                _updatePanels.Invoke(null, null);

                var runtimePanel = GetRuntimePanel(panel);
                EnsurePanelRenderTree(runtimePanel);

                // Prefer direct RenderPanel(panel, restoreState=true) when accessible — it bypasses the offscreen-list
                // filtering that can skip freshly-reassigned panels on the first frame.
                if (runtimePanel != null && _renderPanel != null) {
                    _renderPanel.Invoke(null, new[] { runtimePanel, (object)true });
                } else {
                    _renderOffscreenPanels.Invoke(null, null);
                }

                GL.Flush();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                var png = tex.EncodeToPNG();
                if (png == null || png.Length == 0) {
                    return ToolResultUtil.Error("EncodeToPNG returned no data.");
                }

                var b64 = Convert.ToBase64String(png);
                var info = $"Captured PanelSettings {resolvedPath} at {width}x{height} ({png.Length} bytes PNG)";
                return ToolResultUtil.ImageWithText(b64, "image/png", info);
            } catch (Exception e) {
                Debug.LogException(e);
                return ToolResultUtil.Error($"CapturePanel failed: {e.GetType().Name}: {e.Message}");
            } finally {
                RenderTexture.active = prevActive;
                if (panel != null) panel.targetTexture = origTarget;
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null) {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }

        public static ToolResult CaptureGameView(JObject args) {
            if (EditorApplication.isPlaying) {
                return CaptureGameViewPlayMode();
            }
            return CaptureGameViewEditMode();
        }

        static ToolResult CaptureGameViewPlayMode() {
            Texture2D shot = null;
            try {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                if (shot == null || shot.width == 0) {
                    return ToolResultUtil.Error(
                        "ScreenCapture returned empty texture. Game view may not have rendered yet.");
                }
                var png = shot.EncodeToPNG();
                var b64 = Convert.ToBase64String(png);
                var info = $"Captured Game view at {shot.width}x{shot.height} ({png.Length} bytes PNG)";
                return ToolResultUtil.ImageWithText(b64, "image/png", info);
            } catch (Exception e) {
                Debug.LogException(e);
                return ToolResultUtil.Error($"CaptureGameView (play mode) failed: {e.Message}");
            } finally {
                if (shot != null) UnityEngine.Object.DestroyImmediate(shot);
            }
        }

        static ToolResult CaptureGameViewEditMode() {
            try {
                var asm = typeof(EditorWindow).Assembly;
                var pmvType = asm.GetType("UnityEditor.PlayModeView");
                if (pmvType == null) {
                    return ToolResultUtil.Error(
                        "UnityEditor.PlayModeView type not found. " +
                        "Use unity_capture_panel instead for edit-mode captures.");
                }

                var window = EditorWindow.GetWindow(pmvType, false, null, false);
                if (window == null) {
                    return ToolResultUtil.Error(
                        "No Game view window found. Open Window > General > Game first, " +
                        "or use unity_capture_panel.");
                }

                window.Repaint();

                var rtProp = pmvType.GetProperty("targetTexture",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (rtProp == null) {
                    return ToolResultUtil.Error(
                        "PlayModeView.targetTexture not accessible. Use unity_capture_panel.");
                }

                var rt = rtProp.GetValue(window) as RenderTexture;
                if (rt == null) {
                    return ToolResultUtil.Error(
                        "Game view has no current render texture. Try again after the editor repaints, " +
                        "or use unity_capture_panel.");
                }

                var prev = RenderTexture.active;
                Texture2D tex = null;
                try {
                    RenderTexture.active = rt;
                    tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();

                    var png = tex.EncodeToPNG();
                    var b64 = Convert.ToBase64String(png);
                    var info = $"Captured Game view (edit mode) at {rt.width}x{rt.height} ({png.Length} bytes PNG)";
                    return ToolResultUtil.ImageWithText(b64, "image/png", info);
                } finally {
                    RenderTexture.active = prev;
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                }
            } catch (Exception e) {
                Debug.LogException(e);
                return ToolResultUtil.Error($"CaptureGameView (edit mode) failed: {e.Message}");
            }
        }

        static bool TryFindActivePanelInScene(out PanelSettings panel, out string ownerName) {
            panel = null;
            ownerName = null;
            var docs = FindCompat.FindObjectsByType<UIDocument>();
            foreach (var doc in docs) {
                if (doc != null && doc.panelSettings != null && doc.isActiveAndEnabled) {
                    panel = doc.panelSettings;
                    ownerName = doc.gameObject.name;
                    return true;
                }
            }
            foreach (var doc in docs) {
                if (doc != null && doc.panelSettings != null) {
                    panel = doc.panelSettings;
                    ownerName = doc.gameObject.name;
                    return true;
                }
            }
            return false;
        }

        static void MarkDocumentsDirtyForPanel(PanelSettings panel) {
            var docs = FindCompat.FindObjectsByType<UIDocument>();
            foreach (var doc in docs) {
                if (doc != null && doc.panelSettings == panel) {
                    doc.rootVisualElement?.MarkDirtyRepaint();
                }
            }
        }
    }
}
