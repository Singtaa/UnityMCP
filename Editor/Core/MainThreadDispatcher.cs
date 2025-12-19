using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// Dispatches actions from background threads to Unity's main thread.
    ///
    /// MCP tool calls arrive on the TCP background thread but most Unity APIs
    /// (GameObject manipulation, scene operations, etc.) must run on the main thread.
    /// This dispatcher queues actions and executes them during EditorApplication.update.
    ///
    /// CRITICAL: This dispatcher only works in the main Unity Editor process.
    /// AssetImportWorker and other batch mode processes do NOT have a functioning
    /// EditorApplication.update loop, so actions queued in those processes will
    /// NEVER execute, causing tool calls to timeout.
    /// </summary>
    public static class MainThreadDispatcher {
        static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        static bool _installed;
        static long _lastTickUtcTicks;
        static int _mainThreadId;
        static int _queued;
        static int _executed;

        public static bool IsInstalled => _installed;
        public static int MainThreadId => _mainThreadId;
        public static int QueuedCount => Interlocked.CompareExchange(ref _queued, 0, 0);
        public static int ExecutedCount => Interlocked.CompareExchange(ref _executed, 0, 0);

        public static void Install() {
            // Always re-register event handlers after domain reload
            // Domain reloads clear event handlers but preserve static fields
            _installed = true;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            Interlocked.Exchange(ref _lastTickUtcTicks, DateTime.UtcNow.Ticks);

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        static void OnBeforeReload() {
            Shutdown();
        }

        public static void Shutdown() {
            if (!_installed) return;
            _installed = false;

            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;

            while (_queue.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _queued, 0);
        }

        public static void Enqueue(Action action) {
            if (action == null) return;
            if (!_installed) {
                Debug.LogWarning("[UnityMcp] MainThreadDispatcher not installed, action dropped.");
                return;
            }
            _queue.Enqueue(action);
            Interlocked.Increment(ref _queued);
        }

        static void Tick() {
            Interlocked.Exchange(ref _lastTickUtcTicks, DateTime.UtcNow.Ticks);

            var count = 0;
            while (count < 512 && _queue.TryDequeue(out var a)) {
                try {
                    a();
                } catch (Exception e) {
                    Debug.LogException(e);
                }
                Interlocked.Decrement(ref _queued);
                Interlocked.Increment(ref _executed);
                count++;
            }
        }

        public static string GetStatusJson() {
            var now = DateTime.UtcNow;
            var lastTicks = Interlocked.Read(ref _lastTickUtcTicks);

            long ageMs;
            if (lastTicks <= 0) {
                ageMs = -1;
            } else {
                var last = new DateTime(lastTicks, DateTimeKind.Utc);
                ageMs = (long)Math.Max(0, (now - last).TotalMilliseconds);
            }

            return "{"
                   + "\"installed\":" + (_installed ? "true" : "false") + ","
                   + "\"mainThreadId\":" + _mainThreadId + ","
                   + "\"lastTickAgeMs\":" + ageMs + ","
                   + "\"queued\":" + Interlocked.CompareExchange(ref _queued, 0, 0) + ","
                   + "\"executed\":" + Interlocked.CompareExchange(ref _executed, 0, 0)
                   + "}";
        }
    }
}
