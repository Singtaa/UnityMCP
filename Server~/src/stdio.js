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
// Self-contained on purpose (Node >= 18 builtins only): the editor copies this
// single file to ~/.unity-mcp/stdio.js, a stable path independent of where the
// Unity package itself lives (Packages/ or Library/PackageCache/<hash>).

const fs = require("fs")
const path = require("path")
const readline = require("readline")

const LAUNCHER_VERSION = "1.0.0"
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
            tools: { listChanged: false },
            resources: { subscribe: false, listChanged: false },
        },
        serverInfo: {
            name: "unity-mcp",
            title: `Unity MCP (${projectRoot ? path.basename(projectRoot) : "no project detected"})`,
            version: LAUNCHER_VERSION,
        },
        instructions: projectRoot
            ? `Unity MCP Server for the project at ${projectRoot}. Requires the project to be open in the Unity editor; tools return an error while the editor is closed or reloading.`
            : "Unity MCP launcher: no Unity project was detected for this session. Tools will return errors until run from inside a Unity project.",
    }
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

    try {
        const response = await forwardWithRetry(msg)
        if (response) writeMessage(response)
        else writeMessage({ jsonrpc: "2.0", id, result: {} })
    } catch (err) {
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

module.exports = { walkUpToUnityProject, findProjectRoot, readBeacon, isConnectionRefused }
