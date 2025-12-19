"use strict"

require('dotenv').config()
const http = require("http")
const { URL } = require("url")

const { config } = require("./config")
const { tools, toolNameToBridgeName } = require("./toolRegistry")
const { resources, resourceTemplates, parseResourceUri } = require("./resourceRegistry")
const { BridgeHub } = require("./bridgeHub")
const { makeError, makeResult } = require("./jsonrpc")

// ---- Extract config values ----
const {
    httpHost,
    httpPort,
    ipcHost,
    ipcPort,
    authEnabled,
    authToken,
    requestBodyLimitBytes,
    bridgeTimeoutMs,
    protocolVersion,
} = config

// ---- Bridge (IPC) ----
const bridge = new BridgeHub({
    host: ipcHost,
    port: ipcPort,
    timeoutMs: bridgeTimeoutMs,
})
bridge.start()

// ---- Helpers ----
function sendJson(res, statusCode, obj) {
    const body = JSON.stringify(obj)
    res.writeHead(statusCode, {
        "Content-Type": "application/json; charset=utf-8",
        "Content-Length": Buffer.byteLength(body),
        "Cache-Control": "no-store",
    })
    res.end(body)
}

function sendEmpty(res, statusCode, extraHeaders = null) {
    const headers = {
        "Content-Length": "0",
        "Cache-Control": "no-store",
        ...(extraHeaders || {}),
    }
    res.writeHead(statusCode, headers)
    res.end()
}

function isAllowedOrigin(origin) {
    // Validate Origin when present: allow only localhost/127.0.0.1.
    // Desktop clients sometimes send Origin: null; treat that as local.
    if (!origin) return true
    if (origin === "null") return true

    return (
        origin.startsWith("http://127.0.0.1") ||
        origin.startsWith("http://localhost") ||
        origin.startsWith("https://127.0.0.1") ||
        origin.startsWith("https://localhost")
    )
}

function requireAuth(req, res) {
    if (!authEnabled) return true

    const h = req.headers["authorization"] || ""
    const want = `Bearer ${authToken}`

    if (h !== want) {
        sendJson(res, 401, makeError(null, -32001, "Unauthorized"))
        return false
    }
    return true
}

function readBody(req, res) {
    return new Promise((resolve, reject) => {
        let data = ""
        let size = 0

        req.setEncoding("utf8")
        req.on("data", (chunk) => {
            size += chunk.length
            if (size > requestBodyLimitBytes) {
                sendJson(res, 413, makeError(null, -32002, "Request too large"))
                req.destroy()
                reject(new Error("too_large"))
                return
            }
            data += chunk
        })

        req.on("end", () => resolve(data))
        req.on("error", (e) => reject(e))
    })
}

function isJsonRpcNotification(msg) {
    return msg && msg.jsonrpc === "2.0" && typeof msg.method === "string" && (msg.id === undefined || msg.id === null)
}

function isJsonRpcResponse(msg) {
    // Client-to-server responses are allowed by transport; accept => 202 no body.
    return (
        msg &&
        msg.jsonrpc === "2.0" &&
        msg.method === undefined &&
        (Object.prototype.hasOwnProperty.call(msg, "result") || Object.prototype.hasOwnProperty.call(msg, "error"))
    )
}

function toolListResult() {
    return {
        tools: (Array.isArray(tools) ? tools : []).map((t) => ({
            name: t.name,
            description: t.description || "",
            inputSchema: t.inputSchema || { type: "object", properties: {}, additionalProperties: true },
        })),
    }
}

function initializeResult(params) {
    const pv =
        (params && typeof params.protocolVersion === "string" && params.protocolVersion) ||
        protocolVersion

    return {
        protocolVersion: pv,
        capabilities: {
            tools: { listChanged: false },
            resources: { subscribe: false, listChanged: false },
        },
        serverInfo: {
            name: "unity-mcp-server",
            title: "Unity MCP Server",
            version: "1.0.0",
        },
        instructions: "Unity MCP Server provides tools and resources for interacting with Unity Editor. Use tools/list to see available tools and resources/list to see available resources.",
    }
}

function resourceListResult() {
    return {
        resources: (Array.isArray(resources) ? resources : []).map((r) => ({
            uri: r.uri,
            name: r.name,
            description: r.description || "",
            mimeType: r.mimeType || "application/json",
        })),
        resourceTemplates: (Array.isArray(resourceTemplates) ? resourceTemplates : []).map((t) => ({
            uriTemplate: t.uriTemplate,
            name: t.name,
            description: t.description || "",
        })),
    }
}

function resolveBridgeToolName(requestedName) {
    if (!requestedName || typeof requestedName !== "string") return ""
    return toolNameToBridgeName[requestedName] || requestedName // accept dotted aliases too
}

async function handleSingleMessage(msg) {
    if (isJsonRpcResponse(msg)) return { kind: "accepted" }
    if (isJsonRpcNotification(msg)) {
        if (msg.method === "notifications/initialized") return { kind: "accepted" }
        return { kind: "accepted" }
    }

    if (!msg || msg.jsonrpc !== "2.0" || typeof msg.method !== "string" || (msg.id === undefined || msg.id === null)) {
        return { kind: "response", payload: makeError(msg && msg.id !== undefined ? msg.id : null, -32600, "Invalid Request") }
    }

    const id = msg.id
    const method = msg.method
    const params = msg.params || {}

    if (method === "initialize") {
        return { kind: "response", payload: makeResult(id, initializeResult(params)) }
    }

    if (method === "ping") {
        return { kind: "response", payload: makeResult(id, {}) }
    }

    if (method === "notifications/initialized") {
        // Some clients send it as a request
        return { kind: "response", payload: makeResult(id, {}) }
    }

    if (method === "tools/list") {
        return { kind: "response", payload: makeResult(id, toolListResult()) }
    }

    if (method === "resources/list") {
        return { kind: "response", payload: makeResult(id, resourceListResult()) }
    }

    if (method === "resources/read") {
        const uri = typeof params.uri === "string" ? params.uri : ""

        if (!uri) {
            return { kind: "response", payload: makeError(id, -32602, "Invalid params: missing uri") }
        }

        const parsed = parseResourceUri(uri)
        if (!parsed) {
            return { kind: "response", payload: makeError(id, -32602, `Invalid params: unknown resource URI '${uri}'`) }
        }

        if (!bridge.isConnected()) {
            const resErr = { contents: [{ uri, mimeType: "text/plain", text: "Unity bridge unavailable" }], isError: true }
            return { kind: "response", payload: makeResult(id, resErr) }
        }

        try {
            const args = { uri, ...parsed.args }
            const result = await bridge.callTool(parsed.bridgeName, args)

            const normalized =
                result && typeof result === "object"
                    ? result
                    : { contents: [{ uri, mimeType: "text/plain", text: String(result ?? "") }], isError: false }

            if (!Array.isArray(normalized.contents)) {
                normalized.contents = [{ uri, mimeType: "application/json", text: JSON.stringify(result) }]
            }

            return { kind: "response", payload: makeResult(id, normalized) }
        } catch (e) {
            const resErr = { contents: [{ uri, mimeType: "text/plain", text: `Resource read failed: ${e?.message || String(e)}` }], isError: true }
            return { kind: "response", payload: makeResult(id, resErr) }
        }
    }

    if (method === "tools/call") {
        const requestedName =
            typeof params.name === "string" ? params.name :
                (typeof params.tool === "string" ? params.tool : "")

        const args =
            (params && typeof params.arguments === "object" && params.arguments) ||
            (params && typeof params.args === "object" && params.args) ||
            {}

        if (!requestedName) {
            return { kind: "response", payload: makeError(id, -32602, "Invalid params: missing tool name") }
        }

        const bridgeToolName = resolveBridgeToolName(requestedName)
        if (!bridgeToolName) {
            return { kind: "response", payload: makeError(id, -32602, "Invalid params: bad tool name") }
        }

        if (!bridge.isConnected()) {
            const toolErr = { content: [{ type: "text", text: "Unity bridge unavailable" }], isError: true }
            return { kind: "response", payload: makeResult(id, toolErr) }
        }

        try {
            const result = await bridge.callTool(bridgeToolName, args)

            const normalized =
                result && typeof result === "object"
                    ? result
                    : { content: [{ type: "text", text: String(result ?? "") }], isError: false }

            if (!Array.isArray(normalized.content)) {
                normalized.content = [{ type: "text", text: JSON.stringify(normalized) }]
            }

            return { kind: "response", payload: makeResult(id, normalized) }
        } catch (e) {
            const toolErr = { content: [{ type: "text", text: `Tool call failed: ${e?.message || String(e)}` }], isError: true }
            return { kind: "response", payload: makeResult(id, toolErr) }
        }
    }

    return { kind: "response", payload: makeError(id, -32601, `Method not found: ${method}`) }
}

async function handlePost(req, res) {
    const origin = req.headers["origin"]
    if (!isAllowedOrigin(origin)) {
        sendJson(res, 403, makeError(null, -32003, "Forbidden origin"))
        return
    }

    if (!requireAuth(req, res)) return

    const body = await readBody(req, res).catch(() => null)
    if (body == null) return

    let payload
    try {
        payload = body.length ? JSON.parse(body) : null
    } catch {
        sendJson(res, 400, makeError(null, -32700, "Parse error"))
        return
    }

    if (Array.isArray(payload)) {
        if (payload.length === 0) {
            sendJson(res, 400, makeError(null, -32600, "Invalid Request"))
            return
        }

        const responses = []
        let sawRequestWithId = false

        for (const item of payload) {
            const r = await handleSingleMessage(item)
            if (r.kind === "response") {
                responses.push(r.payload)
                sawRequestWithId = true
            }
        }

        if (!sawRequestWithId) {
            sendEmpty(res, 202)
            return
        }

        sendJson(res, 200, responses)
        return
    }

    const r = await handleSingleMessage(payload)

    if (r.kind === "accepted") {
        sendEmpty(res, 202)
        return
    }

    sendJson(res, 200, r.payload)
}

function start() {
    const server = http.createServer(async (req, res) => {
        const u = new URL(req.url, `http://${req.headers.host || "127.0.0.1"}`)

        if (u.pathname !== "/mcp") {
            sendEmpty(res, 404)
            return
        }

        // Streamable HTTP spec: GET either returns SSE stream or 405.
        if (req.method === "GET") {
            sendEmpty(res, 405, { Allow: "POST, GET" })
            return
        }

        if (req.method !== "POST") {
            sendEmpty(res, 405, { Allow: "POST, GET" })
            return
        }

        const ct = (req.headers["content-type"] || "").toLowerCase()
        if (!ct.includes("application/json")) {
            sendJson(res, 415, makeError(null, -32004, "Unsupported Media Type"))
            return
        }

        await handlePost(req, res)
    })

    server.listen(httpPort, httpHost, () => {
        console.log(`[mcp] http listening on http://${httpHost}:${httpPort}/mcp`)
        console.log(`[mcp] ipc bridge on tcp://${ipcHost}:${ipcPort}`)
        console.log(`[mcp] bridge timeout = ${bridgeTimeoutMs}ms`)
        if (authEnabled) {
            console.log(`[mcp] bearer auth ON, token = ${authToken}`)
        } else {
            console.log("[mcp] bearer auth OFF")
        }
    })
}

start()
