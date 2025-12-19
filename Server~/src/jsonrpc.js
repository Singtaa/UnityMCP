function makeError(id, code, message, data) {
    const err = { code, message }
    if (data !== undefined) err.data = data
    return { jsonrpc: "2.0", id: id ?? null, error: err }
}

function makeResult(id, result) {
    return { jsonrpc: "2.0", id, result }
}

function isValidRequest(obj) {
    return obj && obj.jsonrpc === "2.0" && typeof obj.method === "string"
}

module.exports = { makeError, makeResult, isValidRequest }
