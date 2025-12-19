# Unity MCP Server

[![Unity 6+](https://img.shields.io/badge/Unity-6%2B-blue.svg)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server for Unity Editor, enabling AI assistants like Claude to interact with Unity projects.

## Features

- **68 Tools** for manipulating scenes, GameObjects, components, transforms, and more
- **MCP Resources** for live access to console logs, scene hierarchy, test results, and project files
- **Auto-start Node.js server** - no manual setup required
- **Editor Window** for monitoring and configuration
- **Full test coverage** with unit and integration tests

## Requirements

- Unity 6 or later
- Node.js 18 or later

## Installation

### Via Git URL (Package Manager)

1. Open Window > Package Manager
2. Click the + button > Add package from git URL
3. Enter: `https://github.com/singtaa/unity-mcp.git`

### Via git submodule

```bash
git submodule add https://github.com/singtaa/unity-mcp.git Packages/com.singtaa.unity-mcp
```

## Quick Start

1. Install the package
2. Open Window > Unity MCP Server
3. The server starts automatically when Unity opens
4. Configure your AI assistant to connect to `http://127.0.0.1:5173/mcp`

## Configuration

Settings are stored in `ProjectSettings/McpSettings.json`:

| Setting | Default | Description |
|---------|---------|-------------|
| HTTP Port | 5173 | Port for MCP HTTP server |
| IPC Port | 52100 | Port for Unity-Node TCP bridge |
| Auto Start | true | Start server on Unity launch |
| Auth Enabled | true | Require bearer token authentication |

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

### Testing
- `unity_test_list` - List available tests
- `unity_test_run` - Run tests asynchronously
- `unity_test_run_sync` - Run tests synchronously
- `unity_test_get_results` - Get test results

### Project & Assets
- `unity_project_list_files` - List project files
- `unity_project_read_text` - Read text files
- `unity_project_write_text` - Write text files
- `unity_assets_refresh` - Refresh AssetDatabase
- `unity_assets_import` - Import specific assets

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
│                      AI Assistant                            │
├─────────────────────────────────────────────────────────────┤
│                 HTTP JSON-RPC (Port 5173)                   │
├─────────────────────────────────────────────────────────────┤
│              Node.js MCP Server (Server~/)                  │
├─────────────────────────────────────────────────────────────┤
│                TCP NDJSON (Port 52100)                      │
├─────────────────────────────────────────────────────────────┤
│                Unity Editor C# Bridge                        │
│  (McpBridge → ToolRegistry → MainThreadDispatcher)          │
├─────────────────────────────────────────────────────────────┤
│                     Unity APIs                               │
└─────────────────────────────────────────────────────────────┘
```

## Development

### Running Tests

Open Window > General > Test Runner and run:
- **EditMode** tests for unit and integration tests
- **PlayMode** tests for runtime behavior

### Building the Node Server

The Node.js server is in `Server~/`. Dependencies are installed automatically on first run.

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please read our contributing guidelines and submit PRs to the main branch.
