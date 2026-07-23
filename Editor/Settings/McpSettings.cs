using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// Project-level settings for the Unity MCP Server.
    /// Stored in ProjectSettings/McpSettings.json for team sharing.
    /// </summary>
    [Serializable]
    public class McpSettings {
        // MARK: Singleton
        static McpSettings _instance;
        static readonly string SettingsPath = "ProjectSettings/McpSettings.json";

        public static McpSettings Instance {
            get {
                if (_instance == null) {
                    Load();
                }
                return _instance;
            }
        }

        // MARK: Settings Fields
        [SerializeField] int httpPort = 5173;
        [SerializeField] int ipcPort = 52100;
        [SerializeField] string host = "127.0.0.1";
        [SerializeField] bool autoStart = true;
        [SerializeField] bool autoConnect = true;
        [SerializeField] bool authEnabled = true;
        [SerializeField] string authToken = "";
        [SerializeField] bool verboseLogging = false;

        // MARK: Public Properties
        public static int HttpPort {
            get => Instance.httpPort;
            set {
                if (Instance.httpPort != value) {
                    Instance.httpPort = value;
                    ClearPortOverride(); // manual choice supersedes any auto-allocation
                    Save();
                }
            }
        }

        public static int IpcPort {
            get => Instance.ipcPort;
            set {
                if (Instance.ipcPort != value) {
                    Instance.ipcPort = value;
                    ClearPortOverride(); // manual choice supersedes any auto-allocation
                    Save();
                }
            }
        }

        // MARK: Machine-Local Port Overrides
        // Auto-allocated ports live in UserSettings (gitignored, per-machine), NOT in the
        // team-shared ProjectSettings file: persisting an allocation there would commit one
        // machine's port shuffle to the whole team and make ports drift over time via VCS.
        // The ProjectSettings ports are the team-preferred defaults; the override wins locally
        // until the preferred ports change or the user picks ports manually.
        [Serializable]
        class PortOverride {
            public int httpPort;
            public int ipcPort;
            public int basedOnHttpPort; // preferred ports the allocation was based on;
            public int basedOnIpcPort;  // the override is discarded when these change
        }

        static readonly string PortOverridePath = "UserSettings/McpPortOverride.json";
        static PortOverride _portOverride;
        static bool _portOverrideLoaded;

        static PortOverride GetPortOverride() {
            if (_portOverrideLoaded) return _portOverride;
            _portOverrideLoaded = true;

            try {
                if (File.Exists(PortOverridePath)) {
                    _portOverride = JsonUtility.FromJson<PortOverride>(File.ReadAllText(PortOverridePath));
                }
            } catch (Exception e) {
                Debug.LogWarning($"[McpSettings] Failed to load port override: {e.Message}");
            }

            // Discard when the team-preferred ports changed since the allocation was made
            if (_portOverride != null &&
                (_portOverride.basedOnHttpPort != Instance.httpPort || _portOverride.basedOnIpcPort != Instance.ipcPort)) {
                ClearPortOverride();
            }
            return _portOverride;
        }

        /// <summary>HTTP port actually in use: the machine-local override if present, else the configured port.</summary>
        public static int EffectiveHttpPort => GetPortOverride()?.httpPort ?? Instance.httpPort;

        /// <summary>IPC port actually in use: the machine-local override if present, else the configured port.</summary>
        public static int EffectiveIpcPort => GetPortOverride()?.ipcPort ?? Instance.ipcPort;

        public static bool HasPortOverride => GetPortOverride() != null;

        public static void SetPortOverride(int httpPort, int ipcPort) {
            _portOverride = new PortOverride {
                httpPort = httpPort,
                ipcPort = ipcPort,
                basedOnHttpPort = Instance.httpPort,
                basedOnIpcPort = Instance.ipcPort,
            };
            _portOverrideLoaded = true;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(PortOverridePath));
                File.WriteAllText(PortOverridePath, JsonUtility.ToJson(_portOverride, true));
            } catch (Exception e) {
                Debug.LogWarning($"[McpSettings] Failed to save port override: {e.Message}");
            }
        }

        public static void ClearPortOverride() {
            _portOverride = null;
            _portOverrideLoaded = true;
            try {
                if (File.Exists(PortOverridePath)) File.Delete(PortOverridePath);
            } catch (Exception e) {
                Debug.LogWarning($"[McpSettings] Failed to delete port override: {e.Message}");
            }
        }

        public static string Host {
            get => Instance.host;
            set {
                if (Instance.host != value) {
                    Instance.host = value;
                    Save();
                }
            }
        }

        public static bool AutoStart {
            get => Instance.autoStart;
            set {
                if (Instance.autoStart != value) {
                    Instance.autoStart = value;
                    Save();
                }
            }
        }

        public static bool AutoConnect {
            get => Instance.autoConnect;
            set {
                if (Instance.autoConnect != value) {
                    Instance.autoConnect = value;
                    Save();
                }
            }
        }

        public static bool AuthEnabled {
            get => Instance.authEnabled;
            set {
                if (Instance.authEnabled != value) {
                    Instance.authEnabled = value;
                    Save();
                }
            }
        }

        public static string AuthToken {
            get {
                if (string.IsNullOrEmpty(Instance.authToken)) {
                    Instance.authToken = GenerateToken();
                    Save();
                }
                return Instance.authToken;
            }
            set {
                if (Instance.authToken != value) {
                    Instance.authToken = value;
                    Save();
                }
            }
        }

        public static bool VerboseLogging {
            get => Instance.verboseLogging;
            set {
                if (Instance.verboseLogging != value) {
                    Instance.verboseLogging = value;
                    Save();
                }
            }
        }

        // MARK: Persistence
        static void Load() {
            _instance = new McpSettings();

            if (File.Exists(SettingsPath)) {
                try {
                    var json = File.ReadAllText(SettingsPath);
                    JsonUtility.FromJsonOverwrite(json, _instance);
                } catch (Exception e) {
                    Debug.LogWarning($"[McpSettings] Failed to load settings: {e.Message}");
                }
            }

            // Generate token if empty
            if (string.IsNullOrEmpty(_instance.authToken)) {
                _instance.authToken = GenerateToken();
                Save();
            }
        }

        public static void Save() {
            if (_instance == null) return;

            try {
                var json = JsonUtility.ToJson(_instance, true);
                File.WriteAllText(SettingsPath, json);
            } catch (Exception e) {
                Debug.LogWarning($"[McpSettings] Failed to save settings: {e.Message}");
            }
        }

        static string GenerateToken() {
            var bytes = new byte[24];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes);
        }

        // MARK: Defaults
        public static void ResetToDefaults() {
            _instance = new McpSettings();
            Save();
        }

        /// <summary>
        /// Force reload settings from disk. Call this after external changes to the settings file.
        /// </summary>
        public static void Reload() {
            _instance = null;
            Load();
        }
    }
}
