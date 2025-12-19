using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// Captures Unity console logs for MCP access.
    /// Uses reflection to access Unity's internal console when possible,
    /// falls back to Application.logMessageReceivedThreaded otherwise.
    /// </summary>
    public static class ConsoleCapture {
        // MARK: Fallback
        struct FallbackEntry {
            public LogType type;
            public string condition;
            public string stack;
            public DateTime utc;
        }

        static bool _started;
        static readonly object _lock = new object();
        static readonly List<FallbackEntry> _fallback = new List<FallbackEntry>(512);
        const int MaxFallback = 2000;

        public static void EnsureStarted() {
            if (_started) return;
            _started = true;

            // Unsubscribe first to avoid duplicates after reload
            Application.logMessageReceivedThreaded -= OnLogThreaded;
            Application.logMessageReceivedThreaded += OnLogThreaded;
        }

        public static void Shutdown() {
            if (!_started) return;
            _started = false;

            Application.logMessageReceivedThreaded -= OnLogThreaded;

            lock (_lock) {
                _fallback.Clear();
            }
        }

        static void OnLogThreaded(string condition, string stackTrace, LogType type) {
            if (!_started) return;

            lock (_lock) {
                _fallback.Add(new FallbackEntry {
                    type = type,
                    condition = condition,
                    stack = stackTrace,
                    utc = DateTime.UtcNow
                });

                if (_fallback.Count > MaxFallback) {
                    _fallback.RemoveRange(0, _fallback.Count - MaxFallback);
                }
            }
        }

        public static string GetFallbackText(int maxEntries) {
            List<FallbackEntry> copy;
            lock (_lock) {
                copy = new List<FallbackEntry>(_fallback);
            }

            var take = Math.Min(maxEntries, copy.Count);
            var start = Math.Max(0, copy.Count - take);

            var sb = new StringBuilder();
            sb.AppendLine("source=fallback");
            sb.AppendLine($"count={take}");

            for (var i = start; i < copy.Count; i++) {
                var e = copy[i];
                sb.AppendLine($"[{e.utc:O}] [{e.type}] {e.condition}");
                if (!string.IsNullOrEmpty(e.stack)) {
                    sb.AppendLine(e.stack);
                }
            }

            return sb.ToString();
        }

        // MARK: UnityConsole
        public static bool TryReadUnityConsole(int maxEntries, out string text) {
            text = null;

            try {
                var logEntriesType = FindType("UnityEditor.LogEntries");
                var logEntryType = FindType("UnityEditor.LogEntry");
                if (logEntriesType == null || logEntryType == null) return false;

                var getCount = logEntriesType.GetMethod("GetCount",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var start = logEntriesType.GetMethod("StartGettingEntries",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var end = logEntriesType.GetMethod("EndGettingEntries",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                var getEntry = logEntriesType.GetMethod("GetEntryInternal",
                                   BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                               ?? logEntriesType.GetMethod("GetEntry",
                                   BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                if (getCount == null || getEntry == null) return false;

                var count = (int)getCount.Invoke(null, null);
                if (count <= 0) {
                    text = "source=unity-console\ncount=0";
                    return true;
                }

                var take = Math.Min(maxEntries, count);
                var startIndex = Math.Max(0, count - take);

                var conditionField = logEntryType.GetField("condition",
                                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                     ?? logEntryType.GetField("message",
                                         BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.Instance);

                var stackField = logEntryType.GetField("stacktrace",
                                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 ?? logEntryType.GetField("stackTrace",
                                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                start?.Invoke(null, null);

                var sb = new StringBuilder();
                sb.AppendLine("source=unity-console");
                sb.AppendLine($"count={take}");

                var entry = Activator.CreateInstance(logEntryType);

                for (var i = startIndex; i < count; i++) {
                    getEntry.Invoke(null, new object[] { i, entry });

                    var cond = conditionField?.GetValue(entry) as string ?? "";
                    var stack = stackField?.GetValue(entry) as string ?? "";

                    sb.AppendLine(cond);
                    if (!string.IsNullOrEmpty(stack)) {
                        sb.AppendLine(stack);
                    }
                }

                end?.Invoke(null, null);

                text = sb.ToString();
                return true;
            } catch {
                return false;
            }
        }

        static Type FindType(string fullName) {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies) {
                try {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                } catch {
                }
            }
            return null;
        }
    }
}
