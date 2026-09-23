using System.Text.Json;

namespace DCode.Server.Tools;

public sealed record ExecuteToolRequest(
    string ToolId,
    string ProjectId,
    JsonElement Arguments,
    IReadOnlyList<string>? GrantedPermissions
);
