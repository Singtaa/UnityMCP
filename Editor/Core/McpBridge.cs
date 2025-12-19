using System;
using UnityEditor;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// Main entry point and orchestrator for the Unity MCP Server.
    /// Handles lifecycle, Node.js server management, and TCP client connection.
    ///
    /// CRITICAL: Unity runs multiple processes that all execute [InitializeOnLoad] code:
    /// - Main Editor process (the one we want)
    /// - AssetImportWorker0, AssetImportWorker1, etc. (background import processes)
    ///
    /// The MCP bridge must ONLY run in the main editor process.
    /// </summary>
    [InitializeOnLoad]
    public static class McpBridge {
        static volatile bool _started;
        static readonly object _startLock = new object();
        static McpTcpClient _client;
        static DateTime _startTime;

        public static bool IsStarted => _started;
        public static bool IsConnected => _client?.IsConnected ?? false;
        public static string ClientId => _client?.ClientId;
        public static int TotalCalls => _client?.TotalCalls ?? 0;
        public static TimeSpan Uptime => _started ? DateTime.UtcNow - _startTime : TimeSpan.Zero;

        static McpBridge() {
            // CRITICAL: Don't run in background processes like AssetImportWorker!
            if (IsBackgroundProcess()) {
                return;
            }

            // Start immediately - we're already on main thread during domain reload
            try {
                EnsureStarted();
            } catch (Exception e) {
                Debug.LogException(e);
            }
        }

        public static async void EnsureStarted() {
            lock (_startLock) {
                if (_started && _client != null) return;

                _started = true;
                _startTime = DateTime.UtcNow;

                AppDomain.CurrentDomain.DomainUnload -= OnDomainUnload;
                AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;
                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

                MainThreadDispatcher.Install();
                ConsoleCapture.EnsureStarted();
                ToolRegistry.EnsureInitialized();
                ResourceRegistry.EnsureInitialized();

                // Clean up stale lock files
                McpTcpClient.CleanupStaleLockFiles();

                try { _client?.Dispose(); } catch { }
            }

            // Start Node.js server if auto-start is enabled
            // This also detects if server survived domain reload
            if (McpSettings.AutoStart) {
                var serverStarted = await NodeProcessManager.EnsureServerRunning();
                if (!serverStarted) {
                    Debug.LogWarning("[UnityMcp] Failed to start Node.js server. MCP bridge will wait for manual server start.");
                }
            } else {
                // Even if auto-start is disabled, check if server is running (survived domain reload)
                await NodeProcessManager.CheckServerReachable();
            }

            // Connect TCP client if auto-connect is enabled
            if (McpSettings.AutoConnect) {
                lock (_startLock) {
                    // Subscribe to server unreachable event
                    McpTcpClient.OnServerUnreachable -= OnServerUnreachable;
                    McpTcpClient.OnServerUnreachable += OnServerUnreachable;

                    _client = new McpTcpClient(McpSettings.Host, McpSettings.IpcPort);
                    _client.Start();
                }
            }
        }

        static async void OnServerUnreachable() {
            // Server might have died - run health check and possibly restart
            var stillRunning = await NodeProcessManager.HealthCheck();
            if (!stillRunning && McpSettings.AutoStart) {
                if (McpSettings.VerboseLogging) Debug.Log("[UnityMcp] Server died, attempting restart...");
                await NodeProcessManager.EnsureServerRunning();
            }
        }

        static void OnBeforeAssemblyReload() {
            Shutdown(keepServer: true);
        }

        static void OnDomainUnload(object sender, EventArgs e) {
            Shutdown(keepServer: true);
        }

        public static void Shutdown(bool keepServer = false) {
            if (!_started) return;
            _started = false;

            AppDomain.CurrentDomain.DomainUnload -= OnDomainUnload;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;

            try { _client?.Dispose(); } catch { }
            _client = null;

            try { ConsoleCapture.Shutdown(); } catch { }
            try { MainThreadDispatcher.Shutdown(); } catch { }

            if (!keepServer) {
                NodeProcessManager.StopServer();
            }
        }

        public static void Restart() {
            Shutdown(keepServer: false);
            EnsureStarted();
        }

        public static void Reconnect() {
            lock (_startLock) {
                try { _client?.Dispose(); } catch { }
                McpTcpClient.CleanupStaleLockFiles();
                _client = new McpTcpClient(McpSettings.Host, McpSettings.IpcPort);
                _client.Start();
            }
        }

        /// <summary>
        /// Detects if we're running in a background/worker Unity process.
        /// </summary>
        static bool IsBackgroundProcess() {
            if (Application.isBatchMode) return true;

            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args) {
                if (arg == "-batchMode") return true;
                if (arg.Contains("AssetImport")) return true;
                if (arg.Contains("Worker")) return true;
            }

            return false;
        }
    }
}
