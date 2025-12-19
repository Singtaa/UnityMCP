using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityMcp.Tests {
    [TestFixture]
    public class TransformToolsTests {
        GameObject _testObject;

        [SetUp]
        public void SetUp() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _testObject = new GameObject("TestObject");
        }

        [TearDown]
        public void TearDown() {
            if (_testObject != null) {
                Object.DestroyImmediate(_testObject);
            }
        }

        [Test]
        public void Get_WorldSpace_ReturnsWorldValues() {
            _testObject.transform.position = new Vector3(1, 2, 3);

            var args = new JObject { ["target"] = "TestObject", ["space"] = "world" };
            var result = Tools_Transform.Get(args);

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            Assert.AreEqual("world", (string)json["space"]);

            var pos = json["position"] as JArray;
            Assert.AreEqual(1f, (float)pos[0], 0.001f);
            Assert.AreEqual(2f, (float)pos[1], 0.001f);
            Assert.AreEqual(3f, (float)pos[2], 0.001f);
        }

        [Test]
        public void Get_LocalSpace_ReturnsLocalValues() {
            var parent = new GameObject("Parent");
            parent.transform.position = new Vector3(10, 0, 0);
            _testObject.transform.SetParent(parent.transform);
            _testObject.transform.localPosition = new Vector3(1, 2, 3);

            // Use full path since TestObject is now a child of Parent
            var args = new JObject { ["target"] = "Parent/TestObject", ["space"] = "local" };
            var result = Tools_Transform.Get(args);

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            Assert.AreEqual("local", (string)json["space"]);

            var pos = json["position"] as JArray;
            Assert.AreEqual(1f, (float)pos[0], 0.001f);
            Assert.AreEqual(2f, (float)pos[1], 0.001f);
            Assert.AreEqual(3f, (float)pos[2], 0.001f);

            Object.DestroyImmediate(parent);
        }

        [Test]
        public void Set_Position_MovesObject() {
            var args = new JObject {
                ["target"] = "TestObject",
                ["position"] = new JArray { 5, 10, 15 }
            };

            var result = Tools_Transform.Set(args);

            Assert.IsFalse(result.isError);
            Assert.AreEqual(new Vector3(5, 10, 15), _testObject.transform.localPosition);
        }

        [Test]
        public void Set_Rotation_RotatesObject() {
            var args = new JObject {
                ["target"] = "TestObject",
                ["rotation"] = new JArray { 0, 90, 0 }
            };

            var result = Tools_Transform.Set(args);

            Assert.IsFalse(result.isError);
            Assert.AreEqual(90f, _testObject.transform.localEulerAngles.y, 0.001f);
        }

        [Test]
        public void Set_Scale_ScalesObject() {
            var args = new JObject {
                ["target"] = "TestObject",
                ["scale"] = new JArray { 2, 3, 4 }
            };

            var result = Tools_Transform.Set(args);

            Assert.IsFalse(result.isError);
            Assert.AreEqual(new Vector3(2, 3, 4), _testObject.transform.localScale);
        }

        [Test]
        public void Translate_World_MovesInWorldSpace() {
            _testObject.transform.position = Vector3.zero;
            _testObject.transform.rotation = Quaternion.Euler(0, 90, 0);

            var args = new JObject {
                ["target"] = "TestObject",
                ["delta"] = new JArray { 1, 0, 0 },
                ["space"] = "world"
            };

            var result = Tools_Transform.Translate(args);

            Assert.IsFalse(result.isError);
            Assert.AreEqual(1f, _testObject.transform.position.x, 0.001f);
        }

        [Test]
        public void Translate_Self_MovesInLocalSpace() {
            _testObject.transform.position = Vector3.zero;
            _testObject.transform.rotation = Quaternion.Euler(0, 90, 0);

            var args = new JObject {
                ["target"] = "TestObject",
                ["delta"] = new JArray { 0, 0, 1 },
                ["space"] = "self"
            };

            var result = Tools_Transform.Translate(args);

            Assert.IsFalse(result.isError);
            // Moving in local +Z when rotated 90° around Y should move in world +X
            Assert.AreEqual(1f, _testObject.transform.position.x, 0.001f);
        }

        [Test]
        public void Rotate_World_RotatesInWorldSpace() {
            _testObject.transform.rotation = Quaternion.identity;

            var args = new JObject {
                ["target"] = "TestObject",
                ["euler"] = new JArray { 0, 45, 0 },
                ["space"] = "world"
            };

            var result = Tools_Transform.Rotate(args);

            Assert.IsFalse(result.isError);
            Assert.AreEqual(45f, _testObject.transform.eulerAngles.y, 0.001f);
        }

        [Test]
        public void Reset_ClearsTransform() {
            _testObject.transform.localPosition = new Vector3(1, 2, 3);
            _testObject.transform.localRotation = Quaternion.Euler(45, 45, 45);
            _testObject.transform.localScale = new Vector3(2, 2, 2);

            var args = new JObject { ["target"] = "TestObject" };
            var result = Tools_Transform.Reset(args);

            Assert.IsFalse(result.isError);
            Assert.AreEqual(Vector3.zero, _testObject.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, _testObject.transform.localRotation);
            Assert.AreEqual(Vector3.one, _testObject.transform.localScale);
        }

        [Test]
        public void Reset_PartialReset_OnlyResetsSpecified() {
            _testObject.transform.localPosition = new Vector3(1, 2, 3);
            _testObject.transform.localRotation = Quaternion.Euler(45, 45, 45);
            _testObject.transform.localScale = new Vector3(2, 2, 2);

            var args = new JObject {
                ["target"] = "TestObject",
                ["position"] = true,
                ["rotation"] = false,
                ["scale"] = false
            };
            var result = Tools_Transform.Reset(args);

            Assert.IsFalse(result.isError);
            Assert.AreEqual(Vector3.zero, _testObject.transform.localPosition);
            Assert.AreNotEqual(Quaternion.identity, _testObject.transform.localRotation);
            Assert.AreEqual(new Vector3(2, 2, 2), _testObject.transform.localScale);
        }

        [Test]
        public void LookAt_Point_OrientsTowardPoint() {
            _testObject.transform.position = Vector3.zero;

            var args = new JObject {
                ["target"] = "TestObject",
                ["point"] = new JArray { 0, 0, 10 }
            };

            var result = Tools_Transform.LookAt(args);

            Assert.IsFalse(result.isError);
            // Should be looking forward (0, 0, 1) approximately
            var forward = _testObject.transform.forward;
            Assert.AreEqual(0f, forward.x, 0.001f);
            Assert.AreEqual(0f, forward.y, 0.001f);
            Assert.AreEqual(1f, forward.z, 0.001f);
        }

        [Test]
        public void LookAt_Target_OrientsTowardTarget() {
            _testObject.transform.position = Vector3.zero;
            var target = new GameObject("LookTarget");
            target.transform.position = new Vector3(0, 0, 10);

            var args = new JObject {
                ["target"] = "TestObject",
                ["lookAtTarget"] = "LookTarget"
            };

            var result = Tools_Transform.LookAt(args);

            Assert.IsFalse(result.isError);
            var forward = _testObject.transform.forward;
            Assert.AreEqual(0f, forward.x, 0.001f);
            Assert.AreEqual(0f, forward.y, 0.001f);
            Assert.AreEqual(1f, forward.z, 0.001f);

            Object.DestroyImmediate(target);
        }
    }
}
