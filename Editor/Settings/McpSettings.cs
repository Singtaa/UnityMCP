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

        // MARK: Public Properties
        public static int HttpPort {
            get => Instance.httpPort;
            set {
                if (Instance.httpPort != value) {
                    Instance.httpPort = value;
                    Save();
                }
            }
        }

        public static int IpcPort {
            get => Instance.ipcPort;
            set {
                if (Instance.ipcPort != value) {
                    Instance.ipcPort = value;
                    Save();
                }
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
    }
}
