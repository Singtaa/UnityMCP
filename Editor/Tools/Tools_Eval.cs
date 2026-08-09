using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcp {
    /// <summary>
    /// MCP tool that compiles and executes a C# snippet in the editor via the bundled
    /// Roslyn compiler (see RoslynCompiler): no domain reload, runs on the main thread.
    ///
    /// Snippets are wrapped in a generated static class. The snippet is first tried as an
    /// expression (its value becomes the result); if that fails to compile it is retried
    /// as a statement body, where an explicit `return` produces the result. Leading
    /// `using` directives are hoisted out of the snippet; common Unity/System namespaces
    /// are imported by default.
    /// </summary>
    public static class Tools_Eval {
        static int _counter;

        static readonly Regex _usingLine = new Regex(
            @"^\s*using\s+(?:static\s+)?[A-Za-z_@][A-Za-z0-9_.]*(?:\s*=\s*[A-Za-z_@][A-Za-z0-9_.<>,\s]*)?\s*;\s*$");

        // Aliases resolve the System vs UnityEngine ambiguity (Object, Random) in favor
        // of Unity, matching what an unqualified reference means in a normal Unity script.
        static readonly string[] _defaultUsings = {
            "using System;",
            "using System.Linq;",
            "using System.Collections.Generic;",
            "using UnityEngine;",
            "using UnityEditor;",
            "using Object = UnityEngine.Object;",
            "using Random = UnityEngine.Random;",
        };

        // MARK: Eval
        public static ToolResult Eval(JObject args) {
            var code = args.Value<string>("code");
            if (string.IsNullOrEmpty(code))
                return Error(new { success = false, error = "Missing param: code", errorType = "MissingParameter" });

            var loadError = RoslynCompiler.EnsureAvailable();
            if (loadError != null)
                return Error(new {
                    success = false,
                    error = loadError,
                    errorType = "CompilerUnavailable",
                    hint = "unity_eval needs a Roslyn bundled with the editor; fall back to unity_project_write_text + unity_scripts_recompile."
                });

            SplitUsings(code, out var usings, out var body, out var bodyStartInSnippet);
            var name = "__UnityEvalAsm" + (++_counter);

            // Expression-first: capture the value whenever the snippet can be read as one
            // (works even for expressions containing statement lambdas). Fall back to a
            // statement body, where only an explicit `return` produces a value.
            var form = "expression";
            var bodyLineInGenerated = 0;
            var compiled = RoslynCompiler.Compile(
                Generate(usings, body, true, out bodyLineInGenerated), name);
            if (!compiled.success) {
                var stmtCompiled = RoslynCompiler.Compile(
                    Generate(usings, body, false, out var stmtBodyLine), name + "_s");
                if (stmtCompiled.success) {
                    compiled = stmtCompiled;
                    form = "statements";
                    bodyLineInGenerated = stmtBodyLine;
                } else {
                    // Statement-form diagnostics describe multi-statement snippets best.
                    var diags = stmtCompiled.diagnostics
                        .Where(d => d.severity == "error")
                        .Select(d => new {
                            d.severity, d.id,
                            line = MapLine(d.line, stmtBodyLine, bodyStartInSnippet),
                            d.column, d.message
                        }).ToArray();
                    return Error(new {
                        success = false,
                        error = "C# compilation failed",
                        errorType = "CompilationFailed",
                        diagnostics = diags,
                        compiler = RoslynCompiler.Description
                    });
                }
            }

            object result;
            var sw = Stopwatch.StartNew();
            try {
                result = compiled.assembly.GetType("__UnityEval").GetMethod("Run").Invoke(null, null);
            } catch (TargetInvocationException tie) {
                var inner = tie.InnerException ?? tie;
                return Error(new {
                    success = false,
                    error = inner.Message,
                    errorType = "RuntimeException",
                    exceptionType = inner.GetType().FullName,
                    stackTrace = inner.StackTrace,
                    form,
                    compileMs = compiled.compileMs
                });
            }
            sw.Stop();

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                success = true,
                result = ResultSerializer.Serialize(result),
                resultType = result?.GetType().FullName,
                isNull = result == null,
                form,
                compileMs = compiled.compileMs,
                execMs = sw.ElapsedMilliseconds
            }, Formatting.Indented));
        }

        // MARK: Snippet Wrapping
        internal static void SplitUsings(string code, out List<string> usings, out string body,
            out int bodyStartInSnippet) {
            usings = new List<string>();
            var lines = code.Replace("\r\n", "\n").Split('\n');
            int i = 0;
            for (; i < lines.Length; i++) {
                var line = lines[i];
                if (_usingLine.IsMatch(line)) { usings.Add(line.Trim()); continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.TrimStart().StartsWith("//")) continue;
                break;
            }
            bodyStartInSnippet = i + 1; // 1-based snippet line where the body begins
            body = string.Join("\n", lines.Skip(i));
        }

        internal static string Generate(List<string> userUsings, string body, bool asExpression,
            out int bodyLineInGenerated) {
            var sb = new StringBuilder();
            int headerLines = 0;
            foreach (var u in userUsings) { sb.AppendLine(u); headerLines++; }
            foreach (var d in _defaultUsings) {
                if (IsCovered(userUsings, d)) continue;
                sb.AppendLine(d);
                headerLines++;
            }
            sb.AppendLine("public static class __UnityEval {");
            sb.AppendLine("    public static object Run() {");
            // 0162: unreachable `return null` after a user `return`/`throw`
            // 0219: assigned-but-unused locals, common in quick snippets
            sb.AppendLine("#pragma warning disable 0162, 0219");
            bodyLineInGenerated = headerLines + 4;

            if (asExpression) {
                var expr = body.TrimEnd();
                while (expr.EndsWith(";")) expr = expr.Substring(0, expr.Length - 1).TrimEnd();
                sb.AppendLine($"return ({expr});");
            } else {
                sb.AppendLine(body);
                sb.AppendLine("return null;");
            }

            sb.AppendLine("#pragma warning restore 0162, 0219");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // MARK: Helpers
        static bool IsCovered(List<string> userUsings, string defaultUsing) {
            var eq = defaultUsing.IndexOf('=');
            if (eq >= 0) {
                // alias: skip if the user already defines the same alias name
                var alias = defaultUsing.Substring("using ".Length, eq - "using ".Length).Trim();
                return userUsings.Any(u => Regex.IsMatch(u, $@"^using\s+{Regex.Escape(alias)}\s*="));
            }
            var normalized = Regex.Replace(defaultUsing, @"\s+", " ");
            return userUsings.Any(u => Regex.Replace(u, @"\s+", " ") == normalized);
        }

        static int MapLine(int generatedLine, int bodyLineInGenerated, int bodyStartInSnippet) {
            if (generatedLine <= 0) return 0;
            var mapped = generatedLine - bodyLineInGenerated + bodyStartInSnippet;
            return mapped > 0 ? mapped : generatedLine;
        }

        static ToolResult Error(object payload) {
            return ToolResultUtil.Text(JsonConvert.SerializeObject(payload, Formatting.Indented), true);
        }
    }
}
