using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace UnityMcp.Tests {
    [TestFixture]
    public class ReflectionToolsTests {
        // MARK: SearchTypes Tests
        [Test]
        public void SearchTypes_FindsGameObject() {
            var args = new JObject {
                ["pattern"] = "GameObject",
                ["namespace"] = "UnityEngine",
                ["maxResults"] = 10
            };

            var result = Tools_Reflection.SearchTypes(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);
            Assert.That(json["count"].Value<int>(), Is.GreaterThan(0));

            var types = json["types"] as JArray;
            Assert.IsNotNull(types);
            Assert.IsTrue(types.Count > 0);

            // Should find UnityEngine.GameObject
            bool foundGameObject = false;
            foreach (var type in types) {
                if (type["fullName"].Value<string>() == "UnityEngine.GameObject") {
                    foundGameObject = true;
                    Assert.AreEqual("GameObject", type["name"].Value<string>());
                    Assert.AreEqual("UnityEngine", type["namespace"].Value<string>());
                    Assert.IsTrue(type["isClass"].Value<bool>());
                    break;
                }
            }
            Assert.IsTrue(foundGameObject, "Should find UnityEngine.GameObject");
        }

        [Test]
        public void SearchTypes_ExactMatch_Works() {
            var args = new JObject {
                ["pattern"] = "^Vector3",
                ["namespace"] = "UnityEngine"
            };

            var result = Tools_Reflection.SearchTypes(args);

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            var types = json["types"] as JArray;

            // Exact match should only return types named exactly "Vector3"
            foreach (var type in types) {
                Assert.AreEqual("Vector3", type["name"].Value<string>());
            }
        }

        [Test]
        public void SearchTypes_AssemblyFilter_Works() {
            var args = new JObject {
                ["pattern"] = "Object",
                ["assemblyFilter"] = "UnityEngine.CoreModule",
                ["maxResults"] = 50
            };

            var result = Tools_Reflection.SearchTypes(args);

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            var types = json["types"] as JArray;

            foreach (var type in types) {
                Assert.AreEqual("UnityEngine.CoreModule", type["assembly"].Value<string>());
            }
        }

        [Test]
        public void SearchTypes_MissingPattern_ReturnsError() {
            var args = new JObject();

            var result = Tools_Reflection.SearchTypes(args);

            Assert.IsTrue(result.isError);
            Assert.That(result.content[0].text, Does.Contain("Missing param: pattern"));
        }

        // MARK: GetTypeInfo Tests
        [Test]
        public void GetTypeInfo_ReturnsValidInfo() {
            var args = new JObject {
                ["typeName"] = "UnityEngine.GameObject",
                ["sections"] = new JArray("methods", "properties", "constructors")
            };

            var result = Tools_Reflection.GetTypeInfo(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);

            // Check type info
            var typeInfo = json["type"];
            Assert.IsNotNull(typeInfo);
            Assert.AreEqual("UnityEngine.GameObject", typeInfo["fullName"].Value<string>());
            Assert.AreEqual("UnityEngine.Object", typeInfo["baseType"].Value<string>());

            // Check sections exist
            Assert.IsNotNull(json["methods"]);
            Assert.IsNotNull(json["properties"]);
            Assert.IsNotNull(json["constructors"]);
        }

        [Test]
        public void GetTypeInfo_IncludesPrivateMembers() {
            var args = new JObject {
                ["typeName"] = "UnityEngine.Vector3",
                ["includePrivate"] = true,
                ["sections"] = new JArray("fields")
            };

            var result = Tools_Reflection.GetTypeInfo(args);

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            var fields = json["fields"] as JArray;

            // Should have x, y, z public fields
            Assert.That(fields.Count, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void GetTypeInfo_TypeNotFound_ReturnsError() {
            var args = new JObject {
                ["typeName"] = "NonExistent.FakeType"
            };

            var result = Tools_Reflection.GetTypeInfo(args);

            Assert.IsTrue(result.isError);
            Assert.That(result.content[0].text, Does.Contain("Type not found"));
        }

        // MARK: GetMethodInfo Tests
        [Test]
        public void GetMethodInfo_FindsOverloads() {
            var args = new JObject {
                ["typeName"] = "UnityEngine.GameObject",
                ["methodName"] = "AddComponent"
            };

            var result = Tools_Reflection.GetMethodInfo(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);

            Assert.AreEqual("UnityEngine.GameObject", json["typeName"].Value<string>());
            Assert.AreEqual("AddComponent", json["methodName"].Value<string>());
            Assert.That(json["overloadCount"].Value<int>(), Is.GreaterThanOrEqualTo(2));

            var overloads = json["overloads"] as JArray;
            Assert.IsNotNull(overloads);

            // Find the generic overload
            bool foundGeneric = false;
            foreach (var overload in overloads) {
                if (overload["isGeneric"]?.Value<bool>() == true) {
                    foundGeneric = true;
                    Assert.IsNotNull(overload["genericParameters"]);
                    break;
                }
            }
            Assert.IsTrue(foundGeneric, "Should find generic AddComponent<T> overload");
        }

        [Test]
        public void GetMethodInfo_MethodNotFound_ReturnsError() {
            var args = new JObject {
                ["typeName"] = "UnityEngine.GameObject",
                ["methodName"] = "NonExistentMethod"
            };

            var result = Tools_Reflection.GetMethodInfo(args);

            Assert.IsTrue(result.isError);
            Assert.That(result.content[0].text, Does.Contain("Method not found"));
        }

        // MARK: GetAssemblies Tests
        [Test]
        public void GetAssemblies_ReturnsAssemblies() {
            var args = new JObject {
                ["filter"] = "UnityEngine"
            };

            var result = Tools_Reflection.GetAssemblies(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);

            Assert.That(json["count"].Value<int>(), Is.GreaterThan(0));
            var assemblies = json["assemblies"] as JArray;
            Assert.IsNotNull(assemblies);

            // All should contain "UnityEngine" in name
            foreach (var asm in assemblies) {
                Assert.That(asm["name"].Value<string>(), Does.Contain("Unity").IgnoreCase);
            }
        }

        [Test]
        public void GetAssemblies_ExcludesSystemByDefault() {
            var args = new JObject();

            var result = Tools_Reflection.GetAssemblies(args);

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            var assemblies = json["assemblies"] as JArray;

            foreach (var asm in assemblies) {
                var name = asm["name"].Value<string>();
                Assert.IsFalse(name.StartsWith("System."), $"Should not include System assemblies: {name}");
                Assert.IsFalse(name.StartsWith("mscorlib"), $"Should not include mscorlib: {name}");
            }
        }

        // MARK: Decompile Tests
        [Test]
        public void Decompile_ReturnsSource() {
            var args = new JObject {
                ["typeName"] = "UnityEngine.Vector3",
                ["limit"] = 50
            };

            var result = Tools_Reflection.Decompile(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);

            Assert.AreEqual("UnityEngine.Vector3", json["typeName"].Value<string>());
            Assert.That(json["totalLines"].Value<int>(), Is.GreaterThan(100));
            Assert.AreEqual(0, json["offset"].Value<int>());
            Assert.AreEqual(50, json["limit"].Value<int>());
            Assert.IsTrue(json["hasMore"].Value<bool>());

            var source = json["source"].Value<string>();
            Assert.That(source, Does.Contain("struct Vector3"));
            Assert.That(source, Does.Contain("public float x"));
        }

        [Test]
        public void Decompile_Pagination_Works() {
            var args1 = new JObject {
                ["typeName"] = "UnityEngine.Vector3",
                ["offset"] = 0,
                ["limit"] = 20
            };
            var args2 = new JObject {
                ["typeName"] = "UnityEngine.Vector3",
                ["offset"] = 20,
                ["limit"] = 20
            };

            var result1 = Tools_Reflection.Decompile(args1);
            var result2 = Tools_Reflection.Decompile(args2);

            Assert.IsFalse(result1.isError);
            Assert.IsFalse(result2.isError);

            var json1 = JObject.Parse(result1.content[0].text);
            var json2 = JObject.Parse(result2.content[0].text);

            // Same total, different offsets
            Assert.AreEqual(json1["totalLines"].Value<int>(), json2["totalLines"].Value<int>());
            Assert.AreEqual(0, json1["offset"].Value<int>());
            Assert.AreEqual(20, json2["offset"].Value<int>());

            // Different source content
            Assert.AreNotEqual(json1["source"].Value<string>(), json2["source"].Value<string>());
        }

        [Test]
        public void Decompile_TypeNotFound_ReturnsError() {
            var args = new JObject {
                ["typeName"] = "NonExistent.FakeType"
            };

            var result = Tools_Reflection.Decompile(args);

            Assert.IsTrue(result.isError);
            Assert.That(result.content[0].text, Does.Contain("Type not found"));
        }

        // MARK: InvokeStatic Tests
        [Test]
        public void InvokeStatic_PropertyGetter_Works() {
            var args = new JObject {
                ["typeName"] = "UnityEditor.EditorApplication",
                ["methodName"] = "isPlaying",
                ["isProperty"] = true
            };

            var result = Tools_Reflection.InvokeStatic(args);

            Assert.IsFalse(result.isError, $"Expected success but got error: {result.content[0].text}");
            var json = JObject.Parse(result.content[0].text);

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.AreEqual("UnityEditor.EditorApplication", json["typeName"].Value<string>());
            Assert.AreEqual("isPlaying", json["memberName"].Value<string>());
            Assert.AreEqual("property", json["memberType"].Value<string>());
            Assert.AreEqual("System.Boolean", json["returnType"].Value<string>());
            Assert.IsFalse(json["isVoid"].Value<bool>());
            // isPlaying should be false in edit mode during tests
            Assert.IsFalse(json["result"].Value<bool>());
        }

        [Test]
        public void InvokeStatic_MethodWithParams_ReturnsError() {
            var args = new JObject {
                ["typeName"] = "UnityEngine.Debug",
                ["methodName"] = "Log"
            };

            var result = Tools_Reflection.InvokeStatic(args);

            Assert.IsTrue(result.isError);
            var json = JObject.Parse(result.content[0].text);
            Assert.IsFalse(json["success"].Value<bool>());
            Assert.AreEqual("ParametersRequired", json["errorType"].Value<string>());
        }

        [Test]
        public void InvokeStatic_TypeNotFound_ReturnsError() {
            var args = new JObject {
                ["typeName"] = "NonExistent.FakeType",
                ["methodName"] = "SomeMethod"
            };

            var result = Tools_Reflection.InvokeStatic(args);

            Assert.IsTrue(result.isError);
            var json = JObject.Parse(result.content[0].text);
            Assert.IsFalse(json["success"].Value<bool>());
            Assert.AreEqual("TypeNotFound", json["errorType"].Value<string>());
        }

        [Test]
        public void InvokeStatic_MethodNotFound_ReturnsError() {
            var args = new JObject {
                ["typeName"] = "UnityEditor.EditorApplication",
                ["methodName"] = "NonExistentMethod"
            };

            var result = Tools_Reflection.InvokeStatic(args);

            Assert.IsTrue(result.isError);
            var json = JObject.Parse(result.content[0].text);
            Assert.IsFalse(json["success"].Value<bool>());
            Assert.AreEqual("MethodNotFound", json["errorType"].Value<string>());
        }

        [Test]
        public void InvokeStatic_PropertyNotFound_ReturnsError() {
            var args = new JObject {
                ["typeName"] = "UnityEditor.EditorApplication",
                ["methodName"] = "nonExistentProperty",
                ["isProperty"] = true
            };

            var result = Tools_Reflection.InvokeStatic(args);

            Assert.IsTrue(result.isError);
            var json = JObject.Parse(result.content[0].text);
            Assert.IsFalse(json["success"].Value<bool>());
            Assert.AreEqual("PropertyNotFound", json["errorType"].Value<string>());
        }
    }
}
