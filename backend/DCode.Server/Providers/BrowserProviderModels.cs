namespace DCode.Server.Providers;

public sealed record CreateBrowserProfileRequest(string Name, bool Headless = false, string BrowserChannel = "msedge");
public sealed record UpdateBrowserProfileRequest(bool Headless);
public sealed record BrowserProfile(string Id, string ProviderType, string Name, string UserDataDirectory, string BrowserChannel, bool Headless, bool Connected);
public sealed record BrowserProfileResponse(string Id, string ProviderType, string Name, string BrowserChannel, bool Headless, bool Connected, string Status, string? Error);
public sealed record BrowserConnectionTestResponse(bool Success, string Status, string Message);
public sealed record BrowserWarmLeaseRequest(string LeaseId);
public sealed record BrowserWarmLeaseResponse(string ProfileId, string LeaseId, string Status, int IdleTimeoutSeconds);
