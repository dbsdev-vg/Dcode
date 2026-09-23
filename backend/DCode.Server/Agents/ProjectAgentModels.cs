using System.Text.Json;

namespace DCode.Server.Agents;

public sealed record ProjectAgent(
    string Id, string ProjectId, string Name, string Role, string Instructions,
    string? BrowserProfileId, string? ProviderType, string? AccountName, string? Model,
    JsonElement Permissions, bool Enabled, bool IsLead, string CreatedUtc, string UpdatedUtc);

public sealed record UpdateProjectAgentRequest(
    string Name, string Instructions, string? BrowserProfileId, string? Model, bool Enabled);

public sealed record CreateProjectAgentRequest(
    string Name, string Role, string Instructions, string? BrowserProfileId, string? Model, bool Enabled = true);

public sealed record ProviderAccountSummary(
    string Id, string ProviderType, string Transport, string Name, bool Connected);
