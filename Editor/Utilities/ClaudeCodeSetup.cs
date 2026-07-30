using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UnityMcp {
    /// <summary>
    /// One-click registration of the stdio launcher with Claude Code:
    ///   claude mcp add --scope user --transport stdio unity -- &lt;node&gt; ~/.unity-mcp/stdio.js
    ///
    /// User scope means once per machine: every Claude Code session in any Unity
    /// project then routes to its own editor through the launcher. Re-running
    /// replaces the existing user-scope entry, so the action is idempotent.
    ///
    /// The claude binary is resolved from known install locations and invoked
    /// directly - GUI-launched Unity doesn't inherit the user's shell PATH, and a
    /// non-interactive login shell doesn't source .zshrc (where PATH additions
    /// usually live), so "run it through the shell" is NOT reliable here.
    /// </summary>
    public static class ClaudeCodeSetup {
        const int TimeoutMs = 30000;

        public static async Task<(bool ok, string message)> RunAsync() {
            var launcher = EndpointBeacon.EnsureLauncherDeployed();
            if (launcher == null)
                return (false, "Could not deploy the stdio launcher (see Console for warnings).");

            // ConfigureAwait(false): the continuation must not require the Unity main
            // thread, so callers that block on this task cannot deadlock. The button's
            // own continuation still resumes on the main thread because ITS await
            // captures the Unity context.
            return await Task.Run(() => Run(launcher)).ConfigureAwait(false);
        }

        static (bool ok, string message) Run(string launcher) {
            var claude = FindClaudeCli();
            if (claude == null)
                return (false,
                    "Claude Code CLI not found. Install it (https://claude.com/claude-code), " +
                    "or run manually:\n  claude mcp add --scope user --transport stdio unity -- " +
                    $"node \"{launcher}\"");

            var node = NodeProcessManager.GetNodeExecutable();

            // Remove any existing user-scope entry first so re-running updates it;
            // failure here just means there was nothing to remove.
            Execute(claude, "mcp remove --scope user unity");

            var (exitCode, output) = Execute(claude,
                $"mcp add --scope user --transport stdio unity -- \"{node}\" \"{launcher}\"");
            if (exitCode != 0)
                return (false, $"claude CLI exited with code {exitCode}.\n{output}");

            return (true, string.IsNullOrEmpty(output)
                ? "Claude Code configured (user scope): every Unity project on this machine now connects automatically."
                : output);
        }

        // MARK: CLI discovery
        static string FindClaudeCli() {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor) {
                var winCandidates = new[] {
                    Path.Combine(home, ".local", "bin", "claude.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"),
                };
                foreach (var c in winCandidates)
                    if (File.Exists(c)) return c;
                return "claude"; // let PATH have a shot
            }

            var candidates = new List<string> {
                Path.Combine(home, ".local", "bin", "claude"),    // native installer (stable symlink)
                Path.Combine(home, ".claude", "local", "claude"), // legacy local install
                "/opt/homebrew/bin/claude",
                "/usr/local/bin/claude",
            };

            // npm -g under nvm
            var nvm = Path.Combine(home, ".nvm", "versions", "node");
            if (Directory.Exists(nvm)) {
                var versions = Directory.GetDirectories(nvm);
                Array.Sort(versions);
                for (int i = versions.Length - 1; i >= 0; i--)
                    candidates.Add(Path.Combine(versions[i], "bin", "claude"));
            }

            foreach (var c in candidates)
                if (File.Exists(c)) return c;

            // Last resort: a login shell finds installs whose PATH lives in
            // .zprofile/.profile (not .zshrc, which non-interactive shells skip)
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(shell))
                shell = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";
            var (exitCode, output) = Execute(shell, "-lc \"command -v claude\"", 8000);
            var found = output.Trim();
            if (exitCode == 0 && File.Exists(found)) return found;

            return null;
        }

        // MARK: Process helper
        static (int exitCode, string output) Execute(string fileName, string args, int timeoutMs = TimeoutMs) {
            try {
                // .cmd/.bat can't be exec'd directly without the shell
                if (fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)) {
                    args = $"/c \"{fileName}\" {args}";
                    fileName = "cmd.exe";
                }

                var psi = new ProcessStartInfo {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                var buffer = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) buffer.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) buffer.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs)) {
                    try { process.Kill(); } catch { }
                    return (-1, $"Timed out running: {fileName}");
                }
                process.WaitForExit(); // flush async output handlers

                return (process.ExitCode, buffer.ToString().Trim());
            } catch (Exception e) {
                return (-1, $"{e.GetType().Name}: {e.Message}");
            }
        }
    }
}
