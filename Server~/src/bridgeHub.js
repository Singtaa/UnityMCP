"use strict"

/**
 * BridgeHub - TCP server that communicates with Unity's MCP Bridge client.
 *
 * ARCHITECTURE:
 * - Listens for TCP connections from Unity on a configurable port
 * - Uses newline-delimited JSON (NDJSON) for message framing
 * - Routes MCP tool calls to Unity and returns responses
 *
 * CRITICAL - ZOMBIE CLIENT PREVENTION:
 * Unity may have multiple processes (main editor + AssetImportWorkers) that all
 * try to connect. Additionally, during domain reloads, old background threads
 * may survive and attempt to reconnect.
 *
 * To handle this, we implement TIMESTAMP-BASED VALIDATION:
 * - Each Unity client sends a "bridge.hello" message with a UTC timestamp
 * - We only accept connections where the timestamp is >= the current client's timestamp
 * - This ensures we always prefer the newest client (the real main editor)
 * - Zombie clients with older timestamps are rejected
 *
 * The Unity side (McpBridgeBootstrap.cs) also filters out AssetImportWorker processes
 * to prevent them from connecting in the first place.
 */

const net = require("net")
const crypto = require("crypto")

class BridgeHub {
    constructor({ host, port, timeoutMs }) {
        this._host = host
        this._port = port

        const t = Number(timeoutMs)
        this._timeoutMs = Number.isFinite(t) && t > 0 ? t : 8000

        this._server = null
        this._bridge = null
        this._buffer = ""
        this._lastHelloTime = null
        this._lastClientId = null
        this._lastHelloUtc = null  // UTC timestamp from Unity client's hello message

        this._pending = new Map() // id -> { resolve, timer }
    }

    start() {
        this._server = net.createServer((sock) => this._onConnection(sock))
        this._server.listen(this._port, this._host, () => {
            console.log(`[bridge] listening on tcp://${this._host}:${this._port}`)
        })
    }

    isConnected() {
        return !!this._bridge && !this._bridge.destroyed
    }

    async callTool(tool, args) {
        if (!this.isConnected()) {
            return {
                content: [{ type: "text", text: "Unity bridge unavailable (not connected)." }],
                isError: true,
            }
        }

        const id = crypto.randomUUID()
        const msg = { t: "call", id, tool, args: args ?? {} }

        const p = new Promise((resolve) => {
            const timer = setTimeout(() => {
                this._pending.delete(id)
                resolve({
                    content: [{ type: "text", text: `Unity bridge timeout after ${this._timeoutMs}ms.` }],
                    isError: true,
                })
            }, this._timeoutMs)

            this._pending.set(id, { resolve, timer })
        })
        console.log(`[bridge] -> call ${tool} id=${id}`);
        this._writeLine(msg)
        return p
    }

    _onConnection(sock) {
        // Don't immediately replace the bridge - wait for hello message validation
        // This prevents zombie threads from hijacking the connection

        sock.setEncoding("utf8")
        let pendingBuffer = ""
        const thisSock = sock

        sock.on("data", (chunk) => {
            if (this._bridge === thisSock) {
                this._onData(chunk)
                return
            }

            // Pending connection waiting for hello validation
            pendingBuffer += chunk

            while (true) {
                const idx = pendingBuffer.indexOf("\n")
                if (idx < 0) break

                const line = pendingBuffer.slice(0, idx).trim()
                pendingBuffer = pendingBuffer.slice(idx + 1)
                if (!line) continue

                let msg
                try {
                    msg = JSON.parse(line)
                } catch {
                    continue
                }

                if (msg.t === "bridge.hello") {
                    const newHelloUtc = msg.timeUtc ? new Date(msg.timeUtc).getTime() : Date.now()
                    const currentHelloUtc = this._lastHelloUtc || 0

                    // Accept if: no existing bridge, newer timestamp, or same client reconnecting
                    const noExistingBridge = !this._bridge || this._bridge.destroyed
                    const isNewer = newHelloUtc >= currentHelloUtc
                    const isSameClient = msg.clientId === this._lastClientId

                    if (noExistingBridge || isNewer || isSameClient) {
                        if (this._bridge && !this._bridge.destroyed) {
                            try { this._bridge.removeAllListeners(); this._bridge.destroy() } catch { }
                        }

                        this._bridge = thisSock
                        this._buffer = pendingBuffer
                        this._lastHelloTime = Date.now()
                        this._lastClientId = msg.clientId
                        this._lastHelloUtc = newHelloUtc

                        console.log(`[bridge] connected: clientId=${msg.clientId}`)
                    } else {
                        // Reject zombie connection with older timestamp
                        try { thisSock.destroy() } catch { }
                    }
                    return
                }
            }
        })
        sock.on("close", () => {
            if (this._bridge === thisSock) {
                this._onClose()
            }
        })
        sock.on("error", () => {
            if (this._bridge === thisSock) {
                this._onClose()
            }
        })
    }

    _onData(chunk) {
        this._buffer += chunk

        while (true) {
            const idx = this._buffer.indexOf("\n")
            if (idx < 0) break

            const line = this._buffer.slice(0, idx).trim()
            this._buffer = this._buffer.slice(idx + 1)
            if (!line) continue

            let msg
            try {
                msg = JSON.parse(line)
            } catch {
                continue
            }

            // Hello already processed during connection validation
            if (msg.t === "bridge.hello") continue

            if (msg.t === "resp" && msg.id) {
                const pending = this._pending.get(msg.id)
                if (!pending) continue

                clearTimeout(pending.timer)
                this._pending.delete(msg.id)

                pending.resolve(
                    msg.result ?? {
                        content: [{ type: "text", text: "Malformed bridge response (missing result)." }],
                        isError: true,
                    }
                )
            }
        }
    }

    _onClose() {
        if (!this._bridge) return

        console.log("[bridge] disconnected")

        try {
            this._bridge.destroy()
        } catch { }

        this._bridge = null
        this._lastHelloTime = null
        this._lastClientId = null
        this._lastHelloUtc = null

        // Fail all pending requests quickly
        for (const [id, p] of this._pending.entries()) {
            clearTimeout(p.timer)
            p.resolve({
                content: [{ type: "text", text: "Unity bridge disconnected during request." }],
                isError: true,
            })
            this._pending.delete(id)
        }
    }

    _writeLine(obj) {
        const line = JSON.stringify(obj) + "\n"
        try {
            this._bridge.write(line)
        } catch {
            this._onClose()
        }
    }
}

module.exports = { BridgeHub }
