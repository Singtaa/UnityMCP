"use strict"

const _defs = [
    // MARK: Bridge
    {
        safeName: "unity_bridge_ping",
        bridgeName: "unity.bridge.ping",
        description: "Quick health check. Returns pong when Unity bridge is responsive.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_bridge_mainthread_ping",
        bridgeName: "unity.bridge.mainthreadPing",
        description: "Main-thread health check. Returns pong-mainthread when dispatcher is working.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_bridge_dispatcher_status",
        bridgeName: "unity.bridge.dispatcherStatus",
        description: "Dispatcher diagnostics (installed, lastTickAgeMs, queued/executed). Runs on TCP thread.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },

    // MARK: Playmode
    {
        safeName: "unity_playmode_enter",
        bridgeName: "unity.playmode.enter",
        description: "Enter Play Mode (fire-and-forget).",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_playmode_exit",
        bridgeName: "unity.playmode.exit",
        description: "Exit Play Mode (fire-and-forget).",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },

    // MARK: Console
    {
        safeName: "unity_console_logs",
        bridgeName: "unity.console.logs",
        description: "Get console logs. Uses Unity Console reflection when possible, otherwise fallback capture.",
        inputSchema: {
            type: "object",
            properties: { maxEntries: { type: "integer", minimum: 1, maximum: 5000 } },
            additionalProperties: false,
        },
    },

    // MARK: Hierarchy (legacy)
    {
        safeName: "unity_hierarchy_list",
        bridgeName: "unity.hierarchy.list",
        description: "List all GameObjects in all open scenes (Hierarchy-style). Consider using unity_gameobject_find instead.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },

    // MARK: Project
    {
        safeName: "unity_project_list_files",
        bridgeName: "unity.project.listFiles",
        description: "List all files/dirs under project root excluding .git and root .gitignore matches.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_project_read_text",
        bridgeName: "unity.project.readText",
        description: "Read a UTF-8 text file under project root (must not be gitignored).",
        inputSchema: {
            type: "object",
            properties: { path: { type: "string" } },
            required: ["path"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_project_write_text",
        bridgeName: "unity.project.writeText",
        description: "Write a UTF-8 text file under project root (must not be gitignored). Schedules refresh/compilation after returning.",
        inputSchema: {
            type: "object",
            properties: {
                path: { type: "string" },
                text: { type: "string" },
                createDirs: { type: "boolean" },
            },
            required: ["path", "text"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_project_delete_file",
        bridgeName: "unity.project.deleteFile",
        description: "Delete an allowed file under project root (must not be gitignored).",
        inputSchema: {
            type: "object",
            properties: { path: { type: "string" } },
            required: ["path"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_scripts_status",
        bridgeName: "unity.scripts.status",
        description: "Compilation/playmode status snapshot.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_scripts_recompile",
        bridgeName: "unity.scripts.recompile",
        description: "Request script recompilation and domain reload. Use after modifying .cs files when Unity is in the background.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_assets_refresh",
        bridgeName: "unity.assets.refresh",
        description: "Refresh the AssetDatabase to detect file changes. Use when files have been modified outside Unity.",
        inputSchema: {
            type: "object",
            properties: {
                forceUpdate: { type: "boolean", default: false, description: "Force reimport of all assets" },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_assets_import",
        bridgeName: "unity.assets.import",
        description: "Reimport specific assets by path. Use when you need to force Unity to reimport particular assets (e.g., after modifying import settings or when assets appear stale).",
        inputSchema: {
            type: "object",
            properties: {
                paths: {
                    type: "array",
                    items: { type: "string" },
                    description: "Array of asset paths to import (e.g., ['Assets/Textures/icon.png', 'Assets/Models/player.fbx']). Paths without 'Assets/' prefix will have it added automatically."
                },
                forceUpdate: {
                    type: "boolean",
                    default: false,
                    description: "Force reimport even if Unity thinks the asset is up to date"
                },
            },
            required: ["paths"],
            additionalProperties: false,
        },
    },

    // MARK: Scene
    {
        safeName: "unity_scene_list",
        bridgeName: "unity.scene.list",
        description: "List all loaded scenes with their status.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_scene_save",
        bridgeName: "unity.scene.save",
        description: "Save scene(s). Omit 'scene' to save all open scenes.",
        inputSchema: {
            type: "object",
            properties: { scene: { type: "string", description: "Scene name to save. Omit to save all." } },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_scene_load",
        bridgeName: "unity.scene.load",
        description: "Load a scene by path. mode=single|additive.",
        inputSchema: {
            type: "object",
            properties: {
                path: { type: "string", description: "Scene asset path (e.g., Assets/Scenes/Main.unity)" },
                mode: { type: "string", enum: ["single", "additive"], default: "single" },
            },
            required: ["path"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_scene_new",
        bridgeName: "unity.scene.new",
        description: "Create a new scene. setup=default|empty.",
        inputSchema: {
            type: "object",
            properties: {
                setup: { type: "string", enum: ["default", "empty"], default: "default" },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_scene_close",
        bridgeName: "unity.scene.close",
        description: "Close a scene by name.",
        inputSchema: {
            type: "object",
            properties: {
                scene: { type: "string", description: "Scene name to close" },
                save: { type: "boolean", default: false },
            },
            required: ["scene"],
            additionalProperties: false,
        },
    },

    // MARK: GameObject
    {
        safeName: "unity_gameobject_create",
        bridgeName: "unity.gameobject.create",
        description: "Create a GameObject. Optional primitive: Cube, Sphere, Capsule, Cylinder, Plane, Quad.",
        inputSchema: {
            type: "object",
            properties: {
                name: { type: "string", default: "GameObject" },
                parent: { type: "string", description: "Parent path, #instanceId, or SceneName:/path" },
                primitive: { type: "string", enum: ["Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad"] },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_gameobject_delete",
        bridgeName: "unity.gameobject.delete",
        description: "Delete a GameObject by path or #instanceId.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string", description: "Path, #instanceId, or SceneName:/path" },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_gameobject_find",
        bridgeName: "unity.gameobject.find",
        description: "Find GameObjects by name substring, tag, or exact path.",
        inputSchema: {
            type: "object",
            properties: {
                name: { type: "string", description: "Name substring to search" },
                tag: { type: "string", description: "Tag to filter by" },
                path: { type: "string", description: "Exact path to lookup" },
                maxResults: { type: "integer", default: 100, maximum: 1000 },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_gameobject_set_active",
        bridgeName: "unity.gameobject.setActive",
        description: "Enable or disable a GameObject.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                active: { type: "boolean", default: true },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_gameobject_set_parent",
        bridgeName: "unity.gameobject.setParent",
        description: "Reparent a GameObject. Omit parent to unparent (move to scene root).",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                parent: { type: "string", description: "New parent. Omit to unparent." },
                worldPositionStays: { type: "boolean", default: true },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_gameobject_rename",
        bridgeName: "unity.gameobject.rename",
        description: "Rename a GameObject.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                name: { type: "string" },
            },
            required: ["target", "name"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_gameobject_duplicate",
        bridgeName: "unity.gameobject.duplicate",
        description: "Duplicate a GameObject (with all children and components).",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },

    // MARK: Component
    {
        safeName: "unity_component_list",
        bridgeName: "unity.component.list",
        description: "List all components on a GameObject.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_component_add",
        bridgeName: "unity.component.add",
        description: "Add a component by type name (e.g., Rigidbody, BoxCollider, AudioSource).",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                type: { type: "string", description: "Component type name" },
            },
            required: ["target", "type"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_component_remove",
        bridgeName: "unity.component.remove",
        description: "Remove a component by type name or componentInstanceId.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                type: { type: "string" },
                componentInstanceId: { type: "integer" },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_component_set_enabled",
        bridgeName: "unity.component.setEnabled",
        description: "Enable or disable a Behaviour component.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                type: { type: "string" },
                componentInstanceId: { type: "integer" },
                enabled: { type: "boolean", default: true },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_component_get_properties",
        bridgeName: "unity.component.getProperties",
        description: "Get serialized properties of a component as JSON.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                type: { type: "string" },
                componentInstanceId: { type: "integer" },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_component_set_property",
        bridgeName: "unity.component.setProperty",
        description: "Set a serialized property on a component.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                type: { type: "string" },
                componentInstanceId: { type: "integer" },
                property: { type: "string", description: "Property path (e.g., m_Mass, m_IsKinematic)" },
                value: { description: "Value to set (type depends on property)" },
            },
            required: ["target", "property", "value"],
            additionalProperties: false,
        },
    },

    // MARK: Transform
    {
        safeName: "unity_transform_get",
        bridgeName: "unity.transform.get",
        description: "Get transform position/rotation/scale. space=world|local.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                space: { type: "string", enum: ["world", "local"], default: "world" },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_transform_set",
        bridgeName: "unity.transform.set",
        description: "Set transform values. Arrays are [x, y, z].",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                space: { type: "string", enum: ["world", "local"], default: "local" },
                position: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3 },
                rotation: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3, description: "Euler angles" },
                scale: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3, description: "Only works with space=local" },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_transform_translate",
        bridgeName: "unity.transform.translate",
        description: "Move a transform by delta. space=self|world.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                delta: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3 },
                space: { type: "string", enum: ["self", "world"], default: "self" },
            },
            required: ["target", "delta"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_transform_rotate",
        bridgeName: "unity.transform.rotate",
        description: "Rotate a transform by euler angles. space=self|world.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                euler: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3 },
                space: { type: "string", enum: ["self", "world"], default: "self" },
            },
            required: ["target", "euler"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_transform_look_at",
        bridgeName: "unity.transform.lookAt",
        description: "Orient transform to look at a point or another GameObject.",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                point: { type: "array", items: { type: "number" }, minItems: 3, maxItems: 3 },
                lookAtTarget: { type: "string", description: "Alternative: look at another GameObject" },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_transform_reset",
        bridgeName: "unity.transform.reset",
        description: "Reset transform to identity (local pos=0, rot=0, scale=1).",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string" },
                position: { type: "boolean", default: true },
                rotation: { type: "boolean", default: true },
                scale: { type: "boolean", default: true },
            },
            required: ["target"],
            additionalProperties: false,
        },
    },

    // MARK: Selection
    {
        safeName: "unity_selection_get",
        bridgeName: "unity.selection.get",
        description: "Get currently selected GameObjects in the Editor.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_selection_set",
        bridgeName: "unity.selection.set",
        description: "Set Editor selection. Pass empty array to clear.",
        inputSchema: {
            type: "object",
            properties: {
                targets: { type: "array", items: { type: "string" }, description: "Array of paths or #instanceIds" },
            },
            required: ["targets"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_selection_focus",
        bridgeName: "unity.selection.focus",
        description: "Focus SceneView camera on a GameObject (Frame Selected).",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string", description: "Omit to focus current selection" },
            },
            additionalProperties: false,
        },
    },

    // MARK: Editor
    {
        safeName: "unity_editor_execute_menu_item",
        bridgeName: "unity.editor.executeMenuItem",
        description: "Execute any Unity menu command (e.g., 'GameObject/Create Empty', 'Edit/Play').",
        inputSchema: {
            type: "object",
            properties: {
                menuPath: { type: "string" },
            },
            required: ["menuPath"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_editor_notification",
        bridgeName: "unity.editor.notification",
        description: "Show a notification toast in the SceneView.",
        inputSchema: {
            type: "object",
            properties: {
                message: { type: "string" },
                duration: { type: "number", default: 2 },
            },
            required: ["message"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_editor_log",
        bridgeName: "unity.editor.log",
        description: "Log a message to Unity Console. type=log|warning|error.",
        inputSchema: {
            type: "object",
            properties: {
                message: { type: "string" },
                type: { type: "string", enum: ["log", "warning", "error"], default: "log" },
            },
            required: ["message"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_editor_get_state",
        bridgeName: "unity.editor.getState",
        description: "Get Editor state (isPlaying, isPaused, isCompiling, etc.).",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_editor_pause",
        bridgeName: "unity.editor.pause",
        description: "Pause or unpause play mode.",
        inputSchema: {
            type: "object",
            properties: {
                pause: { type: "boolean", description: "Omit to toggle" },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_editor_step",
        bridgeName: "unity.editor.step",
        description: "Step one frame (only works when paused in play mode).",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },

    // MARK: Undo
    {
        safeName: "unity_undo_perform",
        bridgeName: "unity.undo.perform",
        description: "Perform undo (Ctrl+Z equivalent).",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_redo_perform",
        bridgeName: "unity.redo.perform",
        description: "Perform redo (Ctrl+Y equivalent).",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_undo_get_current_group",
        bridgeName: "unity.undo.getCurrentGroup",
        description: "Get current undo group info.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },
    {
        safeName: "unity_undo_collapse",
        bridgeName: "unity.undo.collapse",
        description: "Collapse all undo operations since groupId into a single undo step.",
        inputSchema: {
            type: "object",
            properties: {
                groupId: { type: "integer", description: "Omit to use current group" },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_undo_set_group_name",
        bridgeName: "unity.undo.setGroupName",
        description: "Set the name of the current undo group.",
        inputSchema: {
            type: "object",
            properties: {
                name: { type: "string" },
            },
            required: ["name"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_undo_clear_all",
        bridgeName: "unity.undo.clearAll",
        description: "Clear entire undo history. Use with caution.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
    },

    // MARK: Test Runner
    {
        safeName: "unity_test_list",
        bridgeName: "unity.test.list",
        description: "List all available tests in the project. Returns test names, full names, and categories. NOTE: If status='stabilizing' or status='compiling', wait ~1 second and retry.",
        inputSchema: {
            type: "object",
            properties: {
                testMode: {
                    type: "string",
                    enum: ["editmode", "playmode", "all"],
                    default: "all",
                    description: "Filter tests by mode. 'editmode' for Editor tests, 'playmode' for Play Mode tests, 'all' for both."
                },
                nameFilter: {
                    type: "string",
                    description: "Filter tests by name (case-insensitive substring match)"
                },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_test_run",
        bridgeName: "unity.test.run",
        description: "Run Unity tests asynchronously. Returns a runId - poll unity_test_get_results to check status and get results. If status='stabilizing', wait ~1 second and retry.",
        inputSchema: {
            type: "object",
            properties: {
                testMode: {
                    type: "string",
                    enum: ["editmode", "playmode", "all"],
                    default: "editmode",
                    description: "Which test mode to run. PlayMode tests require entering Play Mode."
                },
                testFilter: {
                    type: "string",
                    description: "Comma-separated list of test names or partial names to run. Omit to run all tests."
                },
                categoryFilter: {
                    type: "string",
                    description: "Comma-separated list of test categories to include."
                },
                assemblyFilter: {
                    type: "string",
                    description: "Comma-separated list of assembly names to include (e.g., 'OneJS.Tests')."
                },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_test_run_sync",
        bridgeName: "unity.test.runSync",
        description: "Start EditMode tests (NOT truly synchronous - Unity callbacks require polling). Returns immediately with runId. Use unity_test_get_results to poll for completion. Only works for EditMode tests.",
        inputSchema: {
            type: "object",
            properties: {
                testMode: {
                    type: "string",
                    enum: ["editmode"],
                    default: "editmode",
                    description: "Only 'editmode' is supported."
                },
                testFilter: {
                    type: "string",
                    description: "Comma-separated list of test names to run. Omit to run all tests."
                },
                categoryFilter: {
                    type: "string",
                    description: "Comma-separated list of test categories to include."
                },
                assemblyFilter: {
                    type: "string",
                    description: "Comma-separated list of assembly names to include."
                },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_test_get_results",
        bridgeName: "unity.test.getResults",
        description: "Get results from a test run. Returns status (running/completed), summary counts, and detailed results. Poll this after unity_test_run until status='completed'.",
        inputSchema: {
            type: "object",
            properties: {
                runId: {
                    type: "string",
                    description: "The runId returned from unity_test_run. Omit to get results from the most recent run."
                },
            },
            additionalProperties: false,
        },
    },

    // MARK: Prefab
    {
        safeName: "unity_prefab_load",
        bridgeName: "unity.prefab.load",
        description: "Load a prefab asset for inspection/editing. Returns the prefab's root GameObject info and component list. Use unity_component_get_properties/set_property with the returned instanceId to edit.",
        inputSchema: {
            type: "object",
            properties: {
                path: {
                    type: "string",
                    description: "Asset path to the prefab (e.g., 'Assets/Prefabs/Player.prefab')"
                },
            },
            required: ["path"],
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_prefab_save",
        bridgeName: "unity.prefab.save",
        description: "Save changes to a prefab asset. Call after modifying prefab components via unity_component_set_property.",
        inputSchema: {
            type: "object",
            properties: {
                path: {
                    type: "string",
                    description: "Asset path to the prefab. Can be omitted if instanceId is provided."
                },
                instanceId: {
                    type: "integer",
                    description: "Instance ID of the prefab root GameObject (from unity_prefab_load)."
                },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_prefab_get_hierarchy",
        bridgeName: "unity.prefab.getHierarchy",
        description: "Get the full hierarchy of a prefab, including all children and their components.",
        inputSchema: {
            type: "object",
            properties: {
                path: {
                    type: "string",
                    description: "Asset path to the prefab."
                },
                instanceId: {
                    type: "integer",
                    description: "Instance ID of the prefab root GameObject."
                },
                maxDepth: {
                    type: "integer",
                    default: 10,
                    description: "Maximum depth to traverse (default: 10)."
                },
            },
            additionalProperties: false,
        },
    },
    {
        safeName: "unity_prefab_find_component",
        bridgeName: "unity.prefab.findComponent",
        description: "Find a component within a prefab by child path and type. Returns the component's properties.",
        inputSchema: {
            type: "object",
            properties: {
                prefabPath: {
                    type: "string",
                    description: "Asset path to the prefab."
                },
                childPath: {
                    type: "string",
                    description: "Path to child within prefab (e.g., 'Armature/Hips/Spine'). Omit for root."
                },
                type: {
                    type: "string",
                    description: "Component type name (e.g., 'MeshRenderer', 'Animator')."
                },
            },
            required: ["prefabPath", "type"],
            additionalProperties: false,
        },
    },
]

const tools = _defs.map((d) => ({
    name: d.safeName,
    description: d.description,
    inputSchema: d.inputSchema,
}))

const toolNameToBridgeName = (() => {
    const map = Object.create(null)
    for (const d of _defs) {
        map[d.safeName] = d.bridgeName
        map[d.bridgeName] = d.bridgeName
    }
    return map
})()

module.exports = { tools, toolNameToBridgeName }