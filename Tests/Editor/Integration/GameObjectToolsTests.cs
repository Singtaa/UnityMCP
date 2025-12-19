using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityMcp.Tests {
    [TestFixture]
    public class GameObjectToolsTests {
        [SetUp]
        public void SetUp() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown() {
            // Clean up all test objects
            var objects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in objects) {
                Object.DestroyImmediate(obj);
            }
        }

        [Test]
        public void Create_WithName_CreatesGameObject() {
            var args = new JObject { ["name"] = "TestObject" };

            var result = Tools_GameObject.Create(args);

            Assert.IsFalse(result.isError);
            var go = GameObject.Find("TestObject");
            Assert.IsNotNull(go);
        }

        [Test]
        public void Create_WithPrimitive_CreatesMesh() {
            var args = new JObject {
                ["name"] = "TestCube",
                ["primitive"] = "Cube"
            };

            var result = Tools_GameObject.Create(args);

            Assert.IsFalse(result.isError);
            var go = GameObject.Find("TestCube");
            Assert.IsNotNull(go);
            Assert.IsNotNull(go.GetComponent<MeshFilter>());
            Assert.IsNotNull(go.GetComponent<MeshRenderer>());
        }

        [Test]
        public void Create_WithParent_SetsParent() {
            var parent = new GameObject("Parent");
            var args = new JObject {
                ["name"] = "Child",
                ["parent"] = "Parent"
            };

            var result = Tools_GameObject.Create(args);

            Assert.IsFalse(result.isError);
            var child = GameObject.Find("Child");
            Assert.IsNotNull(child);
            Assert.AreEqual(parent.transform, child.transform.parent);
        }

        [Test]
        public void Delete_ExistingObject_RemovesIt() {
            var go = new GameObject("ToDelete");
            var args = new JObject { ["target"] = "ToDelete" };

            var result = Tools_GameObject.Delete(args);

            Assert.IsFalse(result.isError);
            Assert.IsNull(GameObject.Find("ToDelete"));
        }

        [Test]
        public void Delete_NonExistent_ReturnsError() {
            var args = new JObject { ["target"] = "NonExistent" };

            var result = Tools_GameObject.Delete(args);

            Assert.IsTrue(result.isError);
        }

        [Test]
        public void Find_ByName_ReturnsMatches() {
            new GameObject("Player");
            new GameObject("PlayerController");
            new GameObject("Enemy");

            var args = new JObject { ["name"] = "Player" };
            var result = Tools_GameObject.Find(args);

            Assert.IsFalse(result.isError);
            var json = JArray.Parse(result.content[0].text);
            Assert.AreEqual(2, json.Count);
        }

        [Test]
        public void Find_ByTag_ReturnsMatches() {
            var go1 = new GameObject("TaggedObject1");
            var go2 = new GameObject("TaggedObject2");
            var go3 = new GameObject("UntaggedObject");
            go1.tag = "MainCamera";
            go2.tag = "MainCamera";

            var args = new JObject { ["tag"] = "MainCamera" };
            var result = Tools_GameObject.Find(args);

            Assert.IsFalse(result.isError);
            var json = JArray.Parse(result.content[0].text);
            Assert.AreEqual(2, json.Count);
        }

        [Test]
        public void Find_ByPath_ReturnsSingleMatch() {
            var parent = new GameObject("Parent");
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);

            var args = new JObject { ["path"] = "Parent/Child" };
            var result = Tools_GameObject.Find(args);

            Assert.IsFalse(result.isError);
            var json = JArray.Parse(result.content[0].text);
            Assert.AreEqual(1, json.Count);
        }

        [Test]
        public void SetActive_DisablesGameObject() {
            var go = new GameObject("TestObject");
            Assert.IsTrue(go.activeSelf);

            var args = new JObject { ["target"] = "TestObject", ["active"] = false };
            var result = Tools_GameObject.SetActive(args);

            Assert.IsFalse(result.isError);
            Assert.IsFalse(go.activeSelf);
        }

        [Test]
        public void SetParent_ReparentsGameObject() {
            var parent = new GameObject("NewParent");
            var child = new GameObject("Child");
            Assert.IsNull(child.transform.parent);

            var args = new JObject { ["target"] = "Child", ["parent"] = "NewParent" };
            var result = Tools_GameObject.SetParent(args);

            Assert.IsFalse(result.isError);
            Assert.AreEqual(parent.transform, child.transform.parent);
        }

        [Test]
        public void Rename_ChangesName() {
            var go = new GameObject("OldName");

            var args = new JObject { ["target"] = "OldName", ["name"] = "NewName" };
            var result = Tools_GameObject.Rename(args);

            Assert.IsFalse(result.isError);
            Assert.AreEqual("NewName", go.name);
        }

        [Test]
        public void Duplicate_CreatesClone() {
            var original = new GameObject("Original");

            var args = new JObject { ["target"] = "Original" };
            var result = Tools_GameObject.Duplicate(args);

            Assert.IsFalse(result.isError);

            // Should have 2 objects named "Original" now
            var objects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            var count = 0;
            foreach (var obj in objects) {
                if (obj.name == "Original") count++;
            }
            Assert.AreEqual(2, count);
        }
    }
}
