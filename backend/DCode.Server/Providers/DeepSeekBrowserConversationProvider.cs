using DCode.Server.Chats.AgentLoop;

namespace DCode.Server.Providers;

public sealed class DeepSeekBrowserConversationProvider(
    BrowserProfileRepository profiles,
    DeepSeekBrowserChatAdapter adapter) : IConversationProvider
{
    public string ProviderId => "deepseek";
    public string Transport => "browser";

    public async Task<ProviderConversationTurn> SendAsync(
        ProviderConversationSession session,
        string content,
        CancellationToken cancellationToken)
    {
        var profile = profiles.Get(session.AccountId);
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,timeout.Token);
        try
        {
            var result = await adapter.SendAsync(profile, session.ConversationReference, content, linked.Token);
            return new ProviderConversationTurn(result.Content, result.ConversationUrl);
        }
        catch(OperationCanceledException)when(timeout.IsCancellationRequested&&!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("DeepSeek browser provider exceeded the four-minute turn limit.");
        }
    }

    public Task RenameAsync(ProviderConversationSession session, string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.ConversationReference))
            throw new RemoteConversationUnavailableException("The DeepSeek conversation has not been created yet.");
        return adapter.RenameAsync(profiles.Get(session.AccountId), session.ConversationReference, title, cancellationToken);
    }

    public Task OpenAsync(ProviderConversationSession session, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.ConversationReference))
            throw new RemoteConversationUnavailableException("The DeepSeek conversation has not been created yet.");
        return adapter.OpenAsync(profiles.Get(session.AccountId), session.ConversationReference, cancellationToken);
    }
}
