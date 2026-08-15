"use strict"

const { test } = require("node:test")
const assert = require("node:assert")
const fs = require("fs")
const os = require("os")
const path = require("path")

const {
    walkUpToUnityProject, findProjectRoot, readBeacon, isConnectionRefused,
    beaconKey, toolsCachePath, writeToolsCache, readToolsCache,
} = require("../src/stdio.js")

function makeUnityProject(root) {
    fs.mkdirSync(path.join(root, "ProjectSettings"), { recursive: true })
    fs.writeFileSync(path.join(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.5.2f1\n")
}

function tmpdir() {
    return fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-stdio-test-"))
}

test("walkUpToUnityProject finds root from nested dir", () => {
    const root = tmpdir()
    makeUnityProject(root)
    const nested = path.join(root, "Assets", "Scenes", "App")
    fs.mkdirSync(nested, { recursive: true })

    assert.strictEqual(walkUpToUnityProject(nested), root)
    assert.strictEqual(walkUpToUnityProject(root), root)
    fs.rmSync(root, { recursive: true, force: true })
})

test("walkUpToUnityProject returns null outside any project", () => {
    const dir = tmpdir()
    assert.strictEqual(walkUpToUnityProject(dir), null)
    fs.rmSync(dir, { recursive: true, force: true })
})

test("findProjectRoot prefers UNITY_MCP_PROJECT, then CLAUDE_PROJECT_DIR, then cwd", () => {
    const a = tmpdir(), b = tmpdir(), c = tmpdir()
    makeUnityProject(a)
    makeUnityProject(b)
    makeUnityProject(c)

    assert.ok(findProjectRoot({ UNITY_MCP_PROJECT: a, CLAUDE_PROJECT_DIR: b }, c).endsWith(path.basename(a)))
    assert.ok(findProjectRoot({ CLAUDE_PROJECT_DIR: b }, c).endsWith(path.basename(b)))
    assert.ok(findProjectRoot({}, c).endsWith(path.basename(c)))

    for (const d of [a, b, c]) fs.rmSync(d, { recursive: true, force: true })
})

test("findProjectRoot skips non-project env entries and falls through", () => {
    const notProject = tmpdir()
    const project = tmpdir()
    makeUnityProject(project)

    const found = findProjectRoot({ CLAUDE_PROJECT_DIR: notProject }, project)
    assert.ok(found && found.endsWith(path.basename(project)))

    fs.rmSync(notProject, { recursive: true, force: true })
    fs.rmSync(project, { recursive: true, force: true })
})

test("readBeacon reads a valid beacon", () => {
    const root = tmpdir()
    makeUnityProject(root)
    fs.mkdirSync(path.join(root, "Temp"), { recursive: true })
    fs.writeFileSync(
        path.join(root, "Temp", "UnityMcp_Endpoint.json"),
        JSON.stringify({ url: "http://127.0.0.1:5173/mcp", token: "abc", pid: 42 }))

    const beacon = readBeacon(root)
    assert.strictEqual(beacon.url, "http://127.0.0.1:5173/mcp")
    assert.strictEqual(beacon.token, "abc")
    fs.rmSync(root, { recursive: true, force: true })
})

test("isConnectionRefused walks nested and aggregate causes", () => {
    const direct = Object.assign(new Error("refused"), { code: "ECONNREFUSED" })
    assert.strictEqual(isConnectionRefused(direct), true)

    // Node fetch shape: TypeError('fetch failed') -> cause: AggregateError { errors: [...] }
    const agg = new Error("fetch failed")
    agg.cause = { errors: [{ code: "ECONNREFUSED" }, { code: "ECONNREFUSED" }] }
    assert.strictEqual(isConnectionRefused(agg), true)

    const reset = Object.assign(new Error("reset"), { code: "ECONNRESET" })
    assert.strictEqual(isConnectionRefused(reset), false)
    assert.strictEqual(isConnectionRefused(null), false)
})

test("readBeacon returns null for missing or malformed beacon", () => {
    const root = tmpdir()
    makeUnityProject(root)
    assert.strictEqual(readBeacon(root), null)

    fs.mkdirSync(path.join(root, "Temp"), { recursive: true })
    fs.writeFileSync(path.join(root, "Temp", "UnityMcp_Endpoint.json"), "{not json")
    assert.strictEqual(readBeacon(root), null)

    assert.strictEqual(readBeacon(null), null)
    fs.rmSync(root, { recursive: true, force: true })
})

test("beaconKey identifies an editor by url+pid, null when down", () => {
    assert.strictEqual(beaconKey(null), null)

    const up = { url: "http://127.0.0.1:5174/mcp", pid: 80952 }
    assert.strictEqual(beaconKey(up), "http://127.0.0.1:5174/mcp|80952")

    // A reopened editor keeps the port but never the pid: the key must change,
    // because that transition is what triggers the client-side re-list.
    const reopened = { url: "http://127.0.0.1:5174/mcp", pid: 81000 }
    assert.notStrictEqual(beaconKey(reopened), beaconKey(up))

    // A domain reload rewrites the beacon with identical url+pid: same key,
    // so the watcher stays silent.
    assert.strictEqual(beaconKey({ ...up }), beaconKey(up))
})

test("tools cache round-trips per project and rejects garbage", () => {
    const root = tmpdir()
    const cacheDir = tmpdir()
    makeUnityProject(root)

    assert.strictEqual(readToolsCache(root, cacheDir), null)

    const result = { tools: [{ name: "unity_eval" }, { name: "unity_scene_load" }] }
    writeToolsCache(root, result, cacheDir)
    assert.deepStrictEqual(readToolsCache(root, cacheDir), result)

    // Distinct projects with the same basename must not share an entry
    const sibling = path.join(tmpdir(), path.basename(root))
    fs.mkdirSync(sibling, { recursive: true })
    makeUnityProject(sibling)
    assert.notStrictEqual(toolsCachePath(root, cacheDir), toolsCachePath(sibling, cacheDir))
    assert.strictEqual(readToolsCache(sibling, cacheDir), null)

    // Malformed cache reads as no cache
    fs.writeFileSync(toolsCachePath(root, cacheDir), "{not json")
    assert.strictEqual(readToolsCache(root, cacheDir), null)

    // No project: no cache path, and both operations are safe no-ops
    assert.strictEqual(toolsCachePath(null, cacheDir), null)
    writeToolsCache(null, result, cacheDir)
    assert.strictEqual(readToolsCache(null, cacheDir), null)

    fs.rmSync(root, { recursive: true, force: true })
    fs.rmSync(cacheDir, { recursive: true, force: true })
})
