using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace UnityMcp.Tests {
    [TestFixture]
    public class AssetsFindTests {
        [Test]
        public void Registered_InToolRegistry() {
            ToolRegistry.EnsureInitialized();
            CollectionAssert.Contains(ToolRegistry.GetToolNames().ToList(), "unity.assets.find");
        }

        // The handler is private like the other inline ToolRegistry handlers; invoke it
        // the same way the bridge dispatcher does.
        static ToolResult Call(JObject args) {
            var method = typeof(ToolRegistry).GetMethod("AssetsFind",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "AssetsFind handler not found");
            return (ToolResult)method.Invoke(null, new object[] { args });
        }

        [Test]
        public void FindByType_Scripts_ReturnsResults() {
            var result = Call(new JObject { ["query"] = "t:Script", ["limit"] = 10 });

            Assert.IsFalse(result.isError, result.content[0].text);
            var json = JObject.Parse(result.content[0].text);
            Assert.That(json["totalCount"].Value<int>(), Is.GreaterThan(0));
            Assert.That(json["returnedCount"].Value<int>(), Is.LessThanOrEqualTo(10));

            var first = (json["results"] as JArray)[0];
            Assert.IsNotNull(first["path"].Value<string>());
            Assert.IsNotNull(first["guid"].Value<string>());
            Assert.IsNotNull(first["name"].Value<string>());
        }

        [Test]
        public void FindByName_KnownScript_Found() {
            var result = Call(new JObject { ["query"] = "ToolRegistry t:Script" });

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            var found = (json["results"] as JArray)
                .Any(r => r["name"].Value<string>() == "ToolRegistry");
            Assert.IsTrue(found, "Expected to find ToolRegistry.cs");
        }

        [Test]
        public void FolderScoping_PackageFolder_Works() {
            var result = Call(new JObject {
                ["query"] = "t:Script",
                ["folders"] = new JArray("Packages/com.singtaa.unity-mcp"),
                ["limit"] = 5
            });

            Assert.IsFalse(result.isError, result.content[0].text);
            var json = JObject.Parse(result.content[0].text);
            Assert.That(json["totalCount"].Value<int>(), Is.GreaterThan(0));
            foreach (var r in json["results"] as JArray)
                StringAssert.StartsWith("Packages/com.singtaa.unity-mcp", r["path"].Value<string>());
        }

        [Test]
        public void Limit_TruncatesAndReportsTotal() {
            var result = Call(new JObject { ["query"] = "t:Script", ["limit"] = 1 });

            Assert.IsFalse(result.isError);
            var json = JObject.Parse(result.content[0].text);
            Assert.AreEqual(1, json["returnedCount"].Value<int>());
            Assert.That(json["totalCount"].Value<int>(), Is.GreaterThan(1));
            Assert.IsTrue(json["truncated"].Value<bool>());
        }

        [Test]
        public void InvalidFolder_ReturnsError() {
            var result = Call(new JObject {
                ["query"] = "t:Script",
                ["folders"] = new JArray("Assets/__DoesNotExist__")
            });

            Assert.IsTrue(result.isError);
            StringAssert.Contains("Invalid folder", result.content[0].text);
        }

        [Test]
        public void MissingQuery_ReturnsError() {
            var result = Call(new JObject());

            Assert.IsTrue(result.isError);
            StringAssert.Contains("query", result.content[0].text);
        }
    }
}
