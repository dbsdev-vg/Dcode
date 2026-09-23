using DCode.Server.Tools.Permissions;

namespace DCode.Server.Tools;

public interface ITool
{
    ToolDefinition Definition { get; }
    IReadOnlySet<ToolPermission> RequiredPermissions { get; }
    Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context);
}
