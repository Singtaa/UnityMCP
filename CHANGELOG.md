# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`unity_assets_find` tool**: type-aware asset search wrapping `AssetDatabase.FindAssets` with Project-window query syntax (`t:Material`, `t:Prefab ui`, `l:MyLabel`), optional folder scoping (invalid folders are rejected up front instead of silently matching nothing), and a result cap (default 200) with the full match count reported.

- **`unity_eval` tool**: compile and run a C# snippet in the Editor with no domain reload. Uses the Roslyn compiler bundled inside the editor installation (probed and validated at first use, loaded via reflection so the package never binds to a specific Roslyn version; no compiler DLLs are shipped). Expression snippets return their value; statement snippets use `return`. Leading `using` lines are hoisted and common Unity/System namespaces are imported by default, with `Object`/`Random` aliased to `UnityEngine`. Compile diagnostics come back structured (severity, id, line, column). First eval in a session takes a few seconds (one-time reference metadata warm-up); subsequent evals compile in tens of milliseconds. Language ceiling is the bundled compiler (C# 9 on current Unity). Each eval loads a small in-memory assembly that persists until the next domain reload.

- **Multi-editor support**: multiple Unity Editors (different projects) can now run MCP servers side by side on the same machine, each with its own endpoint.
  - **Project identity handshake**: the launching Editor passes `MCP_PROJECT_ROOT` to the Node server. The TCP bridge hub answers a new `bridge.identify` probe (directly, no Unity round-trip) so an Editor can ask "whose server is on this port?" before adopting it, and rejects `bridge.hello` from a different project with `bridge.reject` instead of replacing the bridge. The C# client surfaces rejections as a rate-limited console warning.
  - **Automatic port allocation**: when the configured IPC port is owned by another project's server (or an unrelated process, e.g. Vite on 5173), a free HTTP/IPC port pair at the same offset (e.g. 5174/52101) is allocated and used for a dedicated server. Allocations are stored per-machine in `UserSettings/McpPortOverride.json` (gitignored) - never in the team-shared `ProjectSettings/McpSettings.json`, whose ports remain the preferred defaults - so one machine's port shuffle can't get committed and drift the team's endpoints. Manual port changes clear the local override. The `EADDRINUSE` startup fallback also verifies the occupant's identity and retries once on fresh ports instead of blindly adopting it. Free-port checks bind with `ExclusiveAddressUse` to avoid `SO_REUSEADDR` false-frees, and a non-responding listener gets a short grace re-probe before being treated as foreign (mid-startup/shutdown races).
  - `serverInfo.title` includes the project folder name so MCP clients can tell servers apart; the MCP Server window shows the live endpoint URL and keeps port fields in sync with auto-allocated values.
  - **One-time migration** for updating with the Editor open: a running pre-update server (which predates the identify handshake) is recognized via the legacy EditorPrefs PID and stopped, so the project keeps its configured ports instead of drifting to an allocated pair and orphaning the old process. The legacy machine-global lock file is also cleaned up.

### Fixed

- **Cross-project interference with multiple Editors open.** Three machine-global mechanisms made "last Editor wins": the active-client lock file lived in the system temp directory (now `Temp/UnityMcp_ActiveClient.lock` inside the project, per-project by construction and cleaned up by Unity on quit); the server PID was stored in machine-wide `EditorPrefs`, letting one Editor reattach to - and kill on quit - another project's server (now `SessionState`, scoped to the Editor instance); and a server whose port was already in use was adopted as "external" without checking which project it served (now identity-checked via `bridge.identify`).
- Leaked half-alive Node process when only the IPC port collided at startup (bridge hub logged `EADDRINUSE` but the HTTP server kept running). The residual process is now killed before adopting an external server or retrying.

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
