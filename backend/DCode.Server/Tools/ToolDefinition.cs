using System.Text.Json;
using DCode.Server.Tools.Permissions;

namespace DCode.Server.Tools;

public sealed record ToolDefinition(
    string Id,
    string Description,
    JsonElement ArgumentSchema,
    IReadOnlyList<string> RequiredPermissions
)
{
    public static ToolDefinition Create(
        string id,
        string description,
        object argumentSchema,
        params ToolPermission[] requiredPermissions
    ) => new(
        id,
        description,
        JsonSerializer.SerializeToElement(argumentSchema),
        requiredPermissions.Select(permission => permission.Id).ToArray()
    );
}
