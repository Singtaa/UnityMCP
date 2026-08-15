#!/usr/bin/env node
"use strict"

// Unity MCP stdio launcher.
//
// Registered once per machine (never per project):
//   claude mcp add --scope user unity -- node ~/.unity-mcp/stdio.js
//
// Discovers which Unity project the current session is about, reads that
// project's live endpoint beacon (Temp/UnityMcp_Endpoint.json, written by the
// editor on bridge connect), and pumps newline-delimited MCP JSON-RPC between
// stdio and the project's HTTP endpoint.
//
// Resolution order for the project directory:
//   1. UNITY_MCP_PROJECT env var (explicit override)
//   2. CLAUDE_PROJECT_DIR env var (set by Claude Code for stdio servers)
//   3. walk up from cwd to the nearest folder with ProjectSettings/ProjectVersion.txt
//
// This process must NEVER exit on Unity-side failure: Claude Code does not
// restart stdio servers that die. The editor's Node server restarts on every
// domain reload, so connection-refused errors are retried against a freshly
// re-read beacon until a deadline. It exits only when stdin closes.
//
// Sessions heal without a client restart: a session may start before its
// editor is open (tools/list fails, the client registers zero tools). The
// launcher declares tools.listChanged and watches the beacon; when an editor
// comes up (or a different one takes over), it emits list_changed
// notifications so the client re-fetches the tool and resource lists
// mid-session. A per-project tools/list cache under ~/.unity-mcp/cache/
// additionally answers the initial list from the last known set while the
// editor is still closed.
//
// Self-contained on purpose (Node >= 18 builtins only): the editor copies this
// single file to ~/.unity-mcp/stdio.js, a stable path independent of where the
// Unity package itself lives (Packages/ or Library/PackageCache/<hash>).
// Deployment is upgrade-only (the editor compares LAUNCHER_VERSION), so two
// editors running different package versions do not fight over the file.

const crypto = require("crypto")
const fs = require("fs")
const os = require("os")
const path = require("path")
const readline = require("readline")

const LAUNCHER_VERSION = "1.1.0"
const BEACON_WATCH_INTERVAL_MS = 1500
const RETRY_WINDOW_MS = 20000
const RETRY_DELAY_MS = 500
const ATTEMPT_TIMEOUT_MS = 120000
const FALLBACK_PROTOCOL_VERSION = "2025-03-26"

// MARK: Project resolution
function walkUpToUnityProject(start) {
    let dir = path.resolve(start)
    for (;;) {
        if (fs.existsSync(path.join(dir, "ProjectSettings", "ProjectVersion.txt"))) return dir
        const parent = path.dirname(dir)
        if (parent === dir) return null
        dir = parent
    }
}

function findProjectRoot(env, cwd) {
    for (const start of [env.UNITY_MCP_PROJECT, env.CLAUDE_PROJECT_DIR, cwd]) {
        if (!start) continue
        const root = walkUpToUnityProject(start)
        if (root) return root
    }
    return null
}

function readBeacon(projectRoot) {
    if (!projectRoot) return null
    try {
        const raw = fs.readFileSync(path.join(projectRoot, "Temp", "UnityMcp_Endpoint.json"), "utf8")
        const beacon = JSON.parse(raw)
        if (beacon && typeof beacon.url === "string") return beacon
    } catch {
        // missing or mid-write: treated as "editor not up (yet)"
    }
    return null
}

// MARK: Tools cache
// The last successful tools/list result per project, so a session that starts
// with the editor closed still registers the full tool set immediately (calls
// error gracefully until the editor is up). Best-effort on every path.
function defaultCacheDir() {
    return path.join(os.homedir(), ".unity-mcp", "cache")
}

function toolsCachePath(root, dir = defaultCacheDir()) {
    if (!root) return null
    const resolved = path.resolve(root)
    const hash = crypto.createHash("sha1").update(resolved).digest("hex").slice(0, 12)
    return path.join(dir, `${path.basename(resolved)}-${hash}-tools.json`)
}

function writeToolsCache(root, result, dir = defaultCacheDir()) {
    const dest = toolsCachePath(root, dir)
    if (!dest || !result) return
    try {
        fs.mkdirSync(dir, { recursive: true })
        // tmp + rename so a concurrent reader never sees a torn file
        const tmp = `${dest}.${process.pid}.tmp`
        fs.writeFileSync(tmp, JSON.stringify({
            projectRoot: path.resolve(root),
            savedAtUtc: new Date().toISOString(),
            result,
        }))
        fs.renameSync(tmp, dest)
    } catch {
        // cache is an optimization, never an error
    }
}

function readToolsCache(root, dir = defaultCacheDir()) {
    const src = toolsCachePath(root, dir)
    if (!src) return null
    try {
        const data = JSON.parse(fs.readFileSync(src, "utf8"))
        if (data && data.result && Array.isArray(data.result.tools)) return data.result
    } catch {
        // missing or malformed: same as no cache
    }
    return null
}

// MARK: Beacon watching
// Identity of the editor behind the beacon. A domain reload rewrites the
// beacon with the same url+pid (no change); a newly opened editor has a new
// pid (and possibly an auto-allocated port), which is what should trigger a
// re-fetch of the tool list on the client.
function beaconKey(beacon) {
    return beacon ? `${beacon.url}|${beacon.pid}` : null
}

// MARK: Forwarding
const projectRoot = findProjectRoot(process.env, process.cwd())

function notRunningMessage() {
    return projectRoot
        ? `No running Unity editor found for project '${projectRoot}'. Open the project in Unity (the MCP server starts automatically), then retry.`
        : "No Unity project detected for this session (looked at UNITY_MCP_PROJECT, CLAUDE_PROJECT_DIR, and the working directory). Run from inside a Unity project, or set UNITY_MCP_PROJECT."
}

function isConnectionRefused(err) {
    // Only retry failures where the request never reached a server. Anything
    // that may have executed server-side must not be replayed (tool calls are
    // not idempotent). Node's fetch wraps localhost refusals in nested causes
    // (often an AggregateError covering IPv4+IPv6), so walk the whole chain.
    const codes = []
    const collect = e => {
        if (!e || codes.length > 16) return
        if (e.code) codes.push(e.code)
        if (Array.isArray(e.errors)) e.errors.forEach(collect)
        if (e.cause) collect(e.cause)
    }
    collect(err)
    return codes.includes("ECONNREFUSED")
}

async function forwardOnce(beacon, msg) {
    const headers = { "Content-Type": "application/json" }
    if (beacon.token) headers["Authorization"] = `Bearer ${beacon.token}`
    const res = await fetch(beacon.url, {
        method: "POST",
        headers,
        body: JSON.stringify(msg),
        signal: AbortSignal.timeout(ATTEMPT_TIMEOUT_MS),
    })
    const text = await res.text()
    if (!res.ok) {
        const err = new Error(`HTTP ${res.status}${text ? `: ${text.slice(0, 300)}` : ""}`)
        err.httpStatus = res.status
        throw err
    }
    return text ? JSON.parse(text) : null
}

async function forwardWithRetry(msg) {
    // No project resolved means no beacon can ever appear: fail fast instead of
    // holding the client for the whole retry window
    if (!projectRoot) throw new Error(notRunningMessage())

    const deadline = Date.now() + RETRY_WINDOW_MS
    let lastError = notRunningMessage()
    for (;;) {
        const beacon = readBeacon(projectRoot)
        if (beacon) {
            try {
                return await forwardOnce(beacon, msg)
            } catch (err) {
                // 401 means a stale beacon (token rotated / foreign server on the
                // port): re-read and retry like a connection failure
                if (!isConnectionRefused(err) && err.httpStatus !== 401) throw err
                lastError = `${err.message} (editor restarting?)`
            }
        }
        if (Date.now() > deadline) throw new Error(lastError)
        await new Promise(r => setTimeout(r, RETRY_DELAY_MS))
    }
}

// MARK: Local protocol handling
function initializeResult(requestedVersion) {
    return {
        protocolVersion: requestedVersion || FALLBACK_PROTOCOL_VERSION,
        capabilities: {
            // listChanged: the launcher notifies when an editor comes up (or a
            // different one takes over), so a session started editor-closed
            // gains the tools mid-session instead of needing a client restart
            tools: { listChanged: true },
            resources: { subscribe: false, listChanged: true },
        },
        serverInfo: {
            name: "unity-mcp",
            title: `Unity MCP (${projectRoot ? path.basename(projectRoot) : "no project detected"})`,
            version: LAUNCHER_VERSION,
        },
        instructions: projectRoot
            ? `Unity MCP Server for the project at ${projectRoot}. Requires the project to be open in the Unity editor; tools return an error while the editor is closed or reloading. Opening the editor mid-session is fine: the tool list refreshes automatically once it is up.`
            : "Unity MCP launcher: no Unity project was detected for this session. Tools will return errors until run from inside a Unity project.",
    }
}

// MARK: Beacon watcher (started once the client has initialized)
let _lastBeaconKey = null
let _watcherTimer = null

function startBeaconWatcher() {
    if (_watcherTimer || !projectRoot) return
    _lastBeaconKey = beaconKey(readBeacon(projectRoot))
    _watcherTimer = setInterval(() => {
        const key = beaconKey(readBeacon(projectRoot))
        if (key && key !== _lastBeaconKey) {
            writeMessage({ jsonrpc: "2.0", method: "notifications/tools/list_changed" })
            writeMessage({ jsonrpc: "2.0", method: "notifications/resources/list_changed" })
        }
        // Never cleared on disappearance: a quit editor that comes back is a new
        // pid (notify), while a domain reload keeps url+pid (stay silent).
        if (key) _lastBeaconKey = key
    }, BEACON_WATCH_INTERVAL_MS)
    if (_watcherTimer.unref) _watcherTimer.unref()
}

function writeMessage(msg) {
    process.stdout.write(JSON.stringify(msg) + "\n")
}

function toolErrorResult(id, message) {
    return {
        jsonrpc: "2.0",
        id,
        result: { content: [{ type: "text", text: message }], isError: true },
    }
}

async function handleMessage(msg) {
    const { id, method } = msg

    // Handshake is answered locally so the client's session never depends on
    // Unity being up at launch time
    if (method === "initialize") {
        writeMessage({
            jsonrpc: "2.0",
            id,
            result: initializeResult(msg.params && msg.params.protocolVersion),
        })
        startBeaconWatcher()
        return
    }
    if (method === "ping") {
        writeMessage({ jsonrpc: "2.0", id, result: {} })
        return
    }
    if (id === undefined || id === null) {
        // Notification: initialized is consumed locally, the rest forwarded
        // best-effort (no response either way)
        if (method !== "notifications/initialized") {
            forwardWithRetry(msg).catch(() => {})
        }
        return
    }

    // Editor down with a known tool set: answer the list immediately from the
    // cache rather than holding the client through the retry window. The
    // beacon watcher triggers a real re-list once an editor is up.
    if (method === "tools/list" && projectRoot && !readBeacon(projectRoot)) {
        const cached = readToolsCache(projectRoot)
        if (cached) {
            writeMessage({ jsonrpc: "2.0", id, result: cached })
            return
        }
    }

    try {
        const response = await forwardWithRetry(msg)
        if (method === "tools/list" && response && response.result) {
            writeToolsCache(projectRoot, response.result)
        }
        if (response) writeMessage(response)
        else writeMessage({ jsonrpc: "2.0", id, result: {} })
    } catch (err) {
        if (method === "tools/list") {
            // Editor not up (yet): answer from the last known tool set so the
            // client registers the tools now; calls error until the editor is up,
            // and the beacon watcher triggers a real re-list once it is.
            const cached = readToolsCache(projectRoot)
            if (cached) {
                writeMessage({ jsonrpc: "2.0", id, result: cached })
                return
            }
        }
        const message = `Unity MCP: ${err.message}`
        if (method === "tools/call") {
            // Tool-result-shaped errors read better to the model than protocol errors
            writeMessage(toolErrorResult(id, message))
        } else {
            writeMessage({ jsonrpc: "2.0", id, error: { code: -32000, message } })
        }
    }
}

// MARK: Main loop
function main() {
    process.stderr.write(`[unity-mcp stdio] v${LAUNCHER_VERSION} project=${projectRoot || "(none)"}\n`)

    const rl = readline.createInterface({ input: process.stdin, terminal: false })
    rl.on("line", line => {
        const trimmed = line.trim()
        if (!trimmed) return
        let msg
        try {
            msg = JSON.parse(trimmed)
        } catch {
            process.stderr.write("[unity-mcp stdio] dropped non-JSON line\n")
            return
        }
        handleMessage(msg).catch(err => {
            // Absolute backstop: an unhandled error must never kill the process
            process.stderr.write(`[unity-mcp stdio] internal error: ${err && err.stack}\n`)
        })
    })
    rl.on("close", () => process.exit(0))
}

if (require.main === module) main()

module.exports = {
    walkUpToUnityProject, findProjectRoot, readBeacon, isConnectionRefused,
    beaconKey, toolsCachePath, writeToolsCache, readToolsCache,
    LAUNCHER_VERSION,
}
