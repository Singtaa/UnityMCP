using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// MCP tools for reflection-based type inspection and static method invocation.
    /// </summary>
    public static class Tools_Reflection {
        // MARK: SearchTypes
        public static ToolResult SearchTypes(JObject args) {
            var pattern = args.Value<string>("pattern");
            if (string.IsNullOrEmpty(pattern))
                return ToolResultUtil.Text("Missing param: pattern", true);

            var ns = args.Value<string>("namespace");
            var asmFilter = args.Value<string>("assemblyFilter");
            var includeNested = args.Value<bool?>("includeNested") ?? false;
            var maxResults = args.Value<int?>("maxResults") ?? 100;
            maxResults = Mathf.Clamp(maxResults, 1, 500);

            bool exactMatch = pattern.StartsWith("^");
            if (exactMatch) pattern = pattern.Substring(1);

            var results = new List<object>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (!string.IsNullOrEmpty(asmFilter)) {
                    if (!asm.GetName().Name.Contains(asmFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                try {
                    foreach (var type in asm.GetTypes()) {
                        if (results.Count >= maxResults) break;

                        if (!includeNested && type.IsNested) continue;

                        bool nameMatch = exactMatch
                            ? type.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                            : type.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase);

                        if (!nameMatch) continue;

                        if (!string.IsNullOrEmpty(ns) &&
                            (type.Namespace == null ||
                             !type.Namespace.StartsWith(ns, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        results.Add(CreateBriefTypeInfo(type));
                    }
                } catch {
                    // Some assemblies throw on GetTypes()
                }

                if (results.Count >= maxResults) break;
            }

            var response = new { count = results.Count, types = results };
            return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
        }

        // MARK: GetTypeInfo
        public static ToolResult GetTypeInfo(JObject args) {
            var typeName = args.Value<string>("typeName");
            if (string.IsNullOrEmpty(typeName))
                return ToolResultUtil.Text("Missing param: typeName", true);

            var includeInherited = args.Value<bool?>("includeInherited") ?? false;
            var includePrivate = args.Value<bool?>("includePrivate") ?? false;
            var sectionsToken = args["sections"] as JArray;
            var sections = sectionsToken != null
                ? sectionsToken.Select(t => t.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "methods", "properties", "fields" };

            var type = FindType(typeName);
            if (type == null)
                return ToolResultUtil.Text($"Type not found: {typeName}", true);

            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            if (includePrivate) flags |= BindingFlags.NonPublic;
            if (!includeInherited) flags |= BindingFlags.DeclaredOnly;

            var response = new Dictionary<string, object> {
                ["type"] = CreateDetailedTypeInfo(type)
            };

            if (sections.Contains("constructors")) {
                var ctors = type.GetConstructors(flags | BindingFlags.DeclaredOnly);
                response["constructors"] = ctors.Select(c => new {
                    signature = FormatConstructorSignature(c),
                    parameters = c.GetParameters().Select(p => new {
                        name = p.Name,
                        type = FormatTypeName(p.ParameterType),
                        isOptional = p.IsOptional,
                        defaultValue = p.HasDefaultValue ? p.DefaultValue : null
                    }).ToArray(),
                    isPublic = c.IsPublic
                }).ToArray();
            }

            if (sections.Contains("methods")) {
                var methods = type.GetMethods(flags)
                    .Where(m => !m.IsSpecialName) // Exclude property getters/setters
                    .ToArray();
                response["methods"] = methods.Select(m => new {
                    name = m.Name,
                    signature = FormatMethodSignature(m),
                    returnType = FormatTypeName(m.ReturnType),
                    parameters = m.GetParameters().Select(p => new {
                        name = p.Name,
                        type = FormatTypeName(p.ParameterType),
                        isOptional = p.IsOptional
                    }).ToArray(),
                    isStatic = m.IsStatic,
                    isPublic = m.IsPublic,
                    isVirtual = m.IsVirtual,
                    isAbstract = m.IsAbstract,
                    declaringType = m.DeclaringType?.FullName
                }).ToArray();
            }

            if (sections.Contains("properties")) {
                var props = type.GetProperties(flags);
                response["properties"] = props.Select(p => new {
                    name = p.Name,
                    type = FormatTypeName(p.PropertyType),
                    canRead = p.CanRead,
                    canWrite = p.CanWrite,
                    isStatic = (p.GetMethod ?? p.SetMethod)?.IsStatic ?? false,
                    declaringType = p.DeclaringType?.FullName
                }).ToArray();
            }

            if (sections.Contains("fields")) {
                var fields = type.GetFields(flags);
                response["fields"] = fields.Select(f => new {
                    name = f.Name,
                    type = FormatTypeName(f.FieldType),
                    isStatic = f.IsStatic,
                    isReadOnly = f.IsInitOnly,
                    isLiteral = f.IsLiteral,
                    isPublic = f.IsPublic,
                    declaringType = f.DeclaringType?.FullName
                }).ToArray();
            }

            if (sections.Contains("events")) {
                var events = type.GetEvents(flags);
                response["events"] = events.Select(e => new {
                    name = e.Name,
                    eventHandlerType = FormatTypeName(e.EventHandlerType),
                    declaringType = e.DeclaringType?.FullName
                }).ToArray();
            }

            if (sections.Contains("nested")) {
                var nested = type.GetNestedTypes(flags);
                response["nestedTypes"] = nested.Select(t => CreateBriefTypeInfo(t)).ToArray();
            }

            return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
        }

        // MARK: GetMethodInfo
        public static ToolResult GetMethodInfo(JObject args) {
            var typeName = args.Value<string>("typeName");
            var methodName = args.Value<string>("methodName");

            if (string.IsNullOrEmpty(typeName))
                return ToolResultUtil.Text("Missing param: typeName", true);
            if (string.IsNullOrEmpty(methodName))
                return ToolResultUtil.Text("Missing param: methodName", true);

            var includeInherited = args.Value<bool?>("includeInherited") ?? true;

            var type = FindType(typeName);
            if (type == null)
                return ToolResultUtil.Text($"Type not found: {typeName}", true);

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            if (!includeInherited) flags |= BindingFlags.DeclaredOnly;

            var methods = type.GetMethods(flags)
                .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (methods.Length == 0)
                return ToolResultUtil.Text($"Method not found: {methodName}", true);

            var overloads = methods.Select(m => {
                var result = new Dictionary<string, object> {
                    ["signature"] = FormatMethodSignature(m),
                    ["returnType"] = FormatTypeName(m.ReturnType),
                    ["parameters"] = m.GetParameters().Select(p => new {
                        name = p.Name,
                        type = FormatTypeName(p.ParameterType),
                        isOptional = p.IsOptional,
                        hasDefaultValue = p.HasDefaultValue,
                        defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null,
                        isOut = p.IsOut,
                        isRef = p.ParameterType.IsByRef && !p.IsOut
                    }).ToArray(),
                    ["isStatic"] = m.IsStatic,
                    ["isPublic"] = m.IsPublic,
                    ["isPrivate"] = m.IsPrivate,
                    ["isVirtual"] = m.IsVirtual,
                    ["isAbstract"] = m.IsAbstract,
                    ["isFinal"] = m.IsFinal,
                    ["declaringType"] = m.DeclaringType?.FullName
                };

                if (m.IsGenericMethod) {
                    result["isGeneric"] = true;
                    result["genericParameters"] = m.GetGenericArguments().Select(g => g.Name).ToArray();
                    var constraints = new Dictionary<string, List<string>>();
                    foreach (var ga in m.GetGenericArguments()) {
                        var c = ga.GetGenericParameterConstraints();
                        if (c.Length > 0) {
                            constraints[ga.Name] = c.Select(t => FormatTypeName(t)).ToList();
                        }
                    }
                    if (constraints.Count > 0)
                        result["genericConstraints"] = constraints;
                }

                return result;
            }).ToArray();

            var response = new {
                typeName = type.FullName,
                methodName,
                overloadCount = overloads.Length,
                overloads
            };

            return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
        }

        // MARK: GetAssemblies
        public static ToolResult GetAssemblies(JObject args) {
            var filter = args.Value<string>("filter");
            var includeSystem = args.Value<bool?>("includeSystem") ?? false;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => {
                    var name = a.GetName().Name;

                    if (!includeSystem) {
                        if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
                            return false;
                    }

                    if (!string.IsNullOrEmpty(filter)) {
                        if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }

                    return true;
                })
                .Select(a => {
                    var asmName = a.GetName();
                    int typeCount = 0;
                    try { typeCount = a.GetTypes().Length; } catch { }

                    string location = null;
                    try { if (!a.IsDynamic) location = a.Location; } catch { }

                    return new {
                        name = asmName.Name,
                        fullName = asmName.FullName,
                        version = asmName.Version?.ToString(),
                        location,
                        isDynamic = a.IsDynamic,
                        typeCount
                    };
                })
                .OrderBy(a => a.name)
                .ToArray();

            var response = new { count = assemblies.Length, assemblies };
            return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
        }

        // MARK: Decompile
        public static ToolResult Decompile(JObject args) {
            var typeName = args.Value<string>("typeName");
            if (string.IsNullOrEmpty(typeName))
                return ToolResultUtil.Text("Missing param: typeName", true);

            var methodName = args.Value<string>("methodName");
            var offset = args.Value<int?>("offset") ?? 0;
            var limit = args.Value<int?>("limit") ?? 200;
            limit = Mathf.Clamp(limit, 1, 1000);

            var type = FindType(typeName);
            if (type == null)
                return ToolResultUtil.Text($"Type not found: {typeName}", true);

            var assemblyPath = type.Assembly.Location;
            if (string.IsNullOrEmpty(assemblyPath))
                return ToolResultUtil.Text("Assembly has no location (dynamic/in-memory assembly)", true);

            try {
                var settings = new DecompilerSettings {
                    ThrowOnAssemblyResolveErrors = false
                };
                var decompiler = new CSharpDecompiler(assemblyPath, settings);
                string source;

                if (string.IsNullOrEmpty(methodName)) {
                    // Decompile entire type
                    var fullTypeName = new FullTypeName(type.FullName);
                    source = decompiler.DecompileTypeAsString(fullTypeName);
                } else {
                    // Find and decompile specific method
                    source = DecompileMethod(decompiler, type, methodName);
                }

                var lines = source.Split('\n');
                var totalLines = lines.Length;
                var hasMore = offset + limit < totalLines;
                var pageLines = lines.Skip(offset).Take(limit);

                var response = new {
                    typeName = type.FullName,
                    methodName,
                    totalLines,
                    offset,
                    limit,
                    hasMore,
                    source = string.Join("\n", pageLines)
                };

                return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
            } catch (Exception e) {
                return ToolResultUtil.Text($"Decompilation failed: {e.Message}", true);
            }
        }

        static string DecompileMethod(CSharpDecompiler decompiler, Type type, string methodName) {
            // Get all type definitions from the compilation
            var typeDef = decompiler.TypeSystem.FindType(new FullTypeName(type.FullName))
                .GetDefinition();

            if (typeDef == null)
                throw new Exception($"Type definition not found in assembly: {type.FullName}");

            // Find the method(s) with matching name
            var methods = typeDef.Methods
                .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (methods.Count == 0)
                throw new Exception($"Method not found: {methodName}");

            // Decompile all overloads
            var sources = new List<string>();
            foreach (var method in methods) {
                var handle = method.MetadataToken;
                sources.Add(decompiler.DecompileAsString(handle));
            }

            return string.Join("\n\n// --- Overload ---\n\n", sources);
        }

        // MARK: InvokeStatic
        public static ToolResult InvokeStatic(JObject args) {
            var typeName = args.Value<string>("typeName");
            var memberName = args.Value<string>("methodName");
            var isProperty = args.Value<bool?>("isProperty") ?? false;

            if (string.IsNullOrEmpty(typeName))
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    success = false,
                    error = "Missing param: typeName",
                    errorType = "MissingParameter"
                }, Formatting.Indented), true);

            if (string.IsNullOrEmpty(memberName))
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    success = false,
                    error = "Missing param: methodName",
                    errorType = "MissingParameter"
                }, Formatting.Indented), true);

            var type = FindType(typeName);
            if (type == null)
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    success = false,
                    typeName,
                    memberName,
                    error = $"Type not found: {typeName}",
                    errorType = "TypeNotFound"
                }, Formatting.Indented), true);

            try {
                object result;
                Type returnType;
                string memberType;

                if (isProperty) {
                    var prop = type.GetProperty(memberName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    if (prop == null)
                        return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                            success = false,
                            typeName,
                            memberName,
                            error = $"Static property not found: {memberName}",
                            errorType = "PropertyNotFound"
                        }, Formatting.Indented), true);

                    if (!prop.CanRead)
                        return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                            success = false,
                            typeName,
                            memberName,
                            error = "Property has no getter",
                            errorType = "NoGetter"
                        }, Formatting.Indented), true);

                    result = prop.GetValue(null);
                    returnType = prop.PropertyType;
                    memberType = "property";
                } else {
                    var method = type.GetMethod(memberName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                        null, Type.EmptyTypes, null);

                    if (method == null) {
                        // Check if method exists but requires parameters
                        var anyMethod = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            .FirstOrDefault(m => m.Name.Equals(memberName, StringComparison.OrdinalIgnoreCase));

                        if (anyMethod != null) {
                            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                                success = false,
                                typeName,
                                memberName,
                                error = "Method exists but requires parameters. Only parameterless methods are supported.",
                                errorType = "ParametersRequired",
                                hint = $"Method signature: {FormatMethodSignature(anyMethod)}"
                            }, Formatting.Indented), true);
                        }

                        return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                            success = false,
                            typeName,
                            memberName,
                            error = $"Parameterless static method not found: {memberName}",
                            errorType = "MethodNotFound"
                        }, Formatting.Indented), true);
                    }

                    result = method.Invoke(null, null);
                    returnType = method.ReturnType;
                    memberType = "method";
                }

                var isVoid = returnType == typeof(void);
                var serializedResult = isVoid ? null : SerializeResult(result);

                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    success = true,
                    typeName = type.FullName,
                    memberName,
                    memberType,
                    returnType = returnType.FullName,
                    isVoid,
                    result = serializedResult
                }, Formatting.Indented));

            } catch (TargetInvocationException tie) {
                var inner = tie.InnerException ?? tie;
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    success = false,
                    typeName,
                    memberName,
                    error = inner.Message,
                    errorType = inner.GetType().Name,
                    stackTrace = inner.StackTrace
                }, Formatting.Indented), true);
            } catch (Exception e) {
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    success = false,
                    typeName,
                    memberName,
                    error = e.Message,
                    errorType = e.GetType().Name
                }, Formatting.Indented), true);
            }
        }

        // MARK: Helper Methods
        static Type FindType(string typeName) {
            // Try direct lookup first
            var type = Type.GetType(typeName);
            if (type != null) return type;

            // Search all assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    type = asm.GetType(typeName, throwOnError: false);
                    if (type != null) return type;

                    // Try case-insensitive match on full name
                    foreach (var t in asm.GetTypes()) {
                        if (t.FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true)
                            return t;
                    }
                } catch {
                    // Some assemblies throw on GetTypes()
                }
            }
            return null;
        }

        static object CreateBriefTypeInfo(Type type) {
            return new {
                fullName = type.FullName,
                name = type.Name,
                @namespace = type.Namespace,
                assembly = type.Assembly.GetName().Name,
                isClass = type.IsClass && !type.IsValueType,
                isStruct = type.IsValueType && !type.IsEnum,
                isEnum = type.IsEnum,
                isInterface = type.IsInterface,
                isAbstract = type.IsAbstract,
                isSealed = type.IsSealed,
                isGeneric = type.IsGenericType,
                baseType = type.BaseType?.FullName
            };
        }

        static object CreateDetailedTypeInfo(Type type) {
            return new {
                fullName = type.FullName,
                name = type.Name,
                @namespace = type.Namespace,
                assembly = type.Assembly.GetName().Name,
                assemblyVersion = type.Assembly.GetName().Version?.ToString(),
                isClass = type.IsClass && !type.IsValueType,
                isStruct = type.IsValueType && !type.IsEnum,
                isEnum = type.IsEnum,
                isInterface = type.IsInterface,
                isAbstract = type.IsAbstract,
                isSealed = type.IsSealed,
                isGeneric = type.IsGenericType,
                isNested = type.IsNested,
                baseType = type.BaseType?.FullName,
                interfaces = type.GetInterfaces().Select(i => i.FullName).ToArray(),
                genericParameters = type.IsGenericType
                    ? type.GetGenericArguments().Select(g => g.Name).ToArray()
                    : null,
                attributes = type.GetCustomAttributesData()
                    .Select(a => a.AttributeType.Name)
                    .ToArray()
            };
        }

        static string FormatTypeName(Type type) {
            if (type == null) return "void";
            if (type == typeof(void)) return "void";

            // Handle common primitive types
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(object)) return "object";
            if (type == typeof(long)) return "long";
            if (type == typeof(short)) return "short";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(char)) return "char";
            if (type == typeof(decimal)) return "decimal";

            // Handle arrays
            if (type.IsArray) {
                var elementType = type.GetElementType();
                var rank = type.GetArrayRank();
                return FormatTypeName(elementType) + "[" + new string(',', rank - 1) + "]";
            }

            // Handle by-ref types
            if (type.IsByRef) {
                return "ref " + FormatTypeName(type.GetElementType());
            }

            // Handle nullable types
            if (Nullable.GetUnderlyingType(type) != null) {
                return FormatTypeName(Nullable.GetUnderlyingType(type)) + "?";
            }

            // Handle generic types
            if (type.IsGenericType) {
                var baseName = type.Name;
                var tickIndex = baseName.IndexOf('`');
                if (tickIndex > 0) baseName = baseName.Substring(0, tickIndex);

                var args = type.GetGenericArguments()
                    .Select(FormatTypeName);
                return $"{baseName}<{string.Join(", ", args)}>";
            }

            return type.Name;
        }

        static string FormatMethodSignature(MethodInfo method) {
            var returnType = FormatTypeName(method.ReturnType);
            var parameters = string.Join(", ", method.GetParameters()
                .Select(p => $"{FormatTypeName(p.ParameterType)} {p.Name}"));

            var genericSuffix = "";
            if (method.IsGenericMethod) {
                var args = method.GetGenericArguments().Select(g => g.Name);
                genericSuffix = $"<{string.Join(", ", args)}>";
            }

            return $"{returnType} {method.Name}{genericSuffix}({parameters})";
        }

        static string FormatConstructorSignature(ConstructorInfo ctor) {
            var typeName = ctor.DeclaringType?.Name ?? "Unknown";
            var parameters = string.Join(", ", ctor.GetParameters()
                .Select(p => $"{FormatTypeName(p.ParameterType)} {p.Name}"));
            return $"{typeName}({parameters})";
        }

        static object SerializeResult(object result) {
            if (result == null) return null;

            var type = result.GetType();

            // Primitives and strings serialize directly
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return result;

            // Enums as their string representation
            if (type.IsEnum)
                return result.ToString();

            // Unity Objects - return basic info
            if (result is UnityEngine.Object unityObj) {
                return new {
                    type = type.FullName,
                    name = unityObj.name,
                    instanceId = unityObj.GetInstanceID()
                };
            }

            // Arrays and collections
            if (result is System.Collections.IEnumerable enumerable && !(result is string)) {
                var items = new List<object>();
                foreach (var item in enumerable) {
                    items.Add(SerializeResult(item));
                    if (items.Count >= 100) {
                        items.Add("... (truncated)");
                        break;
                    }
                }
                return items;
            }

            // Try JSON serialization for complex objects
            try {
                return JsonConvert.DeserializeObject(
                    JsonConvert.SerializeObject(result, new JsonSerializerSettings {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        MaxDepth = 3
                    })
                );
            } catch {
                // Fallback to string representation
                return new {
                    type = type.FullName,
                    toString = result.ToString()
                };
            }
        }
    }
}
