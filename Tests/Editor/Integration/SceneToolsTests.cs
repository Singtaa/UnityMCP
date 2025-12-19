using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.Tests {
    [TestFixture]
    public class SceneToolsTests {
        [SetUp]
        public void SetUp() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void List_ReturnsLoadedScenes() {
            var args = new JObject();

            var result = Tools_Scene.List(args);

            Assert.IsFalse(result.isError);
            var scenes = JArray.Parse(result.content[0].text);
            Assert.GreaterOrEqual(scenes.Count, 1);
        }

        [Test]
        public void List_IncludesSceneProperties() {
            var args = new JObject();

            var result = Tools_Scene.List(args);

            Assert.IsFalse(result.isError);
            var scenes = JArray.Parse(result.content[0].text);
            var scene = scenes[0];

            Assert.IsNotNull(scene["name"]);
            Assert.IsNotNull(scene["isLoaded"]);
            Assert.IsNotNull(scene["isDirty"]);
        }

        [Test]
        public void New_CreatesEmptyScene() {
            var args = new JObject { ["setup"] = "empty" };

            var result = Tools_Scene.New(args);

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            Assert.IsTrue((bool)json["isLoaded"]);
        }

        [Test]
        public void New_CreatesDefaultScene() {
            var args = new JObject { ["setup"] = "default" };

            var result = Tools_Scene.New(args);

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            Assert.IsTrue((bool)json["isLoaded"]);

            // Default scene should have a camera and light
            var camera = Object.FindFirstObjectByType<Camera>();
            Assert.IsNotNull(camera);
        }

        [Test]
        public void Close_NonExistentScene_ReturnsError() {
            var args = new JObject { ["scene"] = "NonExistentScene" };

            var result = Tools_Scene.Close(args);

            Assert.IsTrue(result.isError);
            Assert.That(result.content[0].text, Does.Contain("not found"));
        }

        [Test]
        public void Save_NonExistentScene_ReturnsError() {
            var args = new JObject { ["scene"] = "NonExistentScene" };

            var result = Tools_Scene.Save(args);

            Assert.IsTrue(result.isError);
            Assert.That(result.content[0].text, Does.Contain("not found"));
        }
    }
}
