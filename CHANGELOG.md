# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Prefab tools** for editing prefab assets:
  - `unity_prefab_load` - Load prefab for inspection/editing
  - `unity_prefab_save` - Save prefab changes
  - `unity_prefab_get_hierarchy` - Get full prefab hierarchy with components
  - `unity_prefab_find_component` - Find component by child path and type
- **ObjectReference support** in `unity_component_set_property`:
  - Set references by instanceId (integer)
  - Set references by assetPath (string)
  - Set references using `{instanceId: int}` or `{assetPath: string}` object format
  - Clear references by setting to null

## [1.0.0] - 2025-01-XX

### Added

- Initial release of Unity MCP Server
- **68 Tools** for manipulating Unity Editor:
  - Scene management (list, load, save, new, close)
  - GameObject operations (create, delete, find, setActive, setParent, rename, duplicate)
  - Component management (list, add, remove, setEnabled, getProperties, setProperty)
  - Transform operations (get, set, translate, rotate, lookAt, reset)
  - Editor selection (get, set, focus)
  - Editor state and control (executeMenuItem, notification, log, getState, pause, step)
  - Undo/Redo operations
  - Test runner integration (list, run, runSync, getResults)
  - Project file operations (list, read, write, delete)
  - Asset database operations (refresh, import)
  - Play mode control (enter, exit)
  - Console log access
- **4 MCP Resources** for read-only access to Unity state:
  - `unity://console/logs` - Console output
  - `unity://hierarchy` - Scene hierarchy
  - `unity://tests/results` - Test results
  - `unity://project/files` - Project file tree
- **Auto-start Node.js server** - Server starts automatically when Unity opens
- **Editor Window** (`Window > Unity MCP Server`) for monitoring and configuration
- **Project-level settings** stored in `ProjectSettings/McpSettings.json`
- Multi-layered zombie thread prevention for domain reload safety
- Bearer token authentication (optional)
- Git-ignore aware file operations

### Technical Details

- Node.js server runs from `Server~/` folder (excluded from AssetDatabase)
- TCP NDJSON protocol for Unity-Node communication
- JSON-RPC 2.0 over HTTP for MCP clients
- Automatic npm install on first run
