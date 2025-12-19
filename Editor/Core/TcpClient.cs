using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMcp {
    /// <summary>
    /// TCP client that communicates with the MCP Node server using newline-delimited JSON (NDJSON).
    ///
    /// ARCHITECTURE NOTES:
    /// - Runs a background thread for TCP I/O to avoid blocking Unity's main thread
    /// - Uses MainThreadDispatcher to execute tool calls on Unity's main thread
    /// - Implements automatic reconnection with exponential backoff
    ///
    /// CRITICAL - ZOMBIE THREAD PREVENTION:
    /// This client includes multiple mechanisms to prevent "zombie" threads from old
    /// domain reloads from connecting and hijacking the Node server connection.
    /// </summary>
    public sealed class McpTcpClient : IDisposable {
        // Static version counter - incremented each time a new client is created
        static volatile int _globalVersion = 0;

        // Static lock file path - used to coordinate between clients across domain reloads
        static readonly string LockFilePath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "UnityMcp_ActiveClient.lock");

        /// <summary>
        /// Clean up stale lock files from crashed Unity sessions.
        /// </summary>
        public static void CleanupStaleLockFiles() {
            try {
                if (System.IO.File.Exists(LockFilePath)) {
                    System.IO.File.Delete(LockFilePath);
                    if (McpSettings.VerboseLogging) Debug.Log("[UnityMcp] Cleaned up stale lock file");
                }
            } catch (Exception e) {
                if (McpSettings.VerboseLogging) Debug.LogWarning($"[UnityMcp] Failed to cleanup lock file: {e.Message}");
            }
        }

        // MARK: Config
        readonly string _host;
        readonly int _port;

        // MARK: State
        TcpClient _client;
        NetworkStream _stream;
        Thread _thread;
        volatile bool _disposed;

        readonly object _ioLock = new object();
        readonly object _connectingLock = new object();
        TcpClient _connectingClient;
        readonly ManualResetEventSlim _stopEvent = new ManualResetEventSlim(false);

        byte[] _recvBuf;
        int _backoffMs = 200;

        readonly StringBuilder _lineBuf = new StringBuilder(16 * 1024);
        const int MaxLineLength = 1 * 1024 * 1024; // 1MB max per message

        // Rate limiting for log messages
        DateTime _lastConnectLog = DateTime.MinValue;
        DateTime _lastDisconnectLog = DateTime.MinValue;
        const double LogRateLimitSeconds = 2.0;

        // Track consecutive connection failures to detect dead server
        int _consecutiveFailures = 0;
        const int FailuresBeforeServerCheck = 3;

        public static event Action OnServerUnreachable;

        // Unique client ID for tracking/debugging
        readonly string _clientId = Guid.NewGuid().ToString("N").Substring(0, 8);
        readonly int _myVersion;

        // Statistics
        int _totalCalls;

        public string ClientId => _clientId;
        public int TotalCalls => _totalCalls;
        public bool IsConnected {
            get {
                lock (_ioLock) {
                    return _stream != null && _client != null && _client.Connected;
                }
            }
        }

        public McpTcpClient(string host, int port) {
            _host = host;
            _port = port;
            _recvBuf = new byte[64 * 1024];

            // Increment global version - this invalidates all older clients
            _myVersion = Interlocked.Increment(ref _globalVersion);

            // Claim the lock file - this marks us as the active client
            ClaimLockFile();
        }

        void ClaimLockFile() {
            try {
                System.IO.File.WriteAllText(LockFilePath, _clientId);
            } catch (Exception e) {
                if (McpSettings.VerboseLogging) Debug.LogWarning($"[UnityMcp] Failed to claim lock file: {e.Message}");
            }
        }

        bool IsActiveClient() {
            // If no lock file exists, this client should claim it
            // This handles the race condition where the file hasn't been created yet
            try {
                if (!System.IO.File.Exists(LockFilePath)) {
                    // Try to claim it - we might be the first client after cleanup
                    ClaimLockFile();
                    return true;
                }
                var lockId = System.IO.File.ReadAllText(LockFilePath).Trim();
                return lockId == _clientId;
            } catch {
                return true;  // On error, assume we're active to avoid stopping prematurely
            }
        }

        // MARK: Lifecycle
        public void Start() {
            if (_thread != null) return;
            _disposed = false;
            _stopEvent.Reset();

            _thread = new Thread(Loop) {
                IsBackground = true,
                Name = $"UnityMcp.TcpClient-{_clientId}"
            };
            _thread.Start();
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            // Release lock file FIRST
            ReleaseLockFile();

            _stopEvent.Set();
            CloseConnectingClient();
            CloseSocket();

            try {
                if (_thread != null && _thread.IsAlive) {
                    _thread.Join(1000);
                }
            } catch { }

            _thread = null;
            try { _stopEvent.Dispose(); } catch { }
        }

        void ReleaseLockFile() {
            try {
                if (System.IO.File.Exists(LockFilePath)) {
                    var lockId = System.IO.File.ReadAllText(LockFilePath).Trim();
                    if (lockId == _clientId) {
                        System.IO.File.Delete(LockFilePath);
                    }
                }
            } catch { }
        }

        // MARK: Loop
        void Loop() {
            if (_disposed || _stopEvent.IsSet || ShouldStop) return;

            while (!_disposed && !_stopEvent.IsSet && !ShouldStop) {
                try {
                    if (ShouldStop) {
                        CloseSocket();
                        return;
                    }

                    if (!HasStream()) {
                        TryConnectOnce();
                        if (!HasStream()) {
                            SleepBackoff();
                            continue;
                        }
                    }

                    ReadLoop();

                    if (!_disposed && !_stopEvent.IsSet && !ShouldStop) {
                        SleepBackoff();
                    }
                } catch (ObjectDisposedException) {
                    return;
                } catch (Exception e) {
                    if (!_disposed && !ShouldStop && McpSettings.VerboseLogging) {
                        Debug.LogWarning($"[UnityMcp] Socket error: {e.Message}");
                    }
                    CloseSocket();
                    SleepBackoff();
                }
            }
        }

        bool ShouldStop {
            get {
                if (_disposed) return true;
                if (_myVersion < _globalVersion) return true;
                return !IsActiveClient();
            }
        }

        void SleepBackoff() {
            var ms = _backoffMs;
            _backoffMs = Math.Min(_backoffMs * 2, 5000);
            _stopEvent.Wait(ms);
        }

        void LogRateLimited(ref DateTime lastLog, string message) {
            var now = DateTime.UtcNow;
            if ((now - lastLog).TotalSeconds >= LogRateLimitSeconds) {
                lastLog = now;
                Debug.Log(message);
            }
        }

        bool HasStream() {
            lock (_ioLock) {
                return _stream != null && _client != null && _client.Connected;
            }
        }

        void TryConnectOnce() {
            CloseSocket();

            if (_disposed || _stopEvent.IsSet || ShouldStop) {
                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] TryConnectOnce: skipping (disposed={_disposed}, stopSet={_stopEvent.IsSet}, shouldStop={ShouldStop})");
                return;
            }

            var c = new TcpClient();
            c.NoDelay = true;
            c.ReceiveTimeout = 0;
            c.SendTimeout = 5000;

            lock (_connectingLock) {
                if (_disposed || ShouldStop) {
                    try { c.Close(); } catch { }
                    return;
                }
                _connectingClient = c;
            }

            if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] TryConnectOnce: attempting connection to {_host}:{_port}");

            try {
                c.Connect(_host, _port);
                _consecutiveFailures = 0;  // Reset on successful connect
            } catch (ObjectDisposedException) {
                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] TryConnectOnce: ObjectDisposedException");
                return;
            } catch (Exception e) {
                _consecutiveFailures++;
                if (!_disposed && !ShouldStop) {
                    if (McpSettings.VerboseLogging) LogRateLimited(ref _lastConnectLog, $"[UnityMcp] Connect failed: {e.Message}");

                    // After several failures, notify that server might be dead
                    if (_consecutiveFailures == FailuresBeforeServerCheck) {
                        if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] {FailuresBeforeServerCheck} consecutive connection failures, server may be unreachable");
                        MainThreadDispatcher.Enqueue(() => OnServerUnreachable?.Invoke());
                    }
                }
                lock (_connectingLock) { _connectingClient = null; }
                try { c.Close(); } catch { }
                return;
            }

            lock (_connectingLock) { _connectingClient = null; }

            if (_disposed || _stopEvent.IsSet || ShouldStop) {
                if (McpSettings.VerboseLogging) Debug.Log($"[UnityMcp] TryConnectOnce: connected but stopping");
                try { c.Close(); } catch { }
                return;
            }

            lock (_ioLock) {
                if (_disposed || ShouldStop) {
                    try { c.Close(); } catch { }
                    return;
                }
                _client = c;
                _stream = c.GetStream();
            }

            Debug.Log($"[UnityMcp] Bridge connected");
            SendHello();
        }

        void CloseSocket() {
            lock (_ioLock) {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null;
                _client = null;
            }
        }

        void CloseConnectingClient() {
            lock (_connectingLock) {
                try { _connectingClient?.Close(); } catch { }
                _connectingClient = null;
            }
        }

        // MARK: Read
        void ReadLoop() {
            while (!_disposed && !_stopEvent.IsSet && !ShouldStop) {
                NetworkStream s;
                lock (_ioLock) {
                    s = _stream;
                }
                if (s == null) return;

                if (ShouldStop) {
                    CloseSocket();
                    return;
                }

                int n;
                try {
                    n = s.Read(_recvBuf, 0, _recvBuf.Length);
                } catch (ObjectDisposedException) {
                    return;
                } catch (System.IO.IOException) {
                    CloseSocket();
                    return;
                } catch {
                    CloseSocket();
                    return;
                }

                if (n <= 0) {
                    if (!ShouldStop && McpSettings.VerboseLogging) {
                        LogRateLimited(ref _lastDisconnectLog, "[UnityMcp] Server disconnected.");
                    }
                    CloseSocket();
                    return;
                }

                _backoffMs = 200;

                var chunk = Encoding.UTF8.GetString(_recvBuf, 0, n);
                ConsumeChunk(chunk);
            }
        }

        void ConsumeChunk(string chunk) {
            for (int i = 0; i < chunk.Length; i++) {
                var ch = chunk[i];
                if (ch == '\n') {
                    var line = _lineBuf.ToString().Trim();
                    _lineBuf.Length = 0;

                    if (!string.IsNullOrEmpty(line)) {
                        HandleLine(line);
                    }
                } else if (ch != '\r') {
                    if (_lineBuf.Length >= MaxLineLength) {
                        if (McpSettings.VerboseLogging) Debug.LogWarning($"[UnityMcp] Line exceeded {MaxLineLength} bytes, discarding.");
                        _lineBuf.Length = 0;
                        CloseSocket();
                        return;
                    }
                    _lineBuf.Append(ch);
                }
            }
        }

        void HandleLine(string line) {
            if (_disposed) return;

            try {
                var root = JObject.Parse(line);
                var t = root.Value<string>("t");
                if (string.IsNullOrEmpty(t)) return;

                if (t == "call") {
                    Interlocked.Increment(ref _totalCalls);

                    var id = root.Value<string>("id") ?? "";
                    var tool = root.Value<string>("tool") ?? "";
                    var argsObj = root["args"] as JObject ?? new JObject();

                    // TCP-thread ping (no main thread needed)
                    if (tool == "unity.bridge.ping") {
                        SendResponse(id, ToolResultUtil.Text("pong"));
                        return;
                    }

                    // TCP-thread diagnostic
                    if (tool == "unity.bridge.dispatcherStatus") {
                        SendResponse(id, ToolResultUtil.Text(MainThreadDispatcher.GetStatusJson()));
                        return;
                    }

                    // Main-thread ping
                    if (tool == "unity.bridge.mainthreadPing") {
                        var client = this;
                        MainThreadDispatcher.Enqueue(() => {
                            if (!_disposed) {
                                client.SendResponse(id, ToolResultUtil.Text("pong-mainthread"));
                            }
                        });
                        return;
                    }

                    // Resource reads
                    if (tool.StartsWith("unity.resource.")) {
                        var capturedClient = this;
                        MainThreadDispatcher.Enqueue(() => {
                            if (!_disposed) {
                                ResourceRegistry.HandleResourceCall(capturedClient, id, tool, argsObj);
                            }
                        });
                        return;
                    }

                    // Everything else runs on main thread
                    var captured = this;
                    MainThreadDispatcher.Enqueue(() => {
                        if (!_disposed) {
                            ToolRegistry.HandleBridgeCall(captured, id, tool, argsObj);
                        }
                    });
                }
            } catch (Exception e) {
                if (!_disposed && McpSettings.VerboseLogging) {
                    Debug.LogWarning($"[UnityMcp] Bad message: {e.Message}");
                }
            }
        }

        // MARK: Send
        void SendLine(string line) {
            if (string.IsNullOrEmpty(line) || _disposed) return;

            var bytes = Encoding.UTF8.GetBytes(line + "\n");

            lock (_ioLock) {
                if (_stream == null || _disposed) return;

                try {
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                } catch {
                    try { _stream?.Close(); } catch { }
                    try { _client?.Close(); } catch { }
                    _stream = null;
                    _client = null;
                }
            }
        }

        void SendHello() {
            if (_disposed || ShouldStop) {
                CloseSocket();
                return;
            }

            var msg = new {
                t = "bridge.hello",
                clientId = _clientId,
                unityVersion = Application.unityVersion,
                projectRoot = ProjectPaths.ProjectRoot,
                timeUtc = DateTime.UtcNow.ToString("O"),
            };

            var json = JsonConvert.SerializeObject(msg, Formatting.None);
            SendLine(json);
        }

        public void SendResponse(string id, ToolResult result) {
            if (_disposed) return;

            var msg = new {
                t = "resp",
                id = id ?? "",
                result = result ?? ToolResultUtil.Text("Null tool result", true),
            };

            var json = JsonConvert.SerializeObject(msg, Formatting.None);
            SendLine(json);
        }

        public void SendResourceResponse(string id, ResourceResult result) {
            if (_disposed) return;

            var msg = new {
                t = "resp",
                id = id ?? "",
                result = result ?? ResourceResultUtil.Error("", "Null resource result"),
            };

            var json = JsonConvert.SerializeObject(msg, Formatting.None);
            SendLine(json);
        }
    }
}
