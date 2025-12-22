using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Tests {
    [TestFixture]
    public class PrefabToolsTests {
        const string TestPrefabPath = "Assets/TestPrefab_McpTemp.prefab";
        GameObject _prefabInstance;

        [SetUp]
        public void SetUp() {
            // Create a test prefab
            var go = new GameObject("TestPrefabRoot");
            var child = new GameObject("Child");
            child.transform.SetParent(go.transform);

            // Add some components
            go.AddComponent<BoxCollider>();
            child.AddComponent<SphereCollider>();

            // Save as prefab
            PrefabUtility.SaveAsPrefabAsset(go, TestPrefabPath);
            Object.DestroyImmediate(go);

            // Reload the prefab
            _prefabInstance = AssetDatabase.LoadAssetAtPath<GameObject>(TestPrefabPath);
        }

        [TearDown]
        public void TearDown() {
            // Delete test prefab
            if (File.Exists(TestPrefabPath)) {
                AssetDatabase.DeleteAsset(TestPrefabPath);
            }
            AssetDatabase.Refresh();
        }

        [Test]
        public void Load_ValidPath_ReturnsPrefabInfo() {
            var args = new JObject { ["path"] = TestPrefabPath };

            var result = Tools_Prefab.Load(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);
            // Unity renames the prefab root to match the asset filename
            Assert.AreEqual("TestPrefab_McpTemp", json["name"].Value<string>());
            Assert.IsTrue(json["isPrefabAsset"].Value<bool>());
            Assert.AreEqual(1, json["childCount"].Value<int>());
        }

        [Test]
        public void Load_InvalidPath_ReturnsError() {
            var args = new JObject { ["path"] = "Assets/NonExistent.prefab" };

            var result = Tools_Prefab.Load(args);

            Assert.IsTrue(result.isError);
        }

        [Test]
        public void Load_MissingPath_ReturnsError() {
            var args = new JObject();

            var result = Tools_Prefab.Load(args);

            Assert.IsTrue(result.isError);
        }

        [Test]
        public void GetHierarchy_ReturnsPrefabHierarchy() {
            var args = new JObject { ["path"] = TestPrefabPath };

            var result = Tools_Prefab.GetHierarchy(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);
            var hierarchy = json["hierarchy"];
            Assert.IsNotNull(hierarchy);
            // Unity renames the prefab root to match the asset filename
            Assert.AreEqual("TestPrefab_McpTemp", hierarchy["name"].Value<string>());

            // Check children
            var children = hierarchy["children"] as JArray;
            Assert.IsNotNull(children);
            Assert.AreEqual(1, children.Count);
            Assert.AreEqual("Child", children[0]["name"].Value<string>());
        }

        [Test]
        public void GetHierarchy_WithInstanceId_Works() {
            var args = new JObject { ["instanceId"] = _prefabInstance.GetInstanceID() };

            var result = Tools_Prefab.GetHierarchy(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
        }

        [Test]
        public void FindComponent_OnRoot_ReturnsComponent() {
            var args = new JObject {
                ["prefabPath"] = TestPrefabPath,
                ["type"] = "BoxCollider"
            };

            var result = Tools_Prefab.FindComponent(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);
            Assert.AreEqual("UnityEngine.BoxCollider", json["componentType"].Value<string>());
        }

        [Test]
        public void FindComponent_OnChild_ReturnsComponent() {
            var args = new JObject {
                ["prefabPath"] = TestPrefabPath,
                ["childPath"] = "Child",
                ["type"] = "SphereCollider"
            };

            var result = Tools_Prefab.FindComponent(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);
            Assert.AreEqual("UnityEngine.SphereCollider", json["componentType"].Value<string>());
        }

        [Test]
        public void FindComponent_NonExistentChild_ReturnsError() {
            var args = new JObject {
                ["prefabPath"] = TestPrefabPath,
                ["childPath"] = "NonExistent",
                ["type"] = "BoxCollider"
            };

            var result = Tools_Prefab.FindComponent(args);

            Assert.IsTrue(result.isError);
        }

        [Test]
        public void FindComponent_NonExistentType_ReturnsError() {
            var args = new JObject {
                ["prefabPath"] = TestPrefabPath,
                ["type"] = "Rigidbody"
            };

            var result = Tools_Prefab.FindComponent(args);

            Assert.IsTrue(result.isError);
        }

        [Test]
        public void Save_ValidPrefab_Succeeds() {
            var args = new JObject { ["path"] = TestPrefabPath };

            var result = Tools_Prefab.Save(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
        }

        [Test]
        public void Save_ByInstanceId_Succeeds() {
            var args = new JObject { ["instanceId"] = _prefabInstance.GetInstanceID() };

            var result = Tools_Prefab.Save(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
        }

        [Test]
        public void Save_InvalidPath_ReturnsError() {
            var args = new JObject { ["path"] = "Assets/NonExistent.prefab" };

            var result = Tools_Prefab.Save(args);

            Assert.IsTrue(result.isError);
        }
    }
}
