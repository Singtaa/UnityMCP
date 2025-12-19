const crypto = require("crypto")

function parseBool(v, def) {
    if (v === undefined || v === null) return def
    const s = String(v).trim().toLowerCase()
    if (s === "1" || s === "true" || s === "yes" || s === "on") return true
    if (s === "0" || s === "false" || s === "no" || s === "off") return false
    return def
}

function parseIntSafe(v, def) {
    const n = Number.parseInt(v, 10)
    return Number.isFinite(n) ? n : def
}

function getAuthToken(requireAuth) {
    if (!requireAuth) return ""
    const envToken = process.env.MCP_TOKEN && String(process.env.MCP_TOKEN).trim()
    if (envToken) return envToken
    return crypto.randomBytes(24).toString("base64url")
}

const requireAuth = parseBool(process.env.MCP_REQUIRE_AUTH, true)

// ---- Single Source of Truth ----
// All defaults and environment parsing happens here.
// server.js should use this object directly without additional fallbacks.
const config = {
    // HTTP server
    httpHost: "127.0.0.1",
    httpPort: parseIntSafe(process.env.MCP_HTTP_PORT, 5173),

    // TCP bridge (IPC with Unity)
    ipcHost: "127.0.0.1",
    ipcPort: parseIntSafe(process.env.MCP_IPC_PORT, 52100),

    // Auth
    authEnabled: requireAuth,
    authToken: getAuthToken(requireAuth),

    // Limits
    requestBodyLimitBytes: parseIntSafe(process.env.MCP_HTTP_BODY_LIMIT, 2 * 1024 * 1024),
    bridgeTimeoutMs: parseIntSafe(process.env.MCP_BRIDGE_TIMEOUT_MS, 8000),

    // Protocol
    protocolVersion: "2025-11-25",

    // CORS
    allowOriginRegex: /^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/i
}

module.exports = { config }
