using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityMcp {
    /// <summary>
    /// Manages the Node.js MCP server process lifecycle.
    /// Handles automatic startup, npm install, and graceful shutdown.
    ///
    /// DOMAIN RELOAD HANDLING:
    /// We persist the server PID to SessionState so we can reattach to it after domain reload.
    /// SessionState is scoped to this Editor instance (unlike EditorPrefs, which is shared
    /// machine-wide), so multiple open Editors never reattach to each other's servers.
    ///
    /// MULTI-EDITOR SUPPORT:
    /// Each project runs its own Node server on its own port pair. When the configured IPC
    /// port is already owned by a different project's server (or an unrelated process), we
    /// probe its identity via "bridge.identify" and auto-allocate a free port pair for this
    /// project, persisted to ProjectSettings/McpSettings.json.
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

            var savedPid = SessionState.GetInt(PidPrefKey, -1);
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
            SessionState.SetInt(PidPrefKey, pid);
        }

        static void ClearSavedPid() {
            SessionState.EraseInt(PidPrefKey);
        }

        /// <summary>
        /// Check if the server ports are reachable. Useful for detecting external servers after domain reload.
        /// We check the IPC port since that's what Unity connects to.
        /// </summary>
        public static async Task<bool> CheckServerReachable() {
            // Check IPC port (TCP bridge) - this is what Unity actually connects to
            var ipcReachable = await IsPortInUse(McpSettings.EffectiveIpcPort);
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
                var stillReachable = await IsPortInUse(McpSettings.EffectiveIpcPort);
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

                // 3. Verify server files exist
                var packageJsonPath = Path.Combine(_serverPath, "package.json");
                if (!File.Exists(packageJsonPath)) {
                    Debug.LogError($"[UnityMcp] package.json not found in server folder: {_serverPath}");
                    return false;
                }

                // 4. Check/install node_modules
                var nodeModulesPath = Path.Combine(_serverPath, "node_modules");
                if (!Directory.Exists(nodeModulesPath)) {
                    Debug.Log("[UnityMcp] Installing dependencies (first run)...");
                    if (!await RunNpmInstall()) {
                        Debug.LogError("[UnityMcp] npm install failed");
                        return false;
                    }
                    Debug.Log("[UnityMcp] Dependencies installed successfully");
                }

                // 5. If the IPC port already has a listener, find out who owns it before adopting it.
                //    Same project (server survived domain reload, or started manually) -> adopt as external.
                //    Another project's server or an unrelated process -> allocate a free port pair for
                //    this project so multiple Editors can run side by side.
                var probe = await ProbeServer(McpSettings.EffectiveIpcPort);
                if (probe.Listening && !probe.Responded) {
                    // A server mid-startup or mid-shutdown can accept connections without answering
                    // identify yet; give it one more chance before treating it as foreign.
                    await Task.Delay(750);
                    probe = await ProbeServer(McpSettings.EffectiveIpcPort);
                }
                if (probe.Listening) {
                    if (IsOwnServer(probe)) {
                        if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Server already running for this project (IPC port {McpSettings.EffectiveIpcPort} in use)");
                        _externalServerDetected = true;
                        OnServerStarted?.Invoke();
                        return true;
                    }

                    // No identify response: possibly a legacy (pre-multi-editor) server started
                    // before a package update. Stop it so this project keeps its configured ports
                    // instead of drifting to an allocated pair and orphaning the old process.
                    if (!probe.Responded && TryKillLegacyServer()) {
                        await Task.Delay(250);
                        probe = await ProbeServer(McpSettings.EffectiveIpcPort);
                    }
                }
                if (probe.Listening) {
                    var occupant = probe.Responded && !string.IsNullOrEmpty(probe.ProjectRoot)
                        ? $"the MCP server for '{probe.ProjectRoot}'"
                        : "another process";
                    if (!TryAllocatePortPair(out var httpPort, out var ipcPort)) {
                        Debug.LogError($"[UnityMcp] IPC port {McpSettings.EffectiveIpcPort} is in use by {occupant} and no free port pair was found nearby. Set different ports in Window > Unity MCP Server.");
                        return false;
                    }

                    Debug.Log($"[UnityMcp] Port {McpSettings.EffectiveIpcPort} is in use by {occupant}. This project now uses HTTP port {httpPort} / IPC port {ipcPort} (stored per-machine in UserSettings/McpPortOverride.json).");
                    McpSettings.SetPortOverride(httpPort, ipcPort);
                }

                // 6. Start new server
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
            EndpointBeacon.Delete();
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
            var packagePath = GetPackagePath();
            if (string.IsNullOrEmpty(packagePath)) return null;

            var serverPath = Path.Combine(packagePath, "Server~");
            if (!Directory.Exists(serverPath)) return null;

            // If the package is in the immutable PackageCache, copy to a writable location
            if (IsInPackageCache(packagePath)) {
                return GetWritableServerPath(packagePath, serverPath);
            }

            return serverPath;
        }

        static bool IsInPackageCache(string packagePath) {
            var normalized = packagePath.Replace('\\', '/');
            var comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return normalized.IndexOf("Library/PackageCache", comparison) >= 0;
        }

        static string GetWritableServerPath(string packagePath, string sourceServerPath) {
            var projectRoot = ProjectPaths.ProjectRoot;
            var writablePath = Path.Combine(projectRoot, "Library", "UnityMcp", "Server");

            // Extract the hash from the PackageCache folder name (e.g., "com.singtaa.unity-mcp@abc1234")
            var folderName = Path.GetFileName(packagePath);
            var sourceHash = folderName.Contains("@") ? folderName.Substring(folderName.IndexOf('@') + 1) : folderName;

            var hashFile = Path.Combine(writablePath, ".source-hash");

            // Check if we already have an up-to-date copy
            if (Directory.Exists(writablePath) && File.Exists(hashFile)) {
                var existingHash = File.ReadAllText(hashFile).Trim();
                if (existingHash == sourceHash) {
                    return writablePath;
                }
            }

            // Copy source files to writable location (hash changed = package updated)
            Debug.Log($"[UnityMcp] Package is in immutable PackageCache, copying server files to {writablePath}");

            try {
                if (Directory.Exists(writablePath)) {
                    // Hash changed means package was updated — delete everything including
                    // node_modules so npm install picks up any dependency changes
                    Directory.Delete(writablePath, true);
                }
                Directory.CreateDirectory(writablePath);

                // Copy source files (skip node_modules from source if any exist there)
                CopyDirectory(sourceServerPath, writablePath, skipNodeModules: true);

                // Write source hash marker
                File.WriteAllText(hashFile, sourceHash);
            } catch (Exception e) {
                Debug.LogError($"[UnityMcp] Failed to copy server files to writable location: {e.Message}");
                return null;
            }

            return writablePath;
        }

        static void CopyDirectory(string source, string destination, bool skipNodeModules = false) {
            if (!Directory.Exists(destination))
                Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source)) {
                var destFile = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(source)) {
                var dirName = Path.GetFileName(dir);
                if (skipNodeModules && dirName == "node_modules") continue;
                CopyDirectory(dir, Path.Combine(destination, dirName));
            }
        }

        internal static string GetPackagePath() {
            // Use Unity's PackageInfo API to get the real physical path.
            // This correctly resolves git URL installs (Library/PackageCache/),
            // local installs (Packages/), and embedded packages.
            // Note: Packages/<name> is a virtual path remapped by Unity's patched Mono —
            // Directory.Exists() returns true but the OS can't access it for Process.Start.
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NodeProcessManager).Assembly);
            if (packageInfo != null) return packageInfo.resolvedPath;
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

                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] Running: {psi.FileName} install (cwd: {_serverPath})");

                using (var process = Process.Start(psi)) {
                    if (process == null) return false;

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    process.WaitForExit(60000); // 60 second timeout for npm install

                    var output = await outputTask;
                    var error = await errorTask;

                    if (process.ExitCode != 0) {
                        var details = !string.IsNullOrWhiteSpace(error) ? error : output;
                        Debug.LogError($"[UnityMcp] npm install failed (exit code {process.ExitCode}):\n{details}");
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

        // MARK: Multi-Editor Support
        class ServerProbeResult {
            public bool Listening;       // Something accepted the TCP connection
            public bool Responded;       // It answered the bridge.identify handshake (it's a UnityMcp hub)
            public string ProjectRoot = "";
        }

        /// <summary>
        /// Connect to an IPC port and ask the server which project it serves ("bridge.identify").
        /// Answered directly by the Node bridge hub without a Unity round-trip, so a live
        /// server always responds quickly. No response = not a (current-version) UnityMcp server.
        /// </summary>
        static async Task<ServerProbeResult> ProbeServer(int port) {
            var result = new ServerProbeResult();
            System.Net.Sockets.TcpClient client = null;
            try {
                client = new System.Net.Sockets.TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(1000));
                if (completed != connectTask || !client.Connected) return result;
                result.Listening = true;

                var stream = client.GetStream();
                var request = Encoding.UTF8.GetBytes("{\"t\":\"bridge.identify\"}\n");
                await stream.WriteAsync(request, 0, request.Length);

                var buffer = new byte[8192];
                var lineBytes = new MemoryStream();
                var deadline = Task.Delay(1500);
                var gotLine = false;
                while (!gotLine) {
                    var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
                    var done = await Task.WhenAny(readTask, deadline);
                    if (done != readTask) return result; // Timeout: pre-identify server or foreign protocol
                    var n = readTask.Result;
                    if (n <= 0) break;
                    for (int i = 0; i < n && !gotLine; i++) {
                        if (buffer[i] == (byte)'\n') gotLine = true;
                        else lineBytes.WriteByte(buffer[i]);
                    }
                    if (lineBytes.Length > 64 * 1024) break;
                }
                if (!gotLine) return result;

                var obj = JObject.Parse(Encoding.UTF8.GetString(lineBytes.ToArray()));
                if (obj.Value<string>("t") == "bridge.identity") {
                    result.Responded = true;
                    result.ProjectRoot = obj.Value<string>("projectRoot") ?? "";
                }
                return result;
            } catch {
                return result;
            } finally {
                try { client?.Close(); } catch { }
            }
        }

        /// <summary>
        /// One-time migration: pre-multi-editor package versions stored the server PID in
        /// machine-wide EditorPrefs and ran a server that predates the identify handshake.
        /// Consults (and clears) that legacy entry; kills the process if it's still a live
        /// Node server. Returns true if a process was killed.
        /// </summary>
        static bool TryKillLegacyServer() {
            var legacyPid = EditorPrefs.GetInt(PidPrefKey, -1);
            if (legacyPid <= 0) return false;
            EditorPrefs.DeleteKey(PidPrefKey);
            try {
                var process = Process.GetProcessById(legacyPid);
                if (process.HasExited || !process.ProcessName.ToLowerInvariant().Contains("node")) return false;
                Debug.Log($"[UnityMcp] Stopping legacy MCP server (PID {legacyPid}) left over from a pre-update session.");
                process.Kill();
                process.WaitForExit(2000);
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// A probed server counts as ours when it identifies with this project's root, or when
        /// it responds without a root (manually started server with MCP_PROJECT_ROOT unset).
        /// </summary>
        static bool IsOwnServer(ServerProbeResult probe) {
            if (!probe.Responded) return false;
            return string.IsNullOrEmpty(probe.ProjectRoot) || PathsEqual(probe.ProjectRoot, ProjectPaths.ProjectRoot);
        }

        static bool PathsEqual(string a, string b) {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/');
            var comparison = Application.platform == RuntimePlatform.LinuxEditor
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase; // Windows and macOS are case-insensitive by default
            return string.Equals(Norm(a), Norm(b), comparison);
        }

        static bool IsPortFree(int port) {
            try {
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                // Guard against SO_REUSEADDR false-frees (a port can test-bind as free on
                // macOS/Linux while another socket holds it). Best-effort: not every
                // platform/runtime supports the option.
                try { listener.ExclusiveAddressUse = true; } catch { }
                listener.Start();
                listener.Stop();
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Find a free HTTP/IPC port pair near the configured base ports (same offset for both,
        /// so a project's ports stay recognizable, e.g. 5174/52101).
        /// </summary>
        static bool TryAllocatePortPair(out int httpPort, out int ipcPort) {
            var httpBase = McpSettings.HttpPort;
            var ipcBase = McpSettings.IpcPort;
            for (int offset = 1; offset <= 50; offset++) {
                var h = httpBase + offset;
                var i = ipcBase + offset;
                if (h > 65535 || i > 65535) break;
                if (IsPortFree(h) && IsPortFree(i)) {
                    httpPort = h;
                    ipcPort = i;
                    return true;
                }
            }
            httpPort = 0;
            ipcPort = 0;
            return false;
        }

        static async Task<bool> StartServerAsync(bool isRetry = false) {
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
                psi.Environment["MCP_HTTP_PORT"] = McpSettings.EffectiveHttpPort.ToString();
                psi.Environment["MCP_IPC_PORT"] = McpSettings.EffectiveIpcPort.ToString();
                psi.Environment["MCP_REQUIRE_AUTH"] = McpSettings.AuthEnabled ? "true" : "false";
                psi.Environment["MCP_TOKEN"] = McpSettings.AuthToken;
                psi.Environment["MCP_PROJECT_ROOT"] = ProjectPaths.ProjectRoot;

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

                // EADDRINUSE fallback: a port we tried to bind was taken. Only adopt the occupant
                // if it actually serves this project; otherwise move to a free port pair and retry
                // once. (Catches races and non-MCP squatters, e.g. Vite on 5173.)
                if (success && _externalServerDetected) {
                    // Our own process is redundant once we adopt an external server. An IPC-only
                    // EADDRINUSE leaves it half-alive (dead bridge, live HTTP), so kill any residue.
                    if (_serverProcess != null) {
                        try { if (!_serverProcess.HasExited) _serverProcess.Kill(); } catch { }
                        try { _serverProcess.Dispose(); } catch { }
                        _serverProcess = null;
                        ClearSavedPid();
                    }

                    var probe = await ProbeServer(McpSettings.EffectiveIpcPort);
                    if (!IsOwnServer(probe)) {
                        _externalServerDetected = false;
                        if (!isRetry && TryAllocatePortPair(out var httpPort, out var ipcPort)) {
                            Debug.Log($"[UnityMcp] Configured ports are in use by another process. This project now uses HTTP port {httpPort} / IPC port {ipcPort} (stored per-machine in UserSettings/McpPortOverride.json).");
                            McpSettings.SetPortOverride(httpPort, ipcPort);
                            return await StartServerAsync(isRetry: true);
                        }
                        Debug.LogError($"[UnityMcp] Ports {McpSettings.EffectiveHttpPort}/{McpSettings.EffectiveIpcPort} are in use by another process and the server could not start. Set different ports in Window > Unity MCP Server.");
                        return false;
                    }
                }

                if (success) {
                    OnServerStarted?.Invoke();
                    Debug.Log($"[UnityMcp] Server started on port {McpSettings.EffectiveHttpPort}");
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

        static string _cachedNpmPath;

        static string GetNpmExecutable() {
            if (!string.IsNullOrEmpty(_cachedNpmPath)) return _cachedNpmPath;

            if (Application.platform == RuntimePlatform.WindowsEditor) {
                // Use 'where' to find the actual npm.cmd path to avoid picking up local node_modules/.bin/npm.cmd
                try {
                    var process = new Process {
                        StartInfo = new ProcessStartInfo {
                            FileName = "cmd.exe",
                            Arguments = "/c where npm.cmd",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    var firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine)) {
                        return _cachedNpmPath = firstLine;
                    }
                } catch { }
                return _cachedNpmPath = "npm.cmd";
            }

            var nodePath = GetNodeExecutable();
            if (nodePath != "node") {
                // Use npm from same directory as node
                var nodeDir = Path.GetDirectoryName(nodePath);
                var npmPath = Path.Combine(nodeDir, "npm");
                if (File.Exists(npmPath)) return _cachedNpmPath = npmPath;
            }

            return _cachedNpmPath = "npm";
        }

        static void EnsureNodeInPath(ProcessStartInfo psi) {
            var nodePath = GetNodeExecutable();

            if (Application.platform == RuntimePlatform.WindowsEditor) {
                // On Windows, ensure PATH is inherited for cmd/batch file resolution
                var winPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (!psi.Environment.ContainsKey("PATH")) {
                    psi.Environment["PATH"] = winPath;
                }
                return;
            }

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
