namespace DCode.Server.Chats.AgentLoop;

public sealed class ConversationProviderRegistry(IEnumerable<IConversationProvider> providers)
{
    private readonly IReadOnlyDictionary<string, IConversationProvider> _providers = providers.ToDictionary(
        provider => Key(provider.ProviderId, provider.Transport),
        StringComparer.OrdinalIgnoreCase
    );

    public IConversationProvider Get(string providerId, string transport) =>
        _providers.TryGetValue(Key(providerId, transport), out var provider)
            ? provider
            : throw new KeyNotFoundException($"No conversation provider is registered for '{providerId}/{transport}'.");

    private static string Key(string providerId, string transport) => $"{providerId}:{transport}";
}
