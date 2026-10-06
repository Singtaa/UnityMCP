"use strict"

/**
 * Server logging that never blocks the event loop.
 *
 * Unity starts this server with stdout and stderr redirected to pipes and reads
 * them only until its first domain reload: after one, the Editor reattaches to
 * the server by PID and nothing reads those pipes again. On Windows Node writes
 * to a pipe synchronously, so once its small buffer filled, a console.log
 * stopped the whole server (HTTP included) until something let go, and every
 * play-mode entry spent about 8 s that way before the Editor could answer.
 *
 * So when the Editor names a file (MCP_LOG_FILE), everything goes there through
 * an asynchronous stream, and the pipes only carry `startup` lines: the ones
 * the Editor waits for while it is still reading, before any reload. A server
 * started by hand has no MCP_LOG_FILE and logs to the console as before.
 */

const fs = require("fs")
const path = require("path")

const file = (process.env.MCP_LOG_FILE || "").trim()
let stream = null
if (file) {
    try {
        fs.mkdirSync(path.dirname(file), { recursive: true })
        stream = fs.createWriteStream(file, { flags: "w" })
        // A log that cannot be written is not worth stopping the server for.
        stream.on("error", () => { stream = null })
    } catch {
        stream = null
    }
}

function toFile(level, text) {
    if (!stream) return false
    stream.write(`${new Date().toISOString()} ${level} ${text}\n`)
    return true
}

/** Routine output. */
function log(text) {
    if (!toFile("info", text)) console.log(text)
}

/** A problem worth reading later. */
function error(text) {
    if (!toFile("error", text)) console.error(text)
}

/** A line the launching Editor watches the console for: always on the console, and in the file. */
function startup(text) {
    toFile("info", text)
    console.log(text)
}

/** A startup failure the Editor watches stderr for (EADDRINUSE): always on stderr, and in the file. */
function startupError(text) {
    toFile("error", text)
    console.error(text)
}

module.exports = { log, error, startup, startupError, file }
