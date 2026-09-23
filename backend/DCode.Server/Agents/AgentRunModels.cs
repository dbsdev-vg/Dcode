using DCode.Server.Chats.AgentLoop;

namespace DCode.Server.Agents;

public sealed record AgentRun(
    string Id, string ProjectId, string AgentId, string AgentName, string AgentRole,
    string? ConversationId, string? TaskId, string? ParentRunId, string Trigger,
    string Objective, string Status, string? ProviderType, string? Transport,
    string? ProviderAccountId, string? Model, string? UserMessageId, string? Error,
    IReadOnlyList<ConversationProgress> Events, DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc, DateTimeOffset? CompletedUtc, DateTimeOffset UpdatedUtc);

public sealed record StartAgentRun(
    string Id, string ProjectId, string AgentId, string? ConversationId,
    string Trigger, string Objective, string? ProviderType, string? Transport,
    string? ProviderAccountId, string? Model, string? UserMessageId,
    string? TaskId = null, string? ParentRunId = null);
