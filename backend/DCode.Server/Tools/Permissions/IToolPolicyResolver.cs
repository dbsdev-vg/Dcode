namespace DCode.Server.Tools.Permissions;

public interface IToolPolicyResolver
{
    ToolPolicyResolution Resolve(string toolId, string? projectId = null, string? agentId = null);
}
