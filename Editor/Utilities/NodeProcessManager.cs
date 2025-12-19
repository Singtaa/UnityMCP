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
    /// </summary>
    public static class NodeProcessManager {
        static Process _serverProcess;
        static string _serverPath;
        static bool _isStarting;

        public static bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;
        public static bool IsStarting => _isStarting;
        public static string ServerPath => _serverPath;

        public static event Action OnServerStarted;
        public static event Action OnServerStopped;
        public static event Action<string> OnServerOutput;
        public static event Action<string> OnServerError;

        // MARK: Public API
        public static async Task<bool> EnsureServerRunning() {
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

                // 4. Check if server already running (another Unity instance?)
                if (await IsPortInUse(McpSettings.HttpPort)) {
                    Debug.Log($"[UnityMcp] Server already running on port {McpSettings.HttpPort}");
                    return true;
                }

                // 5. Start server
                return StartServer();
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
                        var version = output.Trim();
                        Debug.Log($"[UnityMcp] Found Node.js {version}");
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
            try {
                using (var client = new System.Net.Sockets.TcpClient()) {
                    var connectTask = client.ConnectAsync("127.0.0.1", port);
                    var timeoutTask = Task.Delay(1000);

                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                    if (completedTask == connectTask && client.Connected) {
                        return true;
                    }
                }
            } catch {
                // Port not in use or connection refused
            }

            return false;
        }

        static bool StartServer() {
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

                _serverProcess.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        Debug.Log($"[MCP Server] {e.Data}");
                        OnServerOutput?.Invoke(e.Data);
                    }
                };

                _serverProcess.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        Debug.LogWarning($"[MCP Server] {e.Data}");
                        OnServerError?.Invoke(e.Data);
                    }
                };

                _serverProcess.EnableRaisingEvents = true;
                _serverProcess.Exited += (s, e) => {
                    Debug.Log("[UnityMcp] Server process exited");
                    OnServerStopped?.Invoke();
                };

                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                // Register cleanup handlers
                EditorApplication.quitting -= OnEditorQuitting;
                EditorApplication.quitting += OnEditorQuitting;

                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;

                OnServerStarted?.Invoke();
                Debug.Log($"[UnityMcp] Server started on port {McpSettings.HttpPort}");

                return true;
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
    }
}
