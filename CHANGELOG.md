# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Compile errors on Unity 6.5, where the `InstanceID` APIs (`GetInstanceID`, `EditorUtility.InstanceIDToObject`) become hard errors in favor of 64-bit `EntityId`. All usage now goes through `EntityIdCompat`, which uses `EntityId` on 6000.4+ and falls back to `InstanceID` on older versions (down to 2022.3). `FindCompat` similarly wraps the deprecated `FindObjectsByType(FindObjectsSortMode)` overloads. **Object ids now cross the MCP wire as JSON strings**: `EntityId` values exceed JavaScript's 2^53 safe-integer range, so raw JSON numbers get silently corrupted by the Node relay (verified live on 6000.5: `unity_component_set_enabled` by id failed until the switch to strings). Inputs accept both string and number; ids remain session-scoped as before.

### Changed

- Lowered minimum Unity version from `6000.0` to `2022.3` LTS. No code changes were required: all Unity APIs in use are available in 2022.2+, and internal UIElements reflection calls (`UIElementsRuntimeUtility.UpdatePanels`/`RenderOffscreenPanels`/`BaseRuntimePanel.RenderPanel`) have null-checks that degrade gracefully if signatures differ. Unity 6 remains the recommended target.

### Added

- **Capture tools** for grabbing the rendered output as PNG (returned as MCP image content blocks):
  - `unity_capture_panel` - Render a UI Toolkit `PanelSettings` to an off-screen `RenderTexture` and return the PNG. Works reliably in play mode (no Scene chrome, ideal for OneJS UI feedback loops). Auto-detects the active `PanelSettings` from `UIDocument`s in loaded scenes if `panelPath` is omitted. **Known limitation:** in edit mode the first capture after `targetTexture` is reassigned renders a blank texture; use play mode for now.
  - `unity_capture_game_view` - Capture the Game view to PNG. Uses `ScreenCapture.CaptureScreenshotAsTexture` in play mode. **Known limitation:** edit-mode capture is not supported in Unity 6.3+ because `PlayModeView.targetTexture` is not accessible via reflection in this version.
- **Image content block** support in `ContentBlock` (`data` + `mimeType` fields, omitted from JSON when null so existing text tools are unaffected). New helpers: `ToolResultUtil.Image` and `ToolResultUtil.ImageWithText`.

## [1.0.1] - 2026-03-04

### Fixed

- Track all `.meta` files in git so the package can be installed via UPM git URL without "missing .meta files" errors

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

### Improved

- **Test runner tools** stability and error handling:
  - All responses now include `status` field for programmatic error handling
  - Domain reload detection prevents calls during unstable period
  - Callback invocation tracking detects when test framework isn't ready
  - Helpful hints guide users to workarounds (e.g., use `unity_test_run` when listing fails)
  - Updated tool descriptions to clarify async nature and polling requirements
  - Note: `unity_test_list` may not work in Unity 6000.x beta; `unity_test_run` works reliably

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
