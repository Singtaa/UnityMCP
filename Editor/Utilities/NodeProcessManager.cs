using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityMcp {
    /// <summary>
    /// Manages the Node.js MCP server process lifecycle.
    /// Handles automatic startup, npm install, and graceful shutdown.
    ///
    /// DOMAIN RELOAD HANDLING:
    /// We persist the server PID to EditorPrefs so we can reattach to it after domain reload.
    /// This allows Unity to maintain control of the server process across reloads.
    /// </summary>
    public static class NodeProcessManager {
        static Process _serverProcess;
        static string _serverPath;
        static bool _isStarting;
        static bool _externalServerDetected;  // Server running but not started by us (e.g., manually started)

        const string PidPrefKey = "UnityMcp_ServerPid";

        public static bool IsRunning => (_serverProcess != null && !_serverProcess.HasExited) || _externalServerDetected;
        public static bool IsStarting => _isStarting;
        public static string ServerPath => _serverPath;
        public static bool IsExternalServer => _externalServerDetected && _serverProcess == null;

        public static event Action OnServerStarted;
        public static event Action OnServerStopped;
        public static event Action<string> OnServerOutput;
        public static event Action<string> OnServerError;

        /// <summary>
        /// Try to reattach to a server process that was started before domain reload.
        /// Returns true if successfully reattached.
        /// </summary>
        public static bool TryReattachToProcess() {
            if (_serverProcess != null) return true;  // Already have a process

            var savedPid = EditorPrefs.GetInt(PidPrefKey, -1);
            if (savedPid <= 0) return false;

            try {
                var process = Process.GetProcessById(savedPid);

                // Verify it's actually our Node server (check process name)
                if (process.HasExited) {
                    if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Saved process {savedPid} has exited, clearing PID");
                    ClearSavedPid();
                    return false;
                }

                // Check if it looks like a Node process
                var processName = process.ProcessName.ToLowerInvariant();
                if (!processName.Contains("node")) {
                    if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Process {savedPid} is not Node ({processName}), clearing PID");
                    ClearSavedPid();
                    return false;
                }

                _serverProcess = process;
                _externalServerDetected = false;  // It's OUR process, not external

                // Re-register exit handler
                _serverProcess.EnableRaisingEvents = true;
                _serverProcess.Exited += (s, e) => {
                    if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Server process exited");
                    _serverProcess = null;
                    ClearSavedPid();
                    if (!_externalServerDetected) {
                        OnServerStopped?.Invoke();
                    }
                };

                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Reattached to server process (PID {savedPid})");
                OnServerStarted?.Invoke();
                return true;
            } catch (ArgumentException) {
                // Process with this PID doesn't exist
                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Process {savedPid} no longer exists, clearing PID");
                ClearSavedPid();
                return false;
            } catch (Exception e) {
                if (McpSettings.VerboseLogging) Debug.LogWarning($"[UnityMcp] Failed to reattach to process {savedPid}: {e.Message}");
                ClearSavedPid();
                return false;
            }
        }

        static void SavePid(int pid) {
            EditorPrefs.SetInt(PidPrefKey, pid);
        }

        static void ClearSavedPid() {
            EditorPrefs.DeleteKey(PidPrefKey);
        }

        /// <summary>
        /// Check if the server ports are reachable. Useful for detecting external servers after domain reload.
        /// We check the IPC port since that's what Unity connects to.
        /// </summary>
        public static async Task<bool> CheckServerReachable() {
            // Check IPC port (TCP bridge) - this is what Unity actually connects to
            var ipcReachable = await IsPortInUse(McpSettings.IpcPort);
            if (ipcReachable && !IsRunning) {
                _externalServerDetected = true;
                OnServerStarted?.Invoke();
            } else if (!ipcReachable && _externalServerDetected) {
                if (McpSettings.VerboseLogging) Debug.Log("[UnityMcp] External server no longer reachable, clearing flag");
                _externalServerDetected = false;
                OnServerStopped?.Invoke();
            }
            return ipcReachable;
        }

        /// <summary>
        /// Periodic health check - call this to verify server is still running.
        /// If not, clears the external server flag so we can restart.
        /// </summary>
        public static async Task<bool> HealthCheck() {
            // If we think an external server is running, verify it
            if (_externalServerDetected && _serverProcess == null) {
                var stillReachable = await IsPortInUse(McpSettings.IpcPort);
                if (!stillReachable) {
                    if (McpSettings.VerboseLogging) Debug.Log("[UnityMcp] Health check: external server died, clearing flag");
                    _externalServerDetected = false;
                    OnServerStopped?.Invoke();
                    return false;
                }
            }
            return IsRunning;
        }

        // MARK: Public API
        public static async Task<bool> EnsureServerRunning() {
            // First, try to reattach to a process we started before domain reload
            if (TryReattachToProcess()) {
                return true;
            }

            if (IsRunning) return true;
            if (_isStarting) return false;

            _isStarting = true;

            try {
                // 1. Find Server~ folder
                _serverPath = FindServerFolder();
                if (_serverPath == null) {
                    Debug.LogError("[UnityMcp] Server~ folder not found in package");
                    return false;
                }

                // 2. Check Node.js availability
                if (!await IsNodeInstalled()) {
                    Debug.LogError("[UnityMcp] Node.js not found. Please install Node.js 18+ from https://nodejs.org");
                    return false;
                }

                // 3. Check/install node_modules
                var nodeModulesPath = Path.Combine(_serverPath, "node_modules");
                if (!Directory.Exists(nodeModulesPath)) {
                    Debug.Log("[UnityMcp] Installing dependencies (first run)...");
                    if (!await RunNpmInstall()) {
                        Debug.LogError("[UnityMcp] npm install failed");
                        return false;
                    }
                    Debug.Log("[UnityMcp] Dependencies installed successfully");
                }

                // 4. Check if server already running (survived domain reload or another Unity instance)
                // We check the IPC port since that's what Unity connects to
                if (await IsPortInUse(McpSettings.IpcPort)) {
                    if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Server already running (IPC port {McpSettings.IpcPort} in use)");
                    _externalServerDetected = true;
                    OnServerStarted?.Invoke();
                    return true;
                }

                // 5. Start new server
                return await StartServerAsync();
            } finally {
                _isStarting = false;
            }
        }

        public static void StopServer() {
            if (_serverProcess != null && !_serverProcess.HasExited) {
                try {
                    _serverProcess.Kill();
                    _serverProcess.WaitForExit(1000);
                } catch (Exception e) {
                    Debug.LogWarning($"[UnityMcp] Error stopping server: {e.Message}");
                }
            }

            if (_serverProcess != null) {
                _serverProcess.Dispose();
                _serverProcess = null;
            }

            ClearSavedPid();
            _externalServerDetected = false;
            OnServerStopped?.Invoke();
        }

        public static void RestartServer() {
            StopServer();
            EditorApplication.delayCall += async () => {
                await Task.Delay(500); // Brief delay to ensure port is released
                await EnsureServerRunning();
            };
        }

        // MARK: Internal
        static string FindServerFolder() {
            // Look for Server~ in the package folder
            var packagePath = GetPackagePath();
            if (string.IsNullOrEmpty(packagePath)) return null;

            var serverPath = Path.Combine(packagePath, "Server~");
            if (Directory.Exists(serverPath)) {
                return serverPath;
            }

            return null;
        }

        static string GetPackagePath() {
            // Find the package path by looking for our assembly
            var assembly = typeof(NodeProcessManager).Assembly;
            var assemblyPath = assembly.Location;

            // Navigate up from the assembly location to find the package root
            // Assembly is typically in Library/ScriptAssemblies/
            var projectRoot = ProjectPaths.ProjectRoot;

            // Check Packages folder
            var packagesPath = Path.Combine(projectRoot, "Packages", "com.singtaa.unity-mcp");
            if (Directory.Exists(packagesPath)) return packagesPath;

            // Check Library/PackageCache for installed packages
            var packageCachePath = Path.Combine(projectRoot, "Library", "PackageCache");
            if (Directory.Exists(packageCachePath)) {
                foreach (var dir in Directory.GetDirectories(packageCachePath)) {
                    if (Path.GetFileName(dir).StartsWith("com.singtaa.unity-mcp@")) {
                        return dir;
                    }
                }
            }

            return null;
        }

        static async Task<bool> IsNodeInstalled() {
            try {
                var psi = new ProcessStartInfo {
                    FileName = GetNodeExecutable(),
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi)) {
                    if (process == null) return false;

                    var output = await process.StandardOutput.ReadToEndAsync();
                    process.WaitForExit(5000);

                    if (process.ExitCode == 0) {
                        if (McpSettings.VerboseLogging) {
                            var version = output.Trim();
                            Debug.Log($"[UnityMcp] Found Node.js {version}");
                        }
                        return true;
                    }
                }
            } catch {
                // Node not found
            }

            return false;
        }

        static async Task<bool> RunNpmInstall() {
            try {
                var psi = new ProcessStartInfo {
                    FileName = GetNpmExecutable(),
                    Arguments = "install",
                    WorkingDirectory = _serverPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Ensure PATH includes the node/npm directory
                EnsureNodeInPath(psi);

                using (var process = Process.Start(psi)) {
                    if (process == null) return false;

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    process.WaitForExit(60000); // 60 second timeout for npm install

                    var output = await outputTask;
                    var error = await errorTask;

                    if (process.ExitCode != 0) {
                        Debug.LogError($"[UnityMcp] npm install failed:\n{error}");
                        return false;
                    }

                    return true;
                }
            } catch (Exception e) {
                Debug.LogError($"[UnityMcp] npm install exception: {e.Message}");
                return false;
            }
        }

        static async Task<bool> IsPortInUse(int port) {
            System.Net.Sockets.TcpClient client = null;
            try {
                client = new System.Net.Sockets.TcpClient();
                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Checking if port {port} is in use...");
                var connectTask = client.ConnectAsync("127.0.0.1", port);
                var timeoutTask = Task.Delay(1000);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == connectTask && client.Connected) {
                    // Connection succeeded - port is in use by a listening server
                    if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Port {port} check: connected successfully, server is running");
                    return true;
                }
                // Timeout - treat as not in use
                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Port {port} check: timeout, no server");
                return false;
            } catch (System.Net.Sockets.SocketException ex) {
                // Connection refused means nothing is listening
                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Port {port} check: {ex.SocketErrorCode}");
                return false;
            } catch (Exception ex) {
                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Port {port} check: exception {ex.GetType().Name}: {ex.Message}");
                return false;
            } finally {
                try { client?.Close(); } catch { }
            }
        }

        static async Task<bool> StartServerAsync() {
            try {
                var psi = new ProcessStartInfo {
                    FileName = GetNodeExecutable(),
                    Arguments = "src/server.js",
                    WorkingDirectory = _serverPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Ensure PATH includes the node directory
                EnsureNodeInPath(psi);

                // Set environment variables
                psi.Environment["MCP_HTTP_PORT"] = McpSettings.HttpPort.ToString();
                psi.Environment["MCP_IPC_PORT"] = McpSettings.IpcPort.ToString();
                psi.Environment["MCP_REQUIRE_AUTH"] = McpSettings.AuthEnabled ? "true" : "false";
                psi.Environment["MCP_TOKEN"] = McpSettings.AuthToken;

                _serverProcess = Process.Start(psi);
                if (_serverProcess == null) {
                    Debug.LogError("[UnityMcp] Failed to start Node.js server process");
                    return false;
                }

                // Save PID for reattachment after domain reload
                SavePid(_serverProcess.Id);
                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Started server process (PID {_serverProcess.Id})");

                // Track whether server started successfully or failed
                var startupTcs = new TaskCompletionSource<bool>();
                var startupComplete = false;

                _serverProcess.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        if (McpSettings.VerboseLogging) Debug.Log($"[MCP Server] {e.Data}");
                        OnServerOutput?.Invoke(e.Data);

                        // Server successfully started when we see the bridge listening message
                        if (!startupComplete && e.Data.Contains("[bridge] listening")) {
                            startupComplete = true;
                            startupTcs.TrySetResult(true);
                        }
                    }
                };

                _serverProcess.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        if (McpSettings.VerboseLogging) Debug.LogWarning($"[MCP Server] {e.Data}");
                        OnServerError?.Invoke(e.Data);

                        // Check for port already in use error
                        if (!startupComplete && (e.Data.Contains("EADDRINUSE") || e.Data.Contains("address already in use"))) {
                            startupComplete = true;
                            // Port is in use by another server - that's okay, mark as external
                            if (McpSettings.VerboseLogging) Debug.Log("[UnityMcp] Port already in use by another server, treating as external");
                            _externalServerDetected = true;
                            startupTcs.TrySetResult(true);
                        }
                    }
                };

                _serverProcess.EnableRaisingEvents = true;
                _serverProcess.Exited += (s, e) => {
                    var exitCode = -1;
                    try { exitCode = _serverProcess?.ExitCode ?? -1; } catch { }
                    if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Server process exited with code {exitCode}");
                    if (!startupComplete) {
                        startupComplete = true;
                        startupTcs.TrySetResult(false);
                    }
                    _serverProcess = null;
                    if (!_externalServerDetected) {
                        OnServerStopped?.Invoke();
                    }
                };

                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                // Register cleanup handlers
                EditorApplication.quitting -= OnEditorQuitting;
                EditorApplication.quitting += OnEditorQuitting;

                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;

                // Wait for server to start or fail (with timeout)
                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(startupTcs.Task, timeoutTask);

                if (completedTask == timeoutTask) {
                    if (McpSettings.VerboseLogging) Debug.LogWarning("[UnityMcp] Server startup timed out, assuming it's running");
                    OnServerStarted?.Invoke();
                    return true;
                }

                var success = await startupTcs.Task;
                if (success) {
                    OnServerStarted?.Invoke();
                    Debug.Log($"[UnityMcp] Server started on port {McpSettings.HttpPort}");
                }
                return success;
            } catch (Exception e) {
                Debug.LogError($"[UnityMcp] Failed to start server: {e.Message}");
                return false;
            }
        }

        static void OnEditorQuitting() {
            StopServer();
        }

        static void OnBeforeReload() {
            // Don't stop the server on domain reload - let it keep running
            // The TCP client will reconnect after reload
        }

        static string GetNodeExecutable() {
            // On Windows, just use "node" and let PATH resolve it
            // On macOS/Linux, check common locations
            if (Application.platform == RuntimePlatform.WindowsEditor) {
                return "node";
            }

            // Check common macOS/Linux Node.js locations
            var commonPaths = new[] {
                "/usr/local/bin/node",
                "/opt/homebrew/bin/node",
                "/usr/bin/node",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nvm/versions/node"),
            };

            foreach (var path in commonPaths) {
                if (path.Contains(".nvm")) {
                    // For nvm, find the latest version
                    if (Directory.Exists(path)) {
                        var versions = Directory.GetDirectories(path);
                        if (versions.Length > 0) {
                            Array.Sort(versions);
                            var latestNode = Path.Combine(versions[versions.Length - 1], "bin/node");
                            if (File.Exists(latestNode)) return latestNode;
                        }
                    }
                } else if (File.Exists(path)) {
                    return path;
                }
            }

            // Fall back to PATH
            return "node";
        }

        static string GetNpmExecutable() {
            if (Application.platform == RuntimePlatform.WindowsEditor) {
                return "npm";
            }

            var nodePath = GetNodeExecutable();
            if (nodePath != "node") {
                // Use npm from same directory as node
                var nodeDir = Path.GetDirectoryName(nodePath);
                var npmPath = Path.Combine(nodeDir, "npm");
                if (File.Exists(npmPath)) return npmPath;
            }

            return "npm";
        }

        static void EnsureNodeInPath(ProcessStartInfo psi) {
            if (Application.platform == RuntimePlatform.WindowsEditor) return;

            var nodePath = GetNodeExecutable();
            if (nodePath == "node") return;

            var nodeDir = Path.GetDirectoryName(nodePath);
            if (string.IsNullOrEmpty(nodeDir)) return;

            // Get current PATH or use a sensible default
            var currentPath = psi.Environment.ContainsKey("PATH")
                ? psi.Environment["PATH"]
                : Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin:/usr/sbin:/sbin";

            // Prepend the node directory to PATH
            if (!currentPath.Contains(nodeDir)) {
                psi.Environment["PATH"] = $"{nodeDir}:{currentPath}";
            }
        }
    }
}
