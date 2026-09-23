namespace DCode.Server.Agents;

public sealed record AgentSession(
    string Id, string ProjectId, string AgentId, string AgentName, string AgentRole,
    string ConversationId, string ProviderType, string Transport, string ProviderAccountId,
    string? RemoteConversationReference, string Status, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

public sealed record DelegateAgentRequest(string AgentRole, string Objective);
public sealed record DelegationResponse(AgentRun Run, AgentSession Session, string? Result);
