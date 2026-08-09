using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;

namespace UnityMcp {
    /// <summary>
    /// Compiles C# source in-memory using a Roslyn compiler bundled inside the Unity
    /// editor installation. All Roslyn access is reflection-based so this package never
    /// hard-binds to a specific Roslyn version (the bundled copies vary by Unity version
    /// and platform). All state is per-domain: a domain reload resets the loaded compiler
    /// and the reference cache.
    /// </summary>
    public static class RoslynCompiler {
        // MARK: Types
        public sealed class Diag {
            public string severity;
            public string id;
            public int line;   // 1-based line in the source passed to Compile (0 if unknown)
            public int column;
            public string message;
        }

        public sealed class CompileResult {
            public bool success;
            public Assembly assembly;
            public List<Diag> diagnostics = new List<Diag>();
            public long compileMs;
        }

        // MARK: Candidates
        // Folders (relative to EditorApplication.applicationContentsPath) that may contain
        // a loadable Roslyn. Probed in order; the first that passes an end-to-end
        // validation compile wins. The msbuild copy comes first: it is a net472 build with
        // every dependency alongside it, which matches the editor's Mono domain. Newer
        // copies (e.g. Unity.Analyzers.Common, Roslyn 4.x) load but fail at emit time
        // because the editor domain's own System.Collections.Immutable wins assembly
        // binding and lacks the span APIs Roslyn 4.x needs: the validation compile
        // catches that and falls through.
        static readonly string[] _candidates = {
            "Resources/Scripting/MonoBleedingEdge/lib/mono/msbuild/Current/bin/Roslyn", // macOS
            "MonoBleedingEdge/lib/mono/msbuild/Current/bin/Roslyn",                     // Windows/Linux
            "Resources/BuildPipeline/Unity.Analyzers.Common",
            "BuildPipeline/Unity.Analyzers.Common",
        };

        // Roslyn's Diagnostic.ToString(): "(line,col): error CS1002: ; expected"
        static readonly Regex _diagPattern = new Regex(
            @"^(?:[^(]*\((\d+),(\d+)\): )?(hidden|info|warning|error)\s+(\S+): (.*)$");

        // MARK: State
        static bool _probed;
        static string _loadError;
        static string _description;
        static string _resolveDir;
        static bool _resolverRegistered;
        static Array _references; // MetadataReference[] - cached because Roslyn caches
                                  // assembly metadata per reference INSTANCE; reusing the
                                  // array is what makes warm compiles ~60ms instead of ~4s.

        static Type _syntaxTreeType;
        static Type _csharpSyntaxTreeType;
        static Type _compilationType;
        static Type _optionsType;
        static Type _outputKindType;
        static Type _metadataRefType;

        /// <summary>Human-readable description of the loaded compiler (null until loaded).</summary>
        public static string Description => _description;

        // MARK: Probe
        /// <summary>
        /// Loads and validates a bundled Roslyn. Returns null when the compiler is ready,
        /// otherwise a message explaining why none could be loaded. Cached per domain.
        /// </summary>
        public static string EnsureAvailable() {
            if (_probed) return _loadError;
            _probed = true;

            var failures = new List<string>();
            foreach (var rel in _candidates) {
                var dir = Path.Combine(EditorApplication.applicationContentsPath, rel);
                if (!File.Exists(Path.Combine(dir, "Microsoft.CodeAnalysis.CSharp.dll")))
                    continue;
                try {
                    LoadCandidate(dir);
                    // End-to-end validation; loading alone is not enough (see _candidates).
                    // Reentrancy note: Compile() calls EnsureAvailable(), which is a no-op
                    // here because _probed is already true and the types are loaded.
                    var probe = Compile(
                        "public static class __RoslynProbe { public static int Run() { return 42; } }",
                        "__RoslynProbe");
                    if (!probe.success)
                        throw new Exception("validation compile failed: " +
                            string.Join("; ", probe.diagnostics.Select(d => d.message)));
                    var val = probe.assembly.GetType("__RoslynProbe").GetMethod("Run").Invoke(null, null);
                    if (!Equals(val, 42))
                        throw new Exception($"validation compile returned {val}");

                    _description = $"Roslyn {_compilationType.Assembly.GetName().Version} ({rel})";
                    _loadError = null;
                    return null;
                } catch (Exception e) {
                    var root = e;
                    while (root.InnerException != null) root = root.InnerException;
                    failures.Add($"{rel}: {root.GetType().Name}: {root.Message}");
                    ResetLoaded();
                }
            }

            _loadError = failures.Count == 0
                ? $"No bundled Roslyn found under {EditorApplication.applicationContentsPath}"
                : "No usable bundled Roslyn. Tried: " + string.Join(" | ", failures);
            return _loadError;
        }

        // MARK: Compile
        /// <summary>
        /// Compiles <paramref name="source"/> into an in-memory assembly referencing every
        /// non-dynamic assembly in the current domain. The loaded assembly persists until
        /// the next domain reload.
        /// </summary>
        public static CompileResult Compile(string source, string assemblyName) {
            var result = new CompileResult();
            var loadError = EnsureAvailable();
            if (loadError != null) {
                result.diagnostics.Add(new Diag { severity = "error", id = "MCP", message = loadError });
                return result;
            }

            var sw = Stopwatch.StartNew();

            var tree = CallStatic(_csharpSyntaxTreeType, "ParseText", typeof(string), new object[] { source });
            var treeArr = Array.CreateInstance(_syntaxTreeType, 1);
            treeArr.SetValue(tree, 0);

            var optCtor = _optionsType.GetConstructors()
                .Where(k => {
                    var ps = k.GetParameters();
                    return ps.Length > 0 && ps[0].ParameterType == _outputKindType &&
                        ps.Skip(1).All(p => p.IsOptional);
                })
                .OrderByDescending(k => k.GetParameters().Length).First();
            var optArgs = optCtor.GetParameters().Select(DefaultFor).ToArray();
            optArgs[0] = Enum.Parse(_outputKindType, "DynamicallyLinkedLibrary");
            var options = optCtor.Invoke(optArgs);

            var compilation = CallStatic(_compilationType, "Create", typeof(string),
                new object[] { assemblyName, treeArr, GetReferences(), options });

            using (var ms = new MemoryStream()) {
                var emitM = compilation.GetType().GetMethods()
                    .Where(m => {
                        var ps = m.GetParameters();
                        return m.Name == "Emit" && ps.Length > 0 &&
                            ps[0].ParameterType == typeof(Stream) &&
                            ps.Skip(1).All(p => p.IsOptional);
                    })
                    .OrderBy(m => m.GetParameters().Length).First();
                var emitArgs = emitM.GetParameters().Select(DefaultFor).ToArray();
                emitArgs[0] = ms;
                var emitResult = emitM.Invoke(compilation, emitArgs);

                result.success = (bool)emitResult.GetType().GetProperty("Success").GetValue(emitResult);
                var diags = (System.Collections.IEnumerable)emitResult.GetType()
                    .GetProperty("Diagnostics").GetValue(emitResult);
                foreach (var d in diags)
                    result.diagnostics.Add(ParseDiagnostic(d.ToString()));

                if (result.success)
                    result.assembly = Assembly.Load(ms.ToArray());
            }

            result.compileMs = sw.ElapsedMilliseconds;
            return result;
        }

        // MARK: Loading
        static void LoadCandidate(string dir) {
            _resolveDir = dir;
            if (!_resolverRegistered) {
                // Roslyn's dependencies (System.Reflection.Metadata, etc.) and satellite
                // assemblies resolve from the chosen candidate folder, and only from there.
                AppDomain.CurrentDomain.AssemblyResolve += (s, args) => {
                    if (_resolveDir == null) return null;
                    var name = new AssemblyName(args.Name).Name;
                    var p = Path.Combine(_resolveDir, name + ".dll");
                    return File.Exists(p) ? Assembly.LoadFrom(p) : null;
                };
                _resolverRegistered = true;
            }

            var ca = Assembly.LoadFrom(Path.Combine(dir, "Microsoft.CodeAnalysis.dll"));
            var cs = Assembly.LoadFrom(Path.Combine(dir, "Microsoft.CodeAnalysis.CSharp.dll"));

            _syntaxTreeType = ca.GetType("Microsoft.CodeAnalysis.SyntaxTree", true);
            _metadataRefType = ca.GetType("Microsoft.CodeAnalysis.MetadataReference", true);
            _outputKindType = ca.GetType("Microsoft.CodeAnalysis.OutputKind", true);
            _csharpSyntaxTreeType = cs.GetType("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree", true);
            _compilationType = cs.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation", true);
            _optionsType = cs.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions", true);
        }

        static void ResetLoaded() {
            _resolveDir = null;
            _syntaxTreeType = _csharpSyntaxTreeType = _compilationType = null;
            _optionsType = _outputKindType = _metadataRefType = null;
            // References hold MetadataReference instances from the failed candidate's
            // assembly; they cannot be fed to a different Roslyn.
            _references = null;
            _description = null;
        }

        static Array GetReferences() {
            if (_references != null) return _references;
            var paths = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && File.Exists(a.Location))
                .Select(a => a.Location).Distinct().ToArray();
            var arr = Array.CreateInstance(_metadataRefType, paths.Length);
            for (int i = 0; i < paths.Length; i++)
                arr.SetValue(CallStatic(_metadataRefType, "CreateFromFile", typeof(string),
                    new object[] { paths[i] }), i);
            _references = arr;
            return arr;
        }

        // MARK: Reflection Helpers
        // Overload selection by (name, first parameter type), remaining parameters filled
        // with their declared defaults. Keeps us compatible across Roslyn API revisions
        // that append optional parameters.
        static object CallStatic(Type type, string name, Type firstParamType, object[] leading) {
            var m = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(x => {
                    var ps = x.GetParameters();
                    return x.Name == name && ps.Length >= leading.Length &&
                        ps[0].ParameterType == firstParamType &&
                        ps.Skip(leading.Length).All(p => p.IsOptional);
                })
                .OrderBy(x => x.GetParameters().Length).FirstOrDefault();
            if (m == null)
                throw new MissingMethodException($"{type.Name}.{name}({firstParamType.Name}, ...)");
            var args = m.GetParameters().Select(DefaultFor).ToArray();
            for (int i = 0; i < leading.Length; i++) args[i] = leading[i];
            return m.Invoke(null, args);
        }

        static object DefaultFor(ParameterInfo pi) {
            if (pi.HasDefaultValue && pi.DefaultValue != null) return pi.DefaultValue;
            var t = pi.ParameterType;
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }

        static Diag ParseDiagnostic(string formatted) {
            var m = _diagPattern.Match(formatted);
            if (!m.Success)
                return new Diag { severity = "error", id = "", message = formatted };
            return new Diag {
                line = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0,
                column = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0,
                severity = m.Groups[3].Value,
                id = m.Groups[4].Value,
                message = m.Groups[5].Value
            };
        }
    }
}
