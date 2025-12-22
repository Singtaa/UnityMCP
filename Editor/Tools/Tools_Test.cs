using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// MCP tools for running Unity tests (EditMode and PlayMode) and retrieving results.
    /// </summary>
    public static class Tools_Test {
        // Stores test results from the last run
        static TestRunState _lastRunState;
        static readonly object _stateLock = new object();

        // Domain reload tracking - tests aren't ready immediately after reload
        static double _lastDomainReloadTime;
        const double DomainReloadStabilizationSeconds = 1.0;

        [InitializeOnLoadMethod]
        static void OnDomainReload() {
            _lastDomainReloadTime = EditorApplication.timeSinceStartup;
        }

        static bool IsTestFrameworkStabilizing() {
            return EditorApplication.timeSinceStartup - _lastDomainReloadTime < DomainReloadStabilizationSeconds;
        }

        // MARK: Tool Handlers

        /// <summary>
        /// Lists all available tests in the project.
        /// </summary>
        public static ToolResult ListTests(JObject args) {
            if (EditorApplication.isCompiling) {
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    status = "compiling",
                    message = "Cannot list tests while compiling. Please wait and retry.",
                    count = 0,
                    tests = Array.Empty<object>()
                }, Formatting.Indented), true);
            }

            if (IsTestFrameworkStabilizing()) {
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    status = "stabilizing",
                    message = "Test framework is stabilizing after domain reload. Please wait ~1 second and retry.",
                    count = 0,
                    tests = Array.Empty<object>()
                }, Formatting.Indented), true);
            }

            var testModeStr = args.Value<string>("testMode")?.ToLowerInvariant() ?? "all";
            var nameFilter = args.Value<string>("nameFilter");

            try {
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                var results = new List<object>();
                bool receivedCallback = false;

                if (testModeStr == "all" || testModeStr == "editmode") {
                    var editModeTests = GetTestList(api, TestMode.EditMode, nameFilter, out bool editCallback);
                    receivedCallback |= editCallback;
                    results.AddRange(editModeTests);
                }

                if (testModeStr == "all" || testModeStr == "playmode") {
                    var playModeTests = GetTestList(api, TestMode.PlayMode, nameFilter, out bool playCallback);
                    receivedCallback |= playCallback;
                    results.AddRange(playModeTests);
                }

                ScriptableObject.DestroyImmediate(api);

                // If callback wasn't invoked, the test framework may not be ready
                // Note: Unity 6000.x may have issues with RetrieveTestList - test running still works
                if (!receivedCallback) {
                    return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                        status = "not_ready",
                        message = "Test framework did not respond. This can happen right after domain reload.",
                        hint = "You can still run tests using unity.test.run without a filter - the test count will be available in the results.",
                        count = 0,
                        tests = Array.Empty<object>()
                    }, Formatting.Indented), true);
                }

                var response = new {
                    status = "ok",
                    count = results.Count,
                    tests = results
                };

                return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
            } catch (Exception e) {
                return ToolResultUtil.Text($"Failed to list tests: {e.Message}", true);
            }
        }

        /// <summary>
        /// Runs tests and waits for completion before returning results.
        /// </summary>
        public static ToolResult RunTests(JObject args) {
            var testModeStr = args.Value<string>("testMode")?.ToLowerInvariant() ?? "editmode";
            var testFilter = args.Value<string>("testFilter");
            var categoryFilter = args.Value<string>("categoryFilter");
            var assemblyFilter = args.Value<string>("assemblyFilter");

            if (EditorApplication.isCompiling) {
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    status = "compiling",
                    message = "Cannot run tests while compiling. Please wait and retry."
                }, Formatting.Indented), true);
            }

            if (IsTestFrameworkStabilizing()) {
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    status = "stabilizing",
                    message = "Test framework is stabilizing after domain reload. Please wait ~1 second and retry."
                }, Formatting.Indented), true);
            }

            try {
                // Determine test mode
                TestMode testMode;
                if (testModeStr == "playmode") {
                    testMode = TestMode.PlayMode;
                } else if (testModeStr == "editmode") {
                    testMode = TestMode.EditMode;
                } else if (testModeStr == "all") {
                    testMode = TestMode.EditMode | TestMode.PlayMode;
                } else {
                    return ToolResultUtil.Text($"Invalid testMode: {testModeStr}. Use 'editmode', 'playmode', or 'all'.", true);
                }

                // Create state for this run
                var state = new TestRunState {
                    runId = Guid.NewGuid().ToString(),
                    isRunning = true,
                    startTime = DateTime.UtcNow,
                    results = new List<TestResultInfo>()
                };

                lock (_stateLock) {
                    _lastRunState = state;
                }

                var api = ScriptableObject.CreateInstance<TestRunnerApi>();

                // Build filter
                var filter = new Filter {
                    testMode = testMode
                };

                // If testFilter is provided, resolve partial names to full test names
                if (!string.IsNullOrEmpty(testFilter)) {
                    var resolvedNames = ResolveTestNames(api, testMode, testFilter);
                    if (resolvedNames.Length == 0) {
                        ScriptableObject.DestroyImmediate(api);
                        return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                            status = "no_match",
                            message = $"No tests found matching filter: {testFilter}",
                            hint = "Use unity.test.list to see available tests. Filter matches against test name or full name (case-insensitive substring)."
                        }, Formatting.Indented), true);
                    }
                    filter.testNames = resolvedNames;
                    Debug.Log($"[McpBridge] Test filter '{testFilter}' resolved to {resolvedNames.Length} tests: {string.Join(", ", resolvedNames.Take(5))}{(resolvedNames.Length > 5 ? "..." : "")}");
                }

                if (!string.IsNullOrEmpty(categoryFilter)) {
                    filter.categoryNames = categoryFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()).ToArray();
                }

                if (!string.IsNullOrEmpty(assemblyFilter)) {
                    filter.assemblyNames = assemblyFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()).ToArray();
                }

                // Register callbacks
                var callbacks = new TestCallbacks(state, api);
                api.RegisterCallbacks(callbacks);

                // Execute tests
                api.Execute(new ExecutionSettings(filter));

                // Return immediately with run ID - user should poll for results
                var testsToRun = filter.testNames?.Length ?? 0;
                var response = new {
                    message = "Test run started.",
                    runId = state.runId,
                    testMode = testModeStr,
                    testsToRun = testsToRun,
                    note = testsToRun == 0
                        ? "Running all tests (exact count available in getResults after run starts)."
                        : null,
                    hint = "Use unity.test.getResults with this runId to check status and get results."
                };

                return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
            } catch (Exception e) {
                return ToolResultUtil.Text($"Failed to run tests: {e.Message}", true);
            }
        }

        /// <summary>
        /// Gets results from the last test run.
        /// </summary>
        public static ToolResult GetResults(JObject args) {
            var runId = args.Value<string>("runId");

            lock (_stateLock) {
                if (_lastRunState == null) {
                    return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                        status = "no_run",
                        message = "No test run has been started yet."
                    }, Formatting.Indented));
                }

                // If runId provided, verify it matches
                if (!string.IsNullOrEmpty(runId) && _lastRunState.runId != runId) {
                    return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                        status = "not_found",
                        message = $"Run ID '{runId}' not found. Current run ID: {_lastRunState.runId}"
                    }, Formatting.Indented));
                }

                var response = new {
                    runId = _lastRunState.runId,
                    status = _lastRunState.isRunning ? "running" : "completed",
                    startTime = _lastRunState.startTime.ToString("o"),
                    endTime = _lastRunState.endTime?.ToString("o"),
                    durationMs = _lastRunState.endTime.HasValue
                        ? (long)(_lastRunState.endTime.Value - _lastRunState.startTime).TotalMilliseconds
                        : (long?)(DateTime.UtcNow - _lastRunState.startTime).TotalMilliseconds,
                    summary = new {
                        total = _lastRunState.totalTestCount > 0 ? _lastRunState.totalTestCount : _lastRunState.results.Count,
                        completed = _lastRunState.results.Count,
                        passed = _lastRunState.results.Count(r => r.status == "Passed"),
                        failed = _lastRunState.results.Count(r => r.status == "Failed"),
                        skipped = _lastRunState.results.Count(r => r.status == "Skipped"),
                        inconclusive = _lastRunState.results.Count(r => r.status == "Inconclusive")
                    },
                    results = _lastRunState.results.Select(r => new {
                        name = r.name,
                        fullName = r.fullName,
                        status = r.status,
                        durationMs = r.durationMs,
                        message = r.message,
                        stackTrace = r.stackTrace,
                        testMode = r.testMode
                    }).ToList()
                };

                return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
            }
        }

        /// <summary>
        /// Runs EditMode tests. This starts the test run and returns immediately.
        /// NOTE: Despite the name, this does NOT block. Unity's test callbacks require the editor
        /// update loop to run, making true synchronous execution impossible.
        /// Use unity.test.getResults to poll for completion.
        /// </summary>
        public static ToolResult RunTestsSync(JObject args) {
            var testModeStr = args.Value<string>("testMode")?.ToLowerInvariant() ?? "editmode";
            var testFilter = args.Value<string>("testFilter");
            var categoryFilter = args.Value<string>("categoryFilter");
            var assemblyFilter = args.Value<string>("assemblyFilter");

            if (EditorApplication.isCompiling) {
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    status = "compiling",
                    message = "Cannot run tests while compiling. Please wait and retry."
                }, Formatting.Indented), true);
            }

            if (IsTestFrameworkStabilizing()) {
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    status = "stabilizing",
                    message = "Test framework is stabilizing after domain reload. Please wait ~1 second and retry."
                }, Formatting.Indented), true);
            }

            // PlayMode tests cannot be run synchronously
            if (testModeStr == "playmode" || testModeStr == "all") {
                return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                    status = "invalid_mode",
                    message = "PlayMode tests cannot be run with runSync as they require entering Play Mode. " +
                              "Use unity.test.run instead and poll unity.test.getResults for completion."
                }, Formatting.Indented), true);
            }

            try {
                TestMode testMode = TestMode.EditMode;

                // Create state for this run
                var state = new TestRunState {
                    runId = Guid.NewGuid().ToString(),
                    isRunning = true,
                    startTime = DateTime.UtcNow,
                    results = new List<TestResultInfo>()
                };

                lock (_stateLock) {
                    _lastRunState = state;
                }

                var api = ScriptableObject.CreateInstance<TestRunnerApi>();

                // Build filter
                var filter = new Filter {
                    testMode = testMode
                };

                // If testFilter is provided, resolve partial names to full test names
                if (!string.IsNullOrEmpty(testFilter)) {
                    var resolvedNames = ResolveTestNames(api, testMode, testFilter);
                    if (resolvedNames.Length == 0) {
                        ScriptableObject.DestroyImmediate(api);
                        return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                            status = "no_match",
                            message = $"No tests found matching filter: {testFilter}",
                            hint = "Use unity.test.list to see available tests. Filter matches against test name or full name (case-insensitive substring)."
                        }, Formatting.Indented), true);
                    }
                    filter.testNames = resolvedNames;
                    Debug.Log($"[McpBridge] Test filter '{testFilter}' resolved to {resolvedNames.Length} tests: {string.Join(", ", resolvedNames.Take(5))}{(resolvedNames.Length > 5 ? "..." : "")}");
                }

                if (!string.IsNullOrEmpty(categoryFilter)) {
                    filter.categoryNames = categoryFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()).ToArray();
                }

                if (!string.IsNullOrEmpty(assemblyFilter)) {
                    filter.assemblyNames = assemblyFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()).ToArray();
                }

                // Register callbacks
                var callbacks = new TestCallbacks(state, api);
                api.RegisterCallbacks(callbacks);

                // Execute tests
                api.Execute(new ExecutionSettings(filter));

                // IMPORTANT: Unity's test runner callbacks run on the main thread via EditorApplication.update.
                // We CANNOT block the main thread waiting for results - that would prevent callbacks from running.
                // Instead, we return immediately and let the user poll for results.
                //
                // The "sync" behavior is achieved by the MCP server waiting and polling unity.test.getResults
                // until completion, NOT by blocking here.
                
                var testsToRun = filter.testNames?.Length ?? 0;
                var response = new {
                    status = "started",
                    message = "Test run started. Results will be available via unity.test.getResults.",
                    runId = state.runId,
                    testMode = testModeStr,
                    testsToRun = testsToRun,
                    note = testsToRun == 0
                        ? "Running all EditMode tests (exact count available in getResults after run starts). Poll getResults to check completion."
                        : "EditMode tests typically complete within a few seconds. Poll getResults to check completion."
                };

                return ToolResultUtil.Text(JsonConvert.SerializeObject(response, Formatting.Indented));
            } catch (Exception e) {
                return ToolResultUtil.Text($"Failed to run tests: {e.Message}", true);
            }
        }

        // MARK: Helper Methods

        /// <summary>
        /// Resolves partial test name filters to full test names.
        /// Supports comma-separated partial names that match against test name or full name.
        /// </summary>
        static string[] ResolveTestNames(TestRunnerApi api, TestMode testMode, string testFilter) {
            var filterParts = testFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (filterParts.Length == 0) return Array.Empty<string>();

            var matchedNames = new HashSet<string>();

            // Collect tests from relevant modes
            if ((testMode & TestMode.EditMode) != 0) {
                CollectMatchingTests(api, TestMode.EditMode, filterParts, matchedNames);
            }
            if ((testMode & TestMode.PlayMode) != 0) {
                CollectMatchingTests(api, TestMode.PlayMode, filterParts, matchedNames);
            }

            return matchedNames.ToArray();
        }

        static void CollectMatchingTests(TestRunnerApi api, TestMode mode, string[] filterParts, HashSet<string> matchedNames) {
            var adaptor = new TestNameCollector(filterParts, matchedNames);
            api.RetrieveTestList(mode, adaptor.OnTestListReceived);
        }

        static List<object> GetTestList(TestRunnerApi api, TestMode mode, string nameFilter, out bool receivedCallback) {
            var results = new List<object>();
            var adaptor = new TestListAdaptor(results, mode.ToString(), nameFilter);
            bool[] callbackReceived = { false };  // Use array to allow capture in lambda
            api.RetrieveTestList(mode, (rootTest) => {
                callbackReceived[0] = true;
                adaptor.OnTestListReceived(rootTest);
            });
            receivedCallback = callbackReceived[0];
            return results;
        }

        // MARK: Internal Types

        class TestRunState {
            public string runId;
            public bool isRunning;
            public DateTime startTime;
            public DateTime? endTime;
            public List<TestResultInfo> results;
            public int totalTestCount; // Populated when run starts
        }

        class TestResultInfo {
            public string name;
            public string fullName;
            public string status;
            public double durationMs;
            public string message;
            public string stackTrace;
            public string testMode;
        }

        /// <summary>
        /// Collects full test names that match any of the filter parts.
        /// </summary>
        class TestNameCollector {
            readonly string[] _filterParts;
            readonly HashSet<string> _matchedNames;

            public TestNameCollector(string[] filterParts, HashSet<string> matchedNames) {
                _filterParts = filterParts;
                _matchedNames = matchedNames;
            }

            public void OnTestListReceived(ITestAdaptor rootTest) {
                if (rootTest == null) return;
                CollectMatchingTests(rootTest);
            }

            void CollectMatchingTests(ITestAdaptor test) {
                if (test == null) return;

                // Only check leaf tests (actual test methods, not fixtures)
                if (!test.HasChildren && test.IsSuite == false) {
                    var nameLower = test.Name?.ToLowerInvariant() ?? "";
                    var fullNameLower = test.FullName?.ToLowerInvariant() ?? "";

                    foreach (var filter in _filterParts) {
                        if (nameLower.Contains(filter) || fullNameLower.Contains(filter)) {
                            _matchedNames.Add(test.FullName);
                            break;
                        }
                    }
                }

                // Recurse into children
                if (test.Children != null) {
                    foreach (var child in test.Children) {
                        CollectMatchingTests(child);
                    }
                }
            }
        }

        class TestListAdaptor {
            readonly List<object> _results;
            readonly string _testMode;
            readonly string _nameFilter;

            public TestListAdaptor(List<object> results, string testMode, string nameFilter) {
                _results = results;
                _testMode = testMode;
                _nameFilter = nameFilter?.ToLowerInvariant();
            }

            public void OnTestListReceived(ITestAdaptor rootTest) {
                if (rootTest == null) return;
                CollectTests(rootTest);
            }

            void CollectTests(ITestAdaptor test) {
                if (test == null) return;

                // Only add leaf tests (actual test methods, not fixtures)
                if (!test.HasChildren && test.IsSuite == false) {
                    if (string.IsNullOrEmpty(_nameFilter) ||
                        test.Name.ToLowerInvariant().Contains(_nameFilter) ||
                        test.FullName.ToLowerInvariant().Contains(_nameFilter)) {
                        _results.Add(new {
                            name = test.Name,
                            fullName = test.FullName,
                            testMode = _testMode,
                            categories = test.Categories?.ToArray() ?? Array.Empty<string>()
                        });
                    }
                }

                // Recurse into children
                if (test.Children != null) {
                    foreach (var child in test.Children) {
                        CollectTests(child);
                    }
                }
            }
        }

        class TestCallbacks : ICallbacks {
            readonly TestRunState _state;
            readonly TestRunnerApi _api;

            public TestCallbacks(TestRunState state, TestRunnerApi api) {
                _state = state;
                _api = api;
            }

            public void RunStarted(ITestAdaptor testsToRun) {
                var count = CountLeafTests(testsToRun);
                lock (_stateLock) {
                    _state.totalTestCount = count;
                }
                Debug.Log($"[McpBridge] Test run started: {_state.runId}, {count} tests to run");
            }

            static int CountLeafTests(ITestAdaptor test) {
                if (test == null) return 0;
                if (!test.HasChildren && !test.IsSuite) return 1;

                int count = 0;
                if (test.Children != null) {
                    foreach (var child in test.Children) {
                        count += CountLeafTests(child);
                    }
                }
                return count;
            }

            public void RunFinished(ITestResultAdaptor result) {
                lock (_stateLock) {
                    _state.isRunning = false;
                    _state.endTime = DateTime.UtcNow;
                }

                Debug.Log($"[McpBridge] Test run finished: {_state.runId}");

                // Unregister and cleanup
                try {
                    _api.UnregisterCallbacks(this);
                } catch {
                    // Ignore unregister errors
                }
            }

            public void TestStarted(ITestAdaptor test) {
                // Can be used for progress tracking if needed
            }

            public void TestFinished(ITestResultAdaptor result) {
                if (result == null) return;

                // Only record leaf test results (not fixtures)
                if (result.Test == null || result.Test.IsSuite) return;

                var info = new TestResultInfo {
                    name = result.Test.Name,
                    fullName = result.Test.FullName,
                    status = result.TestStatus.ToString(),
                    durationMs = result.Duration * 1000,
                    message = result.Message,
                    stackTrace = result.StackTrace,
                    testMode = result.Test.TestCaseCount > 0 ? "PlayMode" : "EditMode"
                };

                lock (_stateLock) {
                    _state.results.Add(info);
                }
            }
        }
    }
}
