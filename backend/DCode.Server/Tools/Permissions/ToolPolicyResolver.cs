namespace DCode.Server.Tools.Permissions;

public sealed class ToolPolicyResolver(ToolPolicyRepository policies) : IToolPolicyResolver
{
    public ToolPolicyResolution Resolve(string toolId, string? projectId = null, string? agentId = null)
    {
        // Future resolution order: agent override -> project override -> global default.
        // The parameters are intentionally part of this contract before those stores exist.
        _ = projectId;
        _ = agentId;
        var policy = policies.GetGlobal(toolId) ?? ToolPermissionPolicy.Ask;
        return new ToolPolicyResolution(toolId, policy, "global");
    }
}
