namespace UnityMcp {
    /// <summary>
    /// Content block for MCP tool results.
    /// </summary>
    public sealed class ContentBlock {
        public string type;
        public string text;
    }

    /// <summary>
    /// Result of a tool call.
    /// </summary>
    public sealed class ToolResult {
        public ContentBlock[] content;
        public bool isError;
    }

    /// <summary>
    /// Content for MCP resources.
    /// </summary>
    public sealed class ResourceContent {
        public string uri;
        public string mimeType;
        public string text;
        public string blob; // base64 for binary
    }

    /// <summary>
    /// Result of a resource read.
    /// </summary>
    public sealed class ResourceResult {
        public ResourceContent[] contents;
        public bool isError;
    }

    /// <summary>
    /// Helper methods for creating tool results.
    /// </summary>
    public static class ToolResultUtil {
        public static ToolResult Text(string text, bool isError = false) {
            return new ToolResult {
                content = new[] { new ContentBlock { type = "text", text = text ?? "" } },
                isError = isError
            };
        }

        public static ToolResult Error(string message) {
            return Text(message, isError: true);
        }
    }

    /// <summary>
    /// Helper methods for creating resource results.
    /// </summary>
    public static class ResourceResultUtil {
        public static ResourceResult Text(string uri, string text, string mimeType = "text/plain") {
            return new ResourceResult {
                contents = new[] {
                    new ResourceContent {
                        uri = uri,
                        mimeType = mimeType,
                        text = text ?? ""
                    }
                },
                isError = false
            };
        }

        public static ResourceResult Json(string uri, string json) {
            return Text(uri, json, "application/json");
        }

        public static ResourceResult Error(string uri, string message) {
            return new ResourceResult {
                contents = new[] {
                    new ResourceContent {
                        uri = uri,
                        mimeType = "text/plain",
                        text = message
                    }
                },
                isError = true
            };
        }
    }
}
