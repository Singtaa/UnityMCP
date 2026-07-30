using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// Makes this project's MCP endpoint discoverable by the stdio launcher
    /// (Server~/src/stdio.js) so clients need zero per-project configuration.
    ///
    /// Two artifacts:
    /// 1. Temp/UnityMcp_Endpoint.json - the live endpoint (url/token/pid) for THIS
    ///    project. Temp/ is per-editor-session and removed by Unity on quit, so the
    ///    beacon cannot go stale across sessions. Rewritten on every bridge connect
    ///    (covers domain reloads and port changes); removed when a foreign server
    ///    rejects the bridge or the server is stopped.
    /// 2. ~/.unity-mcp/stdio.js - a copy of the single-file launcher at a stable
    ///    machine path (the package itself may live under Library/PackageCache with
    ///    a version-dependent path). Clients register it once per machine:
    ///    claude mcp add --scope user unity -- node ~/.unity-mcp/stdio.js
    /// </summary>
    public static class EndpointBeacon {
        static string BeaconPath => Path.Combine(ProjectPaths.ProjectRoot, "Temp", "UnityMcp_Endpoint.json");

        /// <summary>Main-thread entry, enqueued by the TCP client on bridge connect.</summary>
        public static void OnBridgeConnected() {
            Write();
            EnsureLauncherDeployed();
        }

        public static void Write() {
            try {
                var payload = new {
                    url = $"http://{McpSettings.Host}:{McpSettings.EffectiveHttpPort}/mcp",
                    token = McpSettings.AuthEnabled ? McpSettings.AuthToken : null,
                    pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                    projectRoot = ProjectPaths.ProjectRoot,
                    writtenAtUtc = DateTime.UtcNow.ToString("O"),
                };
                var dir = Path.GetDirectoryName(BeaconPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(BeaconPath, JsonConvert.SerializeObject(payload, Formatting.Indented));
            } catch (Exception e) {
                Debug.LogWarning($"[UnityMcp] Failed writing endpoint beacon: {e.Message}");
            }
        }

        public static void Delete() {
            try {
                if (File.Exists(BeaconPath)) File.Delete(BeaconPath);
            } catch {
                // Temp/ is removed on editor quit regardless
            }
        }

        // Copies the launcher to a stable per-machine path. Content-compared so it
        // only writes when the packaged copy changed; written via a temp file +
        // rename so a launcher reading it mid-deploy never sees a torn file.
        static void EnsureLauncherDeployed() {
            try {
                var packagePath = NodeProcessManager.GetPackagePath();
                if (string.IsNullOrEmpty(packagePath)) return;
                var src = Path.Combine(packagePath, "Server~", "src", "stdio.js");
                if (!File.Exists(src)) return;

                var destDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-mcp");
                var dest = Path.Combine(destDir, "stdio.js");

                var srcText = File.ReadAllText(src);
                if (File.Exists(dest) && File.ReadAllText(dest) == srcText) return;

                Directory.CreateDirectory(destDir);
                var tmp = dest + ".tmp";
                File.WriteAllText(tmp, srcText);
                if (File.Exists(dest)) {
                    File.Replace(tmp, dest, null);
                } else {
                    File.Move(tmp, dest);
                }
                Debug.Log($"[UnityMcp] Deployed stdio launcher to {dest}");
            } catch (Exception e) {
                Debug.LogWarning($"[UnityMcp] Failed deploying stdio launcher: {e.Message}");
            }
        }
    }
}
