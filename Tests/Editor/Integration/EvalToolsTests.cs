using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.Tests {
    [TestFixture]
    public class EvalToolsTests {
        [OneTimeSetUp]
        public void RequireCompiler() {
            var loadError = RoslynCompiler.EnsureAvailable();
            if (loadError != null)
                Assert.Ignore($"No bundled Roslyn available on this editor install: {loadError}");
        }

        static JObject Run(string code) {
            var result = Tools_Eval.Eval(new JObject { ["code"] = code });
            return JObject.Parse(result.content[0].text);
        }

        // MARK: Expression Form
        [Test]
        public void Expression_ReturnsValue() {
            var json = Run("1 + 1");

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.AreEqual(2, json["result"].Value<int>());
            Assert.AreEqual("expression", json["form"].Value<string>());
        }

        [Test]
        public void Expression_TrailingSemicolon_StillExpression() {
            var json = Run("21 * 2;");

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.AreEqual(42, json["result"].Value<int>());
            Assert.AreEqual("expression", json["form"].Value<string>());
        }

        [Test]
        public void Expression_WithStatementLambda_ReturnsValue() {
            // Internal semicolons inside a lambda must not force statement form
            var json = Run("new int[] { 1, 2, 3 }.Select(x => { return x * 2; }).Sum()");

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.AreEqual(12, json["result"].Value<int>());
            Assert.AreEqual("expression", json["form"].Value<string>());
        }

        [Test]
        public void Expression_Null_ReportsIsNull() {
            var json = Run("null");

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.IsTrue(json["isNull"].Value<bool>());
        }

        // MARK: Statement Form
        [Test]
        public void Statements_WithReturn_ReturnsValue() {
            var json = Run("var x = 40; return x + 2;");

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.AreEqual(42, json["result"].Value<int>());
            Assert.AreEqual("statements", json["form"].Value<string>());
        }

        [Test]
        public void Statements_WithoutReturn_ReturnsNull() {
            var json = Run("var unused = new System.Text.StringBuilder(); unused.Append(1);");

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.IsTrue(json["isNull"].Value<bool>());
        }

        // MARK: Usings
        [Test]
        public void DefaultUsings_LinqAvailable() {
            var json = Run("Enumerable.Range(1, 10).Sum()");

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.AreEqual(55, json["result"].Value<int>());
        }

        [Test]
        public void UsingHoisting_Works() {
            var json = Run("using System.Text;\nvar sb = new StringBuilder();\nsb.Append(\"hi\");\nreturn sb.ToString();");

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.AreEqual("hi", json["result"].Value<string>());
        }

        [Test]
        public void ObjectAlias_ResolvesToUnityEngine() {
            // With both System and UnityEngine imported, unqualified Object must mean
            // UnityEngine.Object (via the generated alias), not System.Object.
            var json = Run(
                "var go = new GameObject(\"__EvalAliasTest\");\n" +
                "var found = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length > 0;\n" +
                "Object.DestroyImmediate(go);\n" +
                "return found;");

            Assert.IsTrue(json["success"].Value<bool>(), json.ToString());
            Assert.IsTrue(json["result"].Value<bool>());
        }

        // MARK: Unity API
        [Test]
        public void UnityApi_CreateAndDestroyGameObject() {
            var json = Run(
                "var go = new GameObject(\"__EvalApiTest\");\n" +
                "var name = go.name;\n" +
                "Object.DestroyImmediate(go);\n" +
                "return name;");

            Assert.IsTrue(json["success"].Value<bool>(), json.ToString());
            Assert.AreEqual("__EvalApiTest", json["result"].Value<string>());
        }

        [Test]
        public void UnityObjectResult_SerializedWithTypeAndName() {
            var json = Run(
                "var go = new GameObject(\"__EvalReturnTest\");\n" +
                "EditorApplication.delayCall += () => { if (go != null) Object.DestroyImmediate(go); };\n" +
                "return go;");

            Assert.IsTrue(json["success"].Value<bool>(), json.ToString());
            Assert.AreEqual("UnityEngine.GameObject", json["result"]["type"].Value<string>());
            Assert.AreEqual("__EvalReturnTest", json["result"]["name"].Value<string>());

            // Clean up without waiting for delayCall
            var leftover = GameObject.Find("__EvalReturnTest");
            if (leftover != null) Object.DestroyImmediate(leftover);
        }

        // MARK: Errors
        [Test]
        public void CompileError_ReturnsDiagnostics() {
            var result = Tools_Eval.Eval(new JObject { ["code"] = "this is not C# at all" });
            var json = JObject.Parse(result.content[0].text);

            Assert.IsTrue(result.isError);
            Assert.IsFalse(json["success"].Value<bool>());
            Assert.AreEqual("CompilationFailed", json["errorType"].Value<string>());
            var diags = json["diagnostics"] as JArray;
            Assert.IsNotNull(diags);
            Assert.That(diags.Count, Is.GreaterThan(0));
            Assert.AreEqual("error", diags[0]["severity"].Value<string>());
        }

        [Test]
        public void RuntimeException_ReportsInnerException() {
            var result = Tools_Eval.Eval(new JObject {
                ["code"] = "throw new System.InvalidOperationException(\"boom\");"
            });
            var json = JObject.Parse(result.content[0].text);

            Assert.IsTrue(result.isError);
            Assert.IsFalse(json["success"].Value<bool>());
            Assert.AreEqual("RuntimeException", json["errorType"].Value<string>());
            Assert.AreEqual("System.InvalidOperationException", json["exceptionType"].Value<string>());
            Assert.AreEqual("boom", json["error"].Value<string>());
        }

        [Test]
        public void MissingCode_ReturnsError() {
            var result = Tools_Eval.Eval(new JObject());
            var json = JObject.Parse(result.content[0].text);

            Assert.IsTrue(result.isError);
            Assert.AreEqual("MissingParameter", json["errorType"].Value<string>());
        }

        // MARK: Performance Contract
        [Test]
        public void WarmCompile_ReportsTimings() {
            Run("1"); // ensure warm
            var json = Run("2 + 3");

            Assert.IsTrue(json["success"].Value<bool>());
            Assert.IsNotNull(json["compileMs"]);
            Assert.IsNotNull(json["execMs"]);
            // Warm compiles were ~62ms in the spike; 2000ms is a generous regression bound
            Assert.That(json["compileMs"].Value<long>(), Is.LessThan(2000));
        }
    }
}
