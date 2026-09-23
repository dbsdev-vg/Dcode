namespace DCode.Server.Tools.Permissions;

public enum ToolPermissionPolicy
{
    Allow,
    Ask,
    Deny
}

public static class ToolPermissionPolicyValues
{
    public static string Serialize(ToolPermissionPolicy policy) => policy.ToString().ToLowerInvariant();

    public static bool TryParse(string? value, out ToolPermissionPolicy policy) =>
        Enum.TryParse(value, ignoreCase: true, out policy) && Enum.IsDefined(policy);
}

public sealed record ToolPolicyResolution(
    string ToolId,
    ToolPermissionPolicy Policy,
    string Scope
);
