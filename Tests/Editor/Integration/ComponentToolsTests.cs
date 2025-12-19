using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityMcp.Tests {
    [TestFixture]
    public class ComponentToolsTests {
        const string TestMaterialPath = "Assets/TestMaterial_McpTemp.mat";
        Material _testMaterial;

        [SetUp]
        public void SetUp() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Create a test material
            _testMaterial = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(_testMaterial, TestMaterialPath);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown() {
            // Clean up all test objects
            var objects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in objects) {
                Object.DestroyImmediate(obj);
            }

            // Delete test material
            if (File.Exists(TestMaterialPath)) {
                AssetDatabase.DeleteAsset(TestMaterialPath);
            }
        }

        [Test]
        public void SetProperty_ObjectReference_ByInstanceId_Works() {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = go.GetComponent<MeshRenderer>();

            var args = new JObject {
                ["target"] = go.name,
                ["type"] = "MeshRenderer",
                ["property"] = "m_Materials.Array.data[0]",
                ["value"] = _testMaterial.GetInstanceID()
            };

            var result = Tools_Component.SetProperty(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            Assert.AreEqual(_testMaterial, renderer.sharedMaterial);
        }

        [Test]
        public void SetProperty_ObjectReference_ByAssetPath_Works() {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = go.GetComponent<MeshRenderer>();

            var args = new JObject {
                ["target"] = go.name,
                ["type"] = "MeshRenderer",
                ["property"] = "m_Materials.Array.data[0]",
                ["value"] = TestMaterialPath
            };

            var result = Tools_Component.SetProperty(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            Assert.AreEqual(_testMaterial.name, renderer.sharedMaterial.name);
        }

        [Test]
        public void SetProperty_ObjectReference_ByObjectWithInstanceId_Works() {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = go.GetComponent<MeshRenderer>();

            var args = new JObject {
                ["target"] = go.name,
                ["type"] = "MeshRenderer",
                ["property"] = "m_Materials.Array.data[0]",
                ["value"] = new JObject { ["instanceId"] = _testMaterial.GetInstanceID() }
            };

            var result = Tools_Component.SetProperty(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            Assert.AreEqual(_testMaterial, renderer.sharedMaterial);
        }

        [Test]
        public void SetProperty_ObjectReference_ByObjectWithAssetPath_Works() {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = go.GetComponent<MeshRenderer>();

            var args = new JObject {
                ["target"] = go.name,
                ["type"] = "MeshRenderer",
                ["property"] = "m_Materials.Array.data[0]",
                ["value"] = new JObject { ["assetPath"] = TestMaterialPath }
            };

            var result = Tools_Component.SetProperty(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            Assert.AreEqual(_testMaterial.name, renderer.sharedMaterial.name);
        }

        [Test]
        public void SetProperty_ObjectReference_Null_ClearsReference() {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _testMaterial;

            var args = new JObject {
                ["target"] = go.name,
                ["type"] = "MeshRenderer",
                ["property"] = "m_Materials.Array.data[0]",
                ["value"] = JValue.CreateNull()
            };

            var result = Tools_Component.SetProperty(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            Assert.IsNull(renderer.sharedMaterial);
        }

        [Test]
        public void SetProperty_ObjectReference_InvalidInstanceId_ReturnsError() {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);

            var args = new JObject {
                ["target"] = go.name,
                ["type"] = "MeshRenderer",
                ["property"] = "m_Materials.Array.data[0]",
                ["value"] = 99999999
            };

            var result = Tools_Component.SetProperty(args);

            Assert.IsTrue(result.isError);
        }

        [Test]
        public void SetProperty_ObjectReference_InvalidAssetPath_ReturnsError() {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);

            var args = new JObject {
                ["target"] = go.name,
                ["type"] = "MeshRenderer",
                ["property"] = "m_Materials.Array.data[0]",
                ["value"] = "Assets/NonExistent.mat"
            };

            var result = Tools_Component.SetProperty(args);

            Assert.IsTrue(result.isError);
        }

        [Test]
        public void GetProperties_ReturnsObjectReference() {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _testMaterial;

            var args = new JObject {
                ["target"] = go.name,
                ["type"] = "MeshRenderer"
            };

            var result = Tools_Component.GetProperties(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);
            var props = json["properties"];
            // Material is in the array
            var material = props["m_Materials.Array.data[0]"];
            Assert.IsNotNull(material);
            Assert.AreEqual(_testMaterial.GetInstanceID(), material["instanceId"].Value<int>());
        }
    }
}
