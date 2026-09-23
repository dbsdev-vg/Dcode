namespace DCode.Server.Chats;

public sealed record CreateChatRequest(string BrowserProfileId);
public sealed record UpdateChatRequest(string Title);
public sealed record SendChatMessageRequest(string Content);
public sealed record SendProjectChatMessageRequest(string ChatId, string Content);
public sealed record ChatMessage(string Id, string ChatId, string Role, string Content, string? ProviderMessageId, DateTimeOffset CreatedUtc);
public sealed record ChatExchangeResponse(ChatConversation Conversation, IReadOnlyList<ChatMessage> Messages);
public sealed record ProjectChatRunResponse(
    string Status,
    string? Error,
    ChatExchangeResponse? Exchange,
    IReadOnlyList<AgentLoop.ToolActivity> Activities
);
public sealed record ProjectChatStreamEvent(
    string Type,
    AgentLoop.ConversationProgress? Progress = null,
    ChatExchangeResponse? Exchange = null,
    ProjectChatRunResponse? Result = null
);
public sealed record ProjectChatExecutionState(
    string Status,
    string? Error,
    IReadOnlyList<AgentLoop.ConversationProgress> Progress,
    DateTimeOffset UpdatedUtc
);
public sealed record ProjectChatExecutionTurn(
    string Id,
    string ChatId,
    string UserMessageId,
    string Status,
    string? Error,
    IReadOnlyList<AgentLoop.ConversationProgress> Events,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc
);

public sealed record ChatConversation(
    string Id,
    string Title,
    string ProviderType,
    string Transport,
    string BrowserProfileId,
    string AccountName,
    string? ProviderConversationId,
    string? ProviderConversationUrl,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc
);

public sealed record ProjectConversationCollection(string? ActiveConversationId, IReadOnlyList<ChatConversation> Conversations);
