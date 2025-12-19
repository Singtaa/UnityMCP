"use strict"

/**
 * MCP Resources for Unity.
 * Resources provide read-only access to Unity state.
 */

const resources = [
    {
        uri: "unity://console/logs",
        name: "Console Logs",
        description: "Unity Editor console output (errors, warnings, logs)",
        mimeType: "application/json"
    },
    {
        uri: "unity://hierarchy",
        name: "Scene Hierarchy",
        description: "GameObject hierarchy of all loaded scenes",
        mimeType: "application/json"
    },
    {
        uri: "unity://tests/results",
        name: "Test Results",
        description: "Results from the most recent test run",
        mimeType: "application/json"
    },
    {
        uri: "unity://project/files",
        name: "Project Files",
        description: "Project file tree (respects .gitignore)",
        mimeType: "application/json"
    }
]

// Resource templates for parameterized access
const resourceTemplates = [
    {
        uriTemplate: "unity://hierarchy/{sceneName}",
        name: "Scene Hierarchy",
        description: "GameObject hierarchy for a specific scene"
    },
    {
        uriTemplate: "unity://project/files/{path}",
        name: "Project File",
        description: "Contents of a specific project file"
    }
]

// Map URI patterns to bridge tool names
const resourceToBridgeName = {
    "unity://console/logs": "unity.resource.console.logs",
    "unity://hierarchy": "unity.resource.hierarchy",
    "unity://tests/results": "unity.resource.tests.results",
    "unity://project/files": "unity.resource.project.files",
}

/**
 * Parse a resource URI and extract any parameters.
 * @param {string} uri - The resource URI
 * @returns {{ bridgeName: string, args: object } | null}
 */
function parseResourceUri(uri) {
    if (!uri || typeof uri !== "string") return null

    // Direct matches
    if (resourceToBridgeName[uri]) {
        return { bridgeName: resourceToBridgeName[uri], args: {} }
    }

    // Template matches
    if (uri.startsWith("unity://hierarchy/")) {
        const sceneName = uri.substring("unity://hierarchy/".length)
        return {
            bridgeName: "unity.resource.hierarchy",
            args: { scene: sceneName }
        }
    }

    if (uri.startsWith("unity://project/files/")) {
        const path = uri.substring("unity://project/files/".length)
        return {
            bridgeName: "unity.resource.project.files",
            args: { path }
        }
    }

    return null
}

module.exports = {
    resources,
    resourceTemplates,
    resourceToBridgeName,
    parseResourceUri,
}
