using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityMcp.Tests {
    [TestFixture]
    public class GameObjectResolverTests {
        [SetUp]
        public void SetUp() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void TryResolve_EmptyIdentifier_ReturnsFalse() {
            var result = GameObjectResolver.TryResolve("", out var go, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(go);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void TryResolve_NullIdentifier_ReturnsFalse() {
            var result = GameObjectResolver.TryResolve(null, out var go, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(go);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void TryResolve_ValidInstanceId_ReturnsGameObject() {
            var testGo = new GameObject("TestObject");
            var instanceId = testGo.GetInstanceID();

            var result = GameObjectResolver.TryResolve($"#{instanceId}", out var go, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual(testGo, go);
            Assert.IsNull(error);

            Object.DestroyImmediate(testGo);
        }

        [Test]
        public void TryResolve_InvalidInstanceId_ReturnsFalse() {
            var result = GameObjectResolver.TryResolve("#99999999", out var go, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(go);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void TryResolve_InvalidInstanceIdFormat_ReturnsFalse() {
            var result = GameObjectResolver.TryResolve("#notanumber", out var go, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(go);
            Assert.That(error, Does.Contain("Invalid instanceId"));
        }

        [Test]
        public void TryResolve_SimplePath_ReturnsGameObject() {
            var testGo = new GameObject("TestObject");

            var result = GameObjectResolver.TryResolve("TestObject", out var go, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual(testGo, go);
            Assert.IsNull(error);

            Object.DestroyImmediate(testGo);
        }

        [Test]
        public void TryResolve_NestedPath_ReturnsGameObject() {
            var parent = new GameObject("Parent");
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);

            var result = GameObjectResolver.TryResolve("Parent/Child", out var go, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual(child, go);
            Assert.IsNull(error);

            Object.DestroyImmediate(parent);
        }

        [Test]
        public void TryResolve_NonExistentPath_ReturnsFalse() {
            var result = GameObjectResolver.TryResolve("NonExistent/Path", out var go, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(go);
            Assert.That(error, Does.Contain("not found"));
        }

        [Test]
        public void GetPath_ReturnsCorrectPath() {
            var parent = new GameObject("Parent");
            var child = new GameObject("Child");
            var grandchild = new GameObject("Grandchild");
            child.transform.SetParent(parent.transform);
            grandchild.transform.SetParent(child.transform);

            var path = GameObjectResolver.GetPath(grandchild);

            Assert.AreEqual("Parent/Child/Grandchild", path);

            Object.DestroyImmediate(parent);
        }

        [Test]
        public void GetPath_RootObject_ReturnsName() {
            var testGo = new GameObject("RootObject");

            var path = GameObjectResolver.GetPath(testGo);

            Assert.AreEqual("RootObject", path);

            Object.DestroyImmediate(testGo);
        }

        [Test]
        public void GetPath_NullObject_ReturnsEmpty() {
            var path = GameObjectResolver.GetPath(null);

            Assert.AreEqual("", path);
        }

        [Test]
        public void GetQualifiedPath_IncludesSceneName() {
            var testGo = new GameObject("TestObject");

            var path = GameObjectResolver.GetQualifiedPath(testGo);

            Assert.That(path, Does.Contain(":/"));
            Assert.That(path, Does.EndWith("/TestObject"));

            Object.DestroyImmediate(testGo);
        }
    }
}
