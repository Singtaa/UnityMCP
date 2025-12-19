using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityMcp {
    /// <summary>
    /// Editor window for managing and monitoring the Unity MCP Server.
    /// </summary>
    public class McpServerWindow : EditorWindow {
        // UI elements
        Label _statusLabel;
        VisualElement _statusIndicator;
        Label _serverStatusLabel;
        Label _bridgeStatusLabel;
        Label _clientIdLabel;
        Label _uptimeLabel;
        Label _callsLabel;
        Button _startButton;
        Button _stopButton;
        Button _reconnectButton;
        Button _copyTokenButton;
        IntegerField _httpPortField;
        IntegerField _ipcPortField;
        Toggle _autoStartToggle;
        Toggle _autoConnectToggle;
        Foldout _toolsFoldout;
        Foldout _resourcesFoldout;

        [MenuItem("Window/Unity MCP Server")]
        public static void ShowWindow() {
            var window = GetWindow<McpServerWindow>();
            window.titleContent = new GUIContent("MCP Server");
            window.minSize = new Vector2(350, 500);
        }

        void CreateGUI() {
            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            // Status header
            var statusHeader = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 15
                }
            };

            _statusIndicator = new VisualElement {
                style = {
                    width = 12,
                    height = 12,
                    borderTopLeftRadius = 6,
                    borderTopRightRadius = 6,
                    borderBottomLeftRadius = 6,
                    borderBottomRightRadius = 6,
                    marginRight = 8,
                    backgroundColor = Color.gray
                }
            };
            statusHeader.Add(_statusIndicator);

            _statusLabel = new Label("Checking...") {
                style = { fontSize = 14, unityFontStyleAndWeight = FontStyle.Bold }
            };
            statusHeader.Add(_statusLabel);
            root.Add(statusHeader);

            // Server section
            root.Add(CreateSection("Node Server", out var serverContent));
            serverContent.Add(CreateLabelRow("Status:", out _serverStatusLabel));
            serverContent.Add(CreateLabelRow("Port:", out var portLabel));
            portLabel.text = McpSettings.HttpPort.ToString();

            var serverButtons = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    marginTop = 8
                }
            };
            _startButton = new Button(OnStartServer) { text = "Start", style = { flexGrow = 1 } };
            _stopButton = new Button(OnStopServer) { text = "Stop", style = { flexGrow = 1 } };
            _copyTokenButton = new Button(OnCopyToken) { text = "Copy Token", style = { flexGrow = 1 } };
            serverButtons.Add(_startButton);
            serverButtons.Add(_stopButton);
            serverButtons.Add(_copyTokenButton);
            serverContent.Add(serverButtons);

            // Bridge section
            root.Add(CreateSection("Unity Bridge", out var bridgeContent));
            bridgeContent.Add(CreateLabelRow("Status:", out _bridgeStatusLabel));
            bridgeContent.Add(CreateLabelRow("Client ID:", out _clientIdLabel));
            bridgeContent.Add(CreateLabelRow("Uptime:", out _uptimeLabel));
            bridgeContent.Add(CreateLabelRow("Calls:", out _callsLabel));

            var bridgeButtons = new VisualElement {
                style = { flexDirection = FlexDirection.Row, marginTop = 8 }
            };
            _reconnectButton = new Button(OnReconnect) { text = "Reconnect", style = { flexGrow = 1 } };
            bridgeButtons.Add(_reconnectButton);
            bridgeContent.Add(bridgeButtons);

            // Tools foldout
            _toolsFoldout = new Foldout {
                text = "Tools (loading...)",
                style = { marginTop = 10 }
            };
            root.Add(_toolsFoldout);

            // Resources foldout
            _resourcesFoldout = new Foldout {
                text = "Resources (loading...)",
                style = { marginTop = 5 }
            };
            root.Add(_resourcesFoldout);

            // Settings section
            root.Add(CreateSection("Settings", out var settingsContent));

            _httpPortField = new IntegerField("HTTP Port") { value = McpSettings.HttpPort };
            _httpPortField.RegisterValueChangedCallback(evt => McpSettings.HttpPort = evt.newValue);
            settingsContent.Add(_httpPortField);

            _ipcPortField = new IntegerField("IPC Port") { value = McpSettings.IpcPort };
            _ipcPortField.RegisterValueChangedCallback(evt => McpSettings.IpcPort = evt.newValue);
            settingsContent.Add(_ipcPortField);

            _autoStartToggle = new Toggle("Auto-start server") { value = McpSettings.AutoStart };
            _autoStartToggle.RegisterValueChangedCallback(evt => McpSettings.AutoStart = evt.newValue);
            settingsContent.Add(_autoStartToggle);

            _autoConnectToggle = new Toggle("Auto-connect bridge") { value = McpSettings.AutoConnect };
            _autoConnectToggle.RegisterValueChangedCallback(evt => McpSettings.AutoConnect = evt.newValue);
            settingsContent.Add(_autoConnectToggle);

            // Start periodic refresh
            EditorApplication.update += RefreshUI;

            // Initial refresh
            RefreshUI();
            PopulateToolsList();
            PopulateResourcesList();
        }

        void OnDestroy() {
            EditorApplication.update -= RefreshUI;
        }

        VisualElement CreateSection(string title, out VisualElement content) {
            var section = new VisualElement {
                style = {
                    marginTop = 10,
                    marginBottom = 5,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 8,
                    paddingBottom = 8,
                    backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f),
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4
                }
            };

            var header = new Label(title) {
                style = {
                    fontSize = 12,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 8,
                    color = new Color(0.8f, 0.8f, 0.8f)
                }
            };
            section.Add(header);

            content = new VisualElement();
            section.Add(content);

            return section;
        }

        VisualElement CreateLabelRow(string label, out Label valueLabel) {
            var row = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    marginBottom = 4
                }
            };

            var labelElement = new Label(label) {
                style = { width = 80, color = new Color(0.7f, 0.7f, 0.7f) }
            };
            row.Add(labelElement);

            valueLabel = new Label("—") { style = { flexGrow = 1 } };
            row.Add(valueLabel);

            return row;
        }

        void RefreshUI() {
            if (_statusLabel == null) return;

            var serverRunning = NodeProcessManager.IsRunning;
            var bridgeConnected = McpBridge.IsConnected;

            // Overall status
            if (serverRunning && bridgeConnected) {
                _statusLabel.text = "Connected";
                _statusIndicator.style.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            } else if (serverRunning) {
                _statusLabel.text = "Server Running (Bridge Disconnected)";
                _statusIndicator.style.backgroundColor = new Color(0.8f, 0.8f, 0.2f);
            } else {
                _statusLabel.text = "Server Stopped";
                _statusIndicator.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
            }

            // Server status
            _serverStatusLabel.text = serverRunning ? "Running" : (NodeProcessManager.IsStarting ? "Starting..." : "Stopped");

            // Bridge status
            _bridgeStatusLabel.text = bridgeConnected ? "Connected" : "Disconnected";
            _clientIdLabel.text = McpBridge.ClientId ?? "—";

            // Uptime
            var uptime = McpBridge.Uptime;
            if (uptime.TotalSeconds > 0) {
                if (uptime.TotalHours >= 1) {
                    _uptimeLabel.text = $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
                } else if (uptime.TotalMinutes >= 1) {
                    _uptimeLabel.text = $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
                } else {
                    _uptimeLabel.text = $"{(int)uptime.TotalSeconds}s";
                }
            } else {
                _uptimeLabel.text = "—";
            }

            _callsLabel.text = McpBridge.TotalCalls.ToString();

            // Button states
            _startButton.SetEnabled(!serverRunning && !NodeProcessManager.IsStarting);
            _stopButton.SetEnabled(serverRunning);
            _reconnectButton.SetEnabled(serverRunning);
        }

        void PopulateToolsList() {
            _toolsFoldout.Clear();

            var toolNames = ToolRegistry.GetToolNames();
            var count = 0;

            foreach (var name in toolNames) {
                var label = new Label($"  • {name}") {
                    style = { fontSize = 11, color = new Color(0.7f, 0.7f, 0.7f) }
                };
                _toolsFoldout.Add(label);
                count++;
            }

            _toolsFoldout.text = $"Tools ({count})";
        }

        void PopulateResourcesList() {
            _resourcesFoldout.Clear();

            var resourceNames = ResourceRegistry.GetResourceNames();
            var count = 0;

            foreach (var name in resourceNames) {
                var label = new Label($"  • {name}") {
                    style = { fontSize = 11, color = new Color(0.7f, 0.7f, 0.7f) }
                };
                _resourcesFoldout.Add(label);
                count++;
            }

            _resourcesFoldout.text = $"Resources ({count})";
        }

        async void OnStartServer() {
            await NodeProcessManager.EnsureServerRunning();
        }

        void OnStopServer() {
            NodeProcessManager.StopServer();
        }

        void OnReconnect() {
            McpBridge.Reconnect();
        }

        void OnCopyToken() {
            EditorGUIUtility.systemCopyBuffer = McpSettings.AuthToken;
            Debug.Log("[UnityMcp] Auth token copied to clipboard");
        }
    }
}
