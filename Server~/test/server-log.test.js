"use strict"

// The server must keep answering when nobody reads its stdout, which is its
// state after every domain reload: the Editor reattaches by PID and never reads
// the pipes again. Windows (and Linux) write to a pipe synchronously, so before
// the server logged to MCP_LOG_FILE, a full pipe stopped it outright and each
// play-mode entry spent about 8 s there. On macOS the Editor closes the pipes
// instead, and the next write to one ended the server with EPIPE: the Editor
// then started a new server, and each play-mode entry spent about 2 s more
// than with the server kept (4.9 s against 2.8 s, 6 Oct 2026).

const { test } = require("node:test")
const assert = require("node:assert")
const { spawn } = require("child_process")
const fs = require("fs")
const net = require("net")
const os = require("os")
const path = require("path")

const SERVER = path.join(__dirname, "..", "src", "server.js")

function freePort() {
    return new Promise((resolve, reject) => {
        const s = net.createServer()
        s.on("error", reject)
        s.listen(0, "127.0.0.1", () => { const { port } = s.address(); s.close(() => resolve(port)) })
    })
}

function waitForLine(stream, text) {
    return new Promise((resolve) => {
        let seen = ""
        const onData = (chunk) => {
            seen += chunk
            if (seen.includes(text)) { stream.off("data", onData); resolve() }
        }
        stream.on("data", onData)
    })
}

async function rpc(url, body, timeoutMs) {
    const ctl = new AbortController()
    const timer = setTimeout(() => ctl.abort(), timeoutMs)
    try {
        const r = await fetch(url, {
            method: "POST", signal: ctl.signal,
            headers: { "Content-Type": "application/json", Accept: "application/json" },
            body: JSON.stringify(body),
        })
        return await r.json()
    } finally {
        clearTimeout(timer)
    }
}

/** A stand-in for the Editor's TCP client: says hello, answers every call. */
function fakeBridge(port, projectRoot) {
    return new Promise((resolve, reject) => {
        const sock = net.connect(port, "127.0.0.1", () => {
            sock.write(JSON.stringify({ t: "bridge.hello", clientId: "test", projectRoot, timeUtc: new Date().toISOString() }) + "\n")
            resolve(sock)
        })
        sock.on("error", reject)
        let buf = ""
        sock.setEncoding("utf8")
        sock.on("data", (chunk) => {
            buf += chunk
            let i
            while ((i = buf.indexOf("\n")) >= 0) {
                const msg = JSON.parse(buf.slice(0, i))
                buf = buf.slice(i + 1)
                if (msg.t === "call") sock.write(JSON.stringify({ t: "resp", id: msg.id, result: { content: [{ type: "text", text: "pong" }] } }) + "\n")
            }
        })
    })
}

/** Starts the server, then leaves its pipes as `leave` does, as a domain reload leaves them. */
async function keepsAnswering(leave) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-log-test-"))
    const logFile = path.join(root, "Logs", "UnityMcpServer.log")
    const [httpPort, ipcPort] = [await freePort(), await freePort()]
    const child = spawn(process.execPath, [SERVER], {
        cwd: path.dirname(SERVER),
        stdio: ["ignore", "pipe", "pipe"],
        env: {
            ...process.env, MCP_HTTP_PORT: String(httpPort), MCP_IPC_PORT: String(ipcPort),
            MCP_REQUIRE_AUTH: "false", MCP_PROJECT_ROOT: root, MCP_LOG_FILE: logFile,
        },
    })
    let bridge = null
    try {
        child.stdout.setEncoding("utf8")
        await Promise.all([waitForLine(child.stdout, "[bridge] listening"), waitForLine(child.stdout, "[mcp] http listening")])
        leave(child)

        const url = `http://127.0.0.1:${httpPort}/mcp`
        bridge = await fakeBridge(ipcPort, root)
        const call = (id) => rpc(url, { jsonrpc: "2.0", id, method: "tools/call", params: { name: "unity_bridge_ping", arguments: {} } }, 3000)
        for (let i = 0; i < 50; i++) {
            const r = await call(i)
            if (!r.result.isError) break
            await new Promise((ok) => setTimeout(ok, 50))
        }

        // Each call used to log a line to stdout: a few hundred overflow the
        // pipe and every buffer behind it many times over.
        let id = 100
        for (let batch = 0; batch < 30; batch++) {
            const results = await Promise.all(Array.from({ length: 50 }, () => call(id++)))
            for (const r of results) assert.strictEqual(r.result.content[0].text, "pong")
        }
        const ping = await rpc(url, { jsonrpc: "2.0", id: 1, method: "ping" }, 1000)
        assert.deepStrictEqual(ping.result, {})

        assert.strictEqual(child.exitCode, null, "the server exited")
        assert.match(fs.readFileSync(logFile, "utf8"), /\[bridge\] -> call unity\.bridge\.ping/)
    } finally {
        bridge?.destroy()
        if (child.exitCode === null) {
            child.kill()
            await new Promise((ok) => child.once("exit", ok))
        }
        fs.rmSync(root, { recursive: true, force: true })
    }
}

// Windows: the pipes stay open and nothing reads them.
test("the server keeps answering while nobody reads its stdout", { timeout: 60000 }, () =>
    keepsAnswering((child) => { child.stdout.pause(); child.stderr.pause() }))

// macOS: the pipes are closed.
test("the server keeps answering once its stdout is closed", { timeout: 60000 }, () =>
    keepsAnswering((child) => { child.stdout.destroy(); child.stderr.destroy() }))
