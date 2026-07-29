using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace UnityMcp.Tests {
    [TestFixture]
    public class CaptureToolsTests {
        const string TempAssetPath = "Assets/__UnityMcp_CaptureTest_PanelSettings.asset";

        [OneTimeSetUp]
        public void RequireGraphics() {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Panel capture needs a graphics device (running with -nographics).");
        }

        // Regression test for the edit-mode blank capture: without the repaint-phase
        // updater invocation, the panel's RenderTreeManager never exists outside play
        // mode and the captured image is empty.
        [Test]
        public void CapturePanel_EditMode_ProducesNonBlankImage() {
            Assert.IsFalse(EditorApplication.isPlaying, "This is an edit-mode regression test.");

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            var themeGuid = AssetDatabase.FindAssets("t:ThemeStyleSheet").FirstOrDefault();
            if (!string.IsNullOrEmpty(themeGuid))
                ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                    AssetDatabase.GUIDToAssetPath(themeGuid));
            AssetDatabase.CreateAsset(ps, TempAssetPath);
            var go = new GameObject("__UnityMcp_CaptureTestDoc");
            Texture2D decoded = null;
            try {
                var doc = go.AddComponent<UIDocument>();
                doc.panelSettings = ps;
                doc.rootVisualElement.Add(new VisualElement {
                    style = {
                        flexGrow = 1,
                        backgroundColor = new Color(1f, 0f, 1f, 1f) // magenta, unmistakable
                    }
                });

                var result = Tools_Capture.CapturePanel(new JObject {
                    ["panelPath"] = TempAssetPath,
                    ["width"] = 128,
                    ["height"] = 128
                });

                Assert.IsFalse(result.isError,
                    result.content.FirstOrDefault(c => c.type == "text")?.text ?? "(no text)");
                var image = result.content.FirstOrDefault(c => c.type == "image");
                Assert.IsNotNull(image, "Expected an image content block");

                decoded = new Texture2D(2, 2);
                Assert.IsTrue(decoded.LoadImage(Convert.FromBase64String(image.data)),
                    "PNG did not decode");

                var px = decoded.GetPixels32();
                int magenta = px.Count(p => p.r > 200 && p.b > 200 && p.g < 80);
                Assert.That(magenta, Is.GreaterThan(px.Length / 2),
                    $"Capture should be mostly the magenta root element, got {magenta}/{px.Length} magenta pixels");
            } finally {
                if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded);
                UnityEngine.Object.DestroyImmediate(go);
                AssetDatabase.DeleteAsset(TempAssetPath);
            }
        }

        [Test]
        public void CapturePanel_MissingAsset_ReturnsError() {
            var result = Tools_Capture.CapturePanel(new JObject {
                ["panelPath"] = "Assets/__DoesNotExist_PanelSettings.asset"
            });

            Assert.IsTrue(result.isError);
        }
    }
}
