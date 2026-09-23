namespace DCode.Server.Chats.AgentLoop;

public sealed record ProviderConversationSession(
    string ProviderId,
    string Transport,
    string AccountId,
    string? ConversationReference
);

public sealed record ProviderConversationTurn(string Content, string ConversationReference);

public sealed class RemoteConversationUnavailableException(string message) : Exception(message);

public interface IConversationProvider
{
    string ProviderId { get; }
    string Transport { get; }
    Task<ProviderConversationTurn> SendAsync(
        ProviderConversationSession session,
        string content,
        CancellationToken cancellationToken
    );
    Task RenameAsync(
        ProviderConversationSession session,
        string title,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException($"{ProviderId} does not support remote conversation rename.");
    Task OpenAsync(
        ProviderConversationSession session,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException($"{ProviderId} does not support opening remote conversations.");
}
