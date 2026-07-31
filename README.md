# Unity MCP Server

[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

[MCP](https://modelcontextprotocol.io/) server for the Unity Editor. Lets AI agents like Claude Code inspect, manipulate, test, and screenshot your project.

## Highlights

- **73 tools**: scenes, GameObjects, components, prefabs, transforms, tests, reflection + decompilation, C# eval, UI capture
- **Zero-config clients**: one click (or one command) per machine, then every project and editor connects automatically. No ports, no tokens, no per-project config
- **Multi-editor safe**: each open project runs its own server, sessions route to the right one by themselves
- **MCP resources**: console logs, scene hierarchy, test results, project files
- Unity 2022.3+ (Unity 6 recommended), Node.js 18+

## Install

Package Manager > `+` > Add package from git URL:

```
https://github.com/Singtaa/UnityMCP.git
```

Or clone / submodule into `Packages/com.singtaa.unity-mcp`.

## Setup (once per machine)

1. Open the project in Unity. Server auto-starts, launcher deploys to `~/.unity-mcp/stdio.js`
2. **Window > Unity MCP Server > Set up Claude Code**

Done. Terminal equivalent:

```bash
# macOS / Linux
claude mcp add --scope user --transport stdio unity -- node ~/.unity-mcp/stdio.js

# Windows
claude mcp add --scope user --transport stdio unity -- node "%USERPROFILE%\.unity-mcp\stdio.js"
```

Every Claude Code session inside any Unity project now reaches that project's own editor. Multiple editors side by side: each session finds its own. Editor closed or mid-reload: tools return a clear error and recover on their own.

Under the hood: the editor writes a per-session beacon (`Temp/UnityMcp_Endpoint.json`, removed on quit, never stale). The launcher resolves the session's project (`CLAUDE_PROJECT_DIR`, else cwd walk-up, `UNITY_MCP_PROJECT` overrides both) and proxies MCP to that project's live endpoint.

Other MCP clients: same launcher, or plain HTTP (endpoint + token shown in the window).

## Tools

| Group | Tools (`unity_` prefix) |
|---|---|
| Scene | `scene_list`, `scene_load`, `scene_save`, `scene_new`, `scene_close` |
| GameObject | `gameobject_create`, `gameobject_find`, `gameobject_delete`, `gameobject_set_active`, `gameobject_set_parent`, `gameobject_rename`, `gameobject_duplicate` |
| Component | `component_list`, `component_add`, `component_remove`, `component_set_enabled`, `component_get_properties`, `component_set_property` |
| Transform | `transform_get`, `transform_set`, `transform_translate`, `transform_rotate`, `transform_look_at`, `transform_reset` |
| Editor | `selection_get/set/focus`, `editor_execute_menu_item`, `editor_notification`, `editor_log`, `editor_get_state`, `editor_pause/step`, `undo_*`, `playmode_enter/exit` |
| Prefab | `prefab_load`, `prefab_save`, `prefab_get_hierarchy`, `prefab_find_component` |
| Test | `test_list`, `test_run`, `test_run_sync`, `test_get_results` |
| Capture | `capture_panel`, `capture_game_view` |
| Project & Assets | `project_list_files`, `project_read_text`, `project_write_text`, `assets_refresh`, `assets_import`, `assets_find` |
| Reflection | `reflection_search_types`, `reflection_get_type_info`, `reflection_get_method_info`, `reflection_get_public_api`, `reflection_get_assemblies`, `reflection_decompile`, `reflection_invoke_static` |
| Eval | `eval` |

Notable:

- `unity_eval`: compile + run a C# snippet in the editor, no domain reload. Expression form returns its value, statement form uses `return`. Common usings imported, leading `using` lines hoisted. Bundled Roslyn (C# 9 ceiling). First call warms up for a few seconds, then tens of ms per compile
- `unity_capture_panel`: renders a UI Toolkit `PanelSettings` to PNG offscreen, no scene chrome, works in edit and play mode. Auto-detects the active `UIDocument`
- `unity_assets_find`: Project-window query syntax (`t:Material`, `t:Prefab ui`, `l:MyLabel`), optional folder scoping, capped results with total count
- `unity_reflection_decompile`: full C# source of any loaded type or method
- Quirks: wait ~1s after a domain reload before test tools; `unity_capture_game_view` is play-mode-only on Unity 6.3+

## Resources

`unity://console/logs` · `unity://hierarchy` · `unity://hierarchy/{scene}` · `unity://tests/results` · `unity://project/files`

## Configuration

`ProjectSettings/McpSettings.json` (meant to be committed): HTTP port 5173, IPC port 52100, auto-start, auth token.

Ports taken by another project's server? A free pair is auto-allocated and stored per-machine in `UserSettings/McpPortOverride.json` (gitignored), so port shuffles never reach the team. Project identity is validated on every bridge connection: an editor can never adopt another project's server, even with misconfigured ports.

Manual server start: `node src/server.js` with `MCP_PROJECT_ROOT` set to the project path.

## Works with Unity CLI

Complementary, not competing:

- Unity CLI owns editor lifecycle: installs, `unity open`, builds, CI
- UnityMCP owns the live editor session: UI capture, decompilation, eval, test loops, zero-config routing
- Typical agent loop: `unity open <project>` via CLI, editor boots, server + beacon come up, launcher connects. No config on either side
- CLI-launched editors run unfocused / in the background. UnityMCP is built and tested for exactly that (background-safe startup, retry through domain reloads)
- No port or tool-name conflicts. Register both

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

Each open project runs its own Node server. The launcher is the shared front door that routes every session to the right one.

## Development

- Unity tests: Window > General > Test Runner (EditMode + PlayMode)
- Launcher tests: `npm test` in `Server~/`
- Changelog: [CHANGELOG.md](CHANGELOG.md)

## License

MIT. See [LICENSE](LICENSE).
