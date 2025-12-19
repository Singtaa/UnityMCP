using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UnityMcp {
    static class Tools_Undo {
        public static ToolResult PerformUndo(JObject args) {
            Undo.PerformUndo();
            return ToolResultUtil.Text("Undo performed.");
        }

        public static ToolResult PerformRedo(JObject args) {
            Undo.PerformRedo();
            return ToolResultUtil.Text("Redo performed.");
        }

        public static ToolResult GetCurrentGroup(JObject args) {
            var name = Undo.GetCurrentGroupName();
            var id = Undo.GetCurrentGroup();

            return ToolResultUtil.Text(JsonConvert.SerializeObject(new {
                groupId = id,
                groupName = name
            }));
        }

        public static ToolResult CollapseUndoOperations(JObject args) {
            var groupId = args.Value<int?>("groupId") ?? Undo.GetCurrentGroup();
            Undo.CollapseUndoOperations(groupId);
            return ToolResultUtil.Text($"Collapsed undo operations to group {groupId}.");
        }

        public static ToolResult SetCurrentGroupName(JObject args) {
            var name = args.Value<string>("name") ?? "[MCP]";
            Undo.SetCurrentGroupName(name);
            return ToolResultUtil.Text($"Undo group name set to: {name}");
        }

        public static ToolResult ClearAll(JObject args) {
            Undo.ClearAll();
            return ToolResultUtil.Text("Undo history cleared.");
        }
    }
}
