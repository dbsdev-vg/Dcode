namespace DCode.Server.Tools.Permissions;

public sealed record UpdateToolPolicyRequest(string Policy);

public sealed record ToolPolicyResponse(string ToolId, string Policy, string Scope);
