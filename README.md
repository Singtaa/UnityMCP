# Unity MCP Server

[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server for Unity Editor, enabling AI assistants like Claude to interact with Unity projects.

## Features

- **73 Tools** for manipulating scenes, GameObjects, components, prefabs, transforms, reflection, C# eval, capture, and more
- **Zero-config Claude Code setup** - one click (or one command) per machine; every Unity project and editor then connects automatically, with no per-project ports or tokens
- **MCP Resources** for live access to console logs, scene hierarchy, test results, and project files
- **Auto-start Node.js server** - no manual setup required
- **Editor Window** for monitoring, configuration, and Claude Code setup
- **Full test coverage** with unit and integration tests

## Requirements

- Unity 2022.3 LTS or later (Unity 6 recommended)
- Node.js 18 or later

## Installation

### Direct `git clone`

```bash
cd YOUR_PROJECT/Packages
git clone https://github.com/Singtaa/UnityMCP.git com.singtaa.unity-mcp
```

### Via Git URL (Package Manager)

1. Open Window > Package Manager
2. Click the + button > Add package from git URL
3. Enter: `https://github.com/Singtaa/UnityMCP.git`

### Via git submodule

```bash
git submodule add https://github.com/Singtaa/UnityMCP.git Packages/com.singtaa.unity-mcp
```

## Quick Start

1. Install the package
2. Open the project once in Unity (the server starts automatically and deploys the stdio launcher to `~/.unity-mcp/stdio.js`)
3. Open **Window > Unity MCP Server** and click **Set up Claude Code** - done. This registers the launcher with Claude Code once per machine (user scope), never per project.

Prefer the terminal? The button runs the equivalent of:

```bash
# macOS / Linux
claude mcp add --scope user --transport stdio unity -- node ~/.unity-mcp/stdio.js

# Windows
claude mcp add --scope user --transport stdio unity -- node "%USERPROFILE%\.unity-mcp\stdio.js"
```

That's the entire setup. Every Claude Code session started inside any Unity project on the machine now routes to that project's own editor automatically - no ports, no tokens, no per-project configuration. Run multiple editors side by side; each session finds its own. While an editor is closed or reloading, tools return a clear error and recover on their own.

How it works: each editor writes a per-session endpoint beacon (`Temp/UnityMcp_Endpoint.json` - removed by Unity on quit, so it can never go stale), and the launcher resolves the session's Unity project (via `CLAUDE_PROJECT_DIR`, or by walking up from the working directory; `UNITY_MCP_PROJECT` overrides both) and proxies MCP to that project's live endpoint.

Other MCP clients can either use the same stdio launcher or connect over HTTP directly (see below).

## Configuration

Settings are stored in `ProjectSettings/McpSettings.json`:

| Setting | Default | Description |
|---------|---------|-------------|
| HTTP Port | 5173 | Port for MCP HTTP server |
| IPC Port | 52100 | Port for Unity-Node TCP bridge |
| Auto Start | true | Start server on Unity launch |
| Auth Enabled | true | Require bearer token authentication |

The `McpSettings.json` ports are the team-preferred defaults (the file is meant to be committed). If they're taken when the server starts (typically by another Unity project's MCP server), a free port pair is allocated automatically and stored per-machine in `UserSettings/McpPortOverride.json` (gitignored), so one machine's port shuffle never gets committed to the team. Changing ports manually in the MCP Server window updates the shared settings and clears the local override. The window always shows the project's current endpoint URL.

## Multiple Unity Editors

Each open project runs its own server on its own port pair, so multiple Editors can be used side by side, each with its own MCP endpoint:

1. The first project gets the default ports (HTTP 5173 / IPC 52100)
2. The next project detects the ports are taken, asks the running server which project it belongs to (`bridge.identify`), and auto-allocates the next free pair (e.g. 5174/52101)
3. Allocated ports are persisted per-machine, so endpoints stay stable across sessions

The stdio launcher (see Quick Start) routes each Claude Code session to its own project automatically, so none of this needs manual attention. Clients connecting over raw HTTP instead should use the project's own endpoint (shown in Window > Unity MCP Server) with that project's own auth token. The server also validates project identity on every bridge connection, so an Editor can never take over a server belonging to a different project - even with misconfigured ports.

If you start the Node server manually (`node src/server.js`), set `MCP_PROJECT_ROOT` to the project path so Editors can identify it; without it, the server accepts whichever project connects first.

## Available Tools

### Scene Management
- `unity_scene_list` - List all loaded scenes
- `unity_scene_load` - Load a scene
- `unity_scene_save` - Save scene(s)
- `unity_scene_new` - Create a new scene
- `unity_scene_close` - Close a scene

### GameObject Operations
- `unity_gameobject_create` - Create GameObjects (with optional primitives)
- `unity_gameobject_find` - Find GameObjects by name, tag, or path
- `unity_gameobject_delete` - Delete GameObjects
- `unity_gameobject_set_active` - Enable/disable GameObjects
- `unity_gameobject_set_parent` - Reparent GameObjects
- `unity_gameobject_rename` - Rename GameObjects
- `unity_gameobject_duplicate` - Duplicate GameObjects

### Component Management
- `unity_component_list` - List components on a GameObject
- `unity_component_add` - Add components
- `unity_component_remove` - Remove components
- `unity_component_set_enabled` - Enable/disable components
- `unity_component_get_properties` - Get component properties
- `unity_component_set_property` - Set component properties

### Transform Operations
- `unity_transform_get` - Get position/rotation/scale
- `unity_transform_set` - Set position/rotation/scale
- `unity_transform_translate` - Move by delta
- `unity_transform_rotate` - Rotate by euler angles
- `unity_transform_look_at` - Orient toward target
- `unity_transform_reset` - Reset to identity

### Editor Operations
- `unity_selection_get/set/focus` - Editor selection
- `unity_editor_execute_menu_item` - Execute menu commands
- `unity_editor_notification` - Show notifications
- `unity_editor_log` - Log to console
- `unity_editor_get_state` - Get editor state
- `unity_editor_pause/step` - Playmode control
- `unity_undo_*` - Undo/redo operations

### Prefab Operations
- `unity_prefab_load` - Load a prefab asset for inspection/editing
- `unity_prefab_save` - Save changes to a prefab
- `unity_prefab_get_hierarchy` - Get full prefab hierarchy
- `unity_prefab_find_component` - Find component within prefab by path

### Testing
- `unity_test_list` - List available tests (returns structured JSON with `status` field)
- `unity_test_run` - Run tests asynchronously, returns `runId` for polling
- `unity_test_run_sync` - Start EditMode tests (not truly sync; poll `get_results`)
- `unity_test_get_results` - Get test results by `runId`

> **Note:** After domain reload (script recompilation), wait ~1 second before calling test tools. If `unity_test_list` returns `status: "not_ready"`, you can still run tests directly with `unity_test_run` (without filter) and see all tests in the results.

### Capture
- `unity_capture_panel` - Render a UI Toolkit `PanelSettings` to a PNG (returned as an MCP `image` content block). Renders to an off-screen `RenderTexture` so the output has no Scene chrome. Auto-detects the active panel via `UIDocument`s in loaded scenes if `panelPath` is omitted.
- `unity_capture_game_view` - Capture the Game view to a PNG. Uses `ScreenCapture.CaptureScreenshotAsTexture` in play mode.

> **Note:** `unity_capture_game_view` only works in play mode in Unity 6.3+ (`PlayModeView.targetTexture` is not exposed via reflection in edit mode). `unity_capture_panel` works in both modes.

### Project & Assets
- `unity_project_list_files` - List project files
- `unity_project_read_text` - Read text files
- `unity_project_write_text` - Write text files
- `unity_assets_refresh` - Refresh AssetDatabase
- `unity_assets_import` - Import specific assets
- `unity_assets_find` - Type-aware asset search (`AssetDatabase.FindAssets`): `t:Material`, `t:Prefab ui`, `l:MyLabel`, optional folder scoping, capped results with total count

### Reflection & Decompilation
- `unity_reflection_search_types` - Search for types by name pattern across all assemblies
- `unity_reflection_get_type_info` - Get detailed structured JSON data about a type's members
- `unity_reflection_get_method_info` - Get method overloads with full parameter details
- `unity_reflection_get_public_api` - Get concise C# interface stub (best for quick API overview)
- `unity_reflection_get_assemblies` - List all loaded assemblies
- `unity_reflection_decompile` - Decompile type/method to C# source code
- `unity_reflection_invoke_static` - Invoke parameterless static methods/properties

### Eval
- `unity_eval` - Compile and run a C# snippet in the Editor (no domain reload). Expression snippets return their value (`Selection.activeGameObject.name`); statement snippets use `return`. Common Unity/System namespaces are imported by default and leading `using` lines are hoisted. Uses the Roslyn bundled with the editor (C# 9 ceiling); first call in a session takes a few seconds, subsequent compiles run in tens of milliseconds. Prefer dedicated tools when one exists - eval covers the long tail.

## MCP Resources

| Resource URI | Description |
|--------------|-------------|
| `unity://console/logs` | Live console output |
| `unity://hierarchy` | Scene hierarchy |
| `unity://hierarchy/{scene}` | Specific scene hierarchy |
| `unity://tests/results` | Latest test results |
| `unity://project/files` | Project file tree |

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      AI Assistant                           │
├──────────────────────────────┬──────────────────────────────┤
│  stdio launcher              │  (or direct HTTP clients)    │
│  ~/.unity-mcp/stdio.js       │                              │
│  resolves the session's      │                              │
│  project, reads its beacon   │                              │
│  Temp/UnityMcp_Endpoint.json │                              │
├──────────────────────────────┴──────────────────────────────┤
│           HTTP JSON-RPC (Port 5173, per project)            │
├─────────────────────────────────────────────────────────────┤
│              Node.js MCP Server (Server~/)                  │
├─────────────────────────────────────────────────────────────┤
│                TCP NDJSON (Port 52100)                      │
├─────────────────────────────────────────────────────────────┤
│                Unity Editor C# Bridge                       │
│  (McpBridge → ToolRegistry → MainThreadDispatcher)          │
├─────────────────────────────────────────────────────────────┤
│                     Unity APIs                              │
└─────────────────────────────────────────────────────────────┘
```

Each open project runs its own Node server; the launcher is the shared front door that routes every session to the right one.

## Development

### Running Tests

Open Window > General > Test Runner and run:
- **EditMode** tests for unit and integration tests
- **PlayMode** tests for runtime behavior

The Node-side tests (stdio launcher project resolution, beacon parsing, retry classification) run with `npm test` inside `Server~/`.

### Building the Node Server

The Node.js server is in `Server~/`. Dependencies are installed automatically on first run.

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please read our contributing guidelines and submit PRs to the main branch.
