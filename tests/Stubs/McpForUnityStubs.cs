// Minimal surface of MCP for Unity (CoplayDev/unity-mcp) used by the toolkit, for compile checks only.
// Mirrors MCPForUnity.Editor.Tools.McpForUnityToolAttribute / ToolParameterAttribute and
// MCPForUnity.Editor.Helpers.SuccessResponse / ErrorResponse.
using System;

namespace MCPForUnity.Editor.Tools
{
    [AttributeUsage(AttributeTargets.Class)]
    public class McpForUnityToolAttribute : Attribute
    {
        public McpForUnityToolAttribute(string name = null) { Name = name; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool StructuredOutput { get; set; } = true;
        public bool AutoRegister { get; set; } = true;
        public string Group { get; set; } = "core";
        public bool RequiresPolling { get; set; }
        public string PollAction { get; set; } = "status";
        public int MaxPollSeconds { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class ToolParameterAttribute : Attribute
    {
        public ToolParameterAttribute(string description) { Description = description; }
        public string Description { get; set; }
        public bool Required { get; set; } = true;
        public string DefaultValue { get; set; }
    }
}

namespace MCPForUnity.Editor.Helpers
{
    public sealed class SuccessResponse
    {
        public SuccessResponse(string message, object data = null) { Message = message; Data = data; }
        public string Message { get; }
        public object Data { get; }
    }

    public sealed class ErrorResponse
    {
        public ErrorResponse(string messageOrCode, object data = null) { Error = messageOrCode; Data = data; }
        public string Error { get; }
        public object Data { get; }
    }
}
