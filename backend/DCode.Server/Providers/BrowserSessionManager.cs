using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace DCode.Server.Providers;

public sealed class BrowserSessionManager(BrowserProfileRepository profiles) : IHostedService
{
    private readonly ConcurrentDictionary<string, IBrowserContext> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _errors = new();
    private readonly SemaphoreSlim _playwrightLock = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _profileLocks = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _warmLeases = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _idleClosures = new();
    private readonly ConcurrentDictionary<string, byte> _warmOwnedSessions = new();
    private IPlaywright? _playwright;
    private static readonly TimeSpan WarmIdleTimeout = TimeSpan.FromMinutes(3);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var sessions = _sessions.ToArray();
        _sessions.Clear();
        foreach (var closure in _idleClosures.Values) closure.Cancel();
        _idleClosures.Clear();
        _warmLeases.Clear();
        _warmOwnedSessions.Clear();
        await Task.WhenAll(sessions.Select(pair => CloseQuietlyAsync(pair.Value)));
        _playwright?.Dispose();
        _playwright = null;
    }

    public BrowserProfileResponse ToResponse(BrowserProfile profile)
    {
        var running = _sessions.TryGetValue(profile.Id, out var context)
            && context.Browser?.IsConnected == true;
        if (!running && context is not null) _sessions.TryRemove(profile.Id, out _);
        _errors.TryGetValue(profile.Id, out var error);
        return new(profile.Id, profile.ProviderType, profile.Name, profile.BrowserChannel,
            profile.Headless, profile.Connected, running ? "running" : "stopped", error);
    }

    public async Task<BrowserProfileResponse> StartProfileAsync(BrowserProfile profile)
    {
        var profileLock = _profileLocks.GetOrAdd(profile.Id, _ => new SemaphoreSlim(1, 1));
        if (!await profileLock.WaitAsync(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("This browser profile is busy with another DCode operation. Stop the active Agent run or wait for it to finish, then retry.");
        try { return await StartProfileCoreAsync(profile); }
        finally { profileLock.Release(); }
    }

    private async Task<BrowserProfileResponse> StartProfileCoreAsync(BrowserProfile profile)
    {
        if (_sessions.TryGetValue(profile.Id, out var existing)
            && existing.Browser?.IsConnected == true
            && !_warmOwnedSessions.ContainsKey(profile.Id)) return ToResponse(profile);
        if (existing?.Browser?.IsConnected == true && _warmOwnedSessions.TryRemove(profile.Id, out _))
        {
            _sessions.TryRemove(profile.Id, out _);
            await CloseQuietlyAsync(existing);
            existing = null;
        }
        if (existing is not null) _sessions.TryRemove(profile.Id, out _);

        IBrowserContext? launchedContext = null;
        try
        {
            Directory.CreateDirectory(profile.UserDataDirectory);
            var playwright = await GetPlaywrightAsync();
            launchedContext = await playwright.Chromium.LaunchPersistentContextAsync(
                profile.UserDataDirectory,
                new() { Channel = profile.BrowserChannel, Headless = profile.Headless, Timeout = 30_000 });
            var storageStatePath = GetStorageStatePath(profile);
            if (profile.Connected && File.Exists(storageStatePath))
                await launchedContext.SetStorageStateAsync(storageStatePath);
            if (!_sessions.TryAdd(profile.Id, launchedContext))
            {
                await launchedContext.CloseAsync();
                return ToResponse(profile);
            }
            _errors.TryRemove(profile.Id, out _);
            var page = launchedContext.Pages.FirstOrDefault() ?? await launchedContext.NewPageAsync();
            await page.GotoAsync("https://chat.deepseek.com/");
            if (!profile.Connected) _ = MonitorAuthenticationAsync(profile, launchedContext);
            return ToResponse(profile);
        }
        catch (Exception exception)
        {
            _sessions.TryRemove(profile.Id, out _);
            if (launchedContext is not null) await CloseQuietlyAsync(launchedContext);
            var error = DescribeBrowserFailure(exception);
            _errors[profile.Id] = error;
            throw new InvalidOperationException(
                $"Unable to start DeepSeek profile '{profile.Name}'. {error}", exception);
        }
    }

    public async Task<BrowserProfileResponse> StopProfileAsync(BrowserProfile profile)
    {
        if (_idleClosures.TryRemove(profile.Id, out var idleClosure)) idleClosure.Cancel();
        _warmLeases.TryRemove(profile.Id, out _);
        _warmOwnedSessions.TryRemove(profile.Id, out _);
        if (_sessions.TryRemove(profile.Id, out var context))
        {
            if (!profile.Connected) await TryCaptureConnectionAsync(profile, context);
            await CloseQuietlyAsync(context);
        }
        _errors.TryRemove(profile.Id, out _);
        return ToResponse(profile);
    }

    public async Task DeleteProfileAsync(BrowserProfile profile)
    {
        if (_idleClosures.TryRemove(profile.Id, out var idleClosure)) idleClosure.Cancel();
        _warmLeases.TryRemove(profile.Id, out _);
        _warmOwnedSessions.TryRemove(profile.Id, out _);
        if (_sessions.TryRemove(profile.Id, out var context)) await CloseQuietlyAsync(context);
        _errors.TryRemove(profile.Id, out _);
        profiles.Delete(profile);
    }

    public async Task<BrowserConnectionTestResponse> TestConnectionAsync(BrowserProfile profile)
    {
        var profileLock = _profileLocks.GetOrAdd(profile.Id, _ => new SemaphoreSlim(1, 1));
        if (!await profileLock.WaitAsync(TimeSpan.FromSeconds(15)))
            return new(false, "busy", "This browser profile is busy with another DCode operation. Stop the active Agent run or wait for it to finish, then retry.");
        try { return await TestConnectionCoreAsync(profile); }
        finally { profileLock.Release(); }
    }

    private async Task<BrowserConnectionTestResponse> TestConnectionCoreAsync(BrowserProfile profile)
    {
        if (!profile.Connected || !File.Exists(GetStorageStatePath(profile)))
            return new(false, "disconnected", "Sign in to this DeepSeek account before testing it.");

        if (_sessions.TryGetValue(profile.Id, out var runningContext)
            && runningContext.Browser?.IsConnected == true)
        {
            var runningPage = runningContext.Pages.FirstOrDefault() ?? await runningContext.NewPageAsync();
            await runningPage.GotoAsync("https://chat.deepseek.com/");
            BrowserConnectionTestResponse result = await IsAuthenticatedAsync(runningContext, 10_000)
                ? new(true, "connected", "DeepSeek is authenticated and ready for chat.")
                : new(false, "authentication-required", "DeepSeek did not restore the authenticated chat screen. Reconnect this account.");
            if (result.Success) _errors.TryRemove(profile.Id, out _);
            return result;
        }

        IBrowserContext? testContext = null;
        try
        {
            testContext = await LaunchAuthenticatedContextAsync(profile, headless: true);
            var page = testContext.Pages.FirstOrDefault() ?? await testContext.NewPageAsync();
            await page.GotoAsync("https://chat.deepseek.com/");

            BrowserConnectionTestResponse result = await IsAuthenticatedAsync(testContext, 10_000)
                ? new(true, "connected", "DeepSeek is authenticated and ready for chat.")
                : new(false, "authentication-required", "DeepSeek did not restore the authenticated chat screen. Reconnect this account.");
            if (result.Success) _errors.TryRemove(profile.Id, out _);
            return result;
        }
        catch (PlaywrightException exception)
        {
            var error = DescribeBrowserFailure(exception);
            _errors[profile.Id] = error;
            return new(false, "unavailable", $"DeepSeek could not be tested. {error}");
        }
        finally
        {
            if (testContext is not null) await CloseQuietlyAsync(testContext);
        }
    }

    public async Task<T> UseAuthenticatedProfileAsync<T>(BrowserProfile profile, Func<IBrowserContext, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (!profile.Connected || !File.Exists(GetStorageStatePath(profile)))
            throw new InvalidOperationException("The browser account is not connected.");

        var profileLock = _profileLocks.GetOrAdd(profile.Id, _ => new SemaphoreSlim(1, 1));
        await profileLock.WaitAsync(cancellationToken);
        IBrowserContext? temporaryContext = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sessions.TryGetValue(profile.Id, out var runningContext)
                && runningContext.Browser?.IsConnected == true)
                return await operation(runningContext);

            temporaryContext = await LaunchAuthenticatedContextAsync(profile, profile.Headless);
            return await operation(temporaryContext);
        }
        finally
        {
            if (temporaryContext is not null) await CloseQuietlyAsync(temporaryContext);
            profileLock.Release();
        }
    }

    public async Task OpenAuthenticatedPageAsync(BrowserProfile profile, string url, CancellationToken cancellationToken = default)
    {
        if (!profile.Connected || !File.Exists(GetStorageStatePath(profile)))
            throw new InvalidOperationException("The browser account is not connected.");
        var profileLock = _profileLocks.GetOrAdd(profile.Id, _ => new SemaphoreSlim(1, 1));
        await profileLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(profile.Id, out var existing) && existing.Browser?.IsConnected == true && !_warmOwnedSessions.ContainsKey(profile.Id))
            {
                var existingPage = existing.Pages.FirstOrDefault() ?? await existing.NewPageAsync();
                await existingPage.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
                await existingPage.BringToFrontAsync();
                return;
            }
            if (existing is not null)
            {
                _sessions.TryRemove(profile.Id, out _); _warmOwnedSessions.TryRemove(profile.Id, out _);
                await CloseQuietlyAsync(existing);
            }
            var context = await LaunchAuthenticatedContextAsync(profile, headless: false);
            _sessions[profile.Id] = context;
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            await page.BringToFrontAsync();
        }
        finally { profileLock.Release(); }
    }

    public async Task<BrowserWarmLeaseResponse> AcquireWarmLeaseAsync(BrowserProfile profile, string leaseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leaseId)) throw new ArgumentException("A warm-session lease ID is required.");
        if (!profile.Connected || !File.Exists(GetStorageStatePath(profile)))
            throw new InvalidOperationException("The browser account is not connected.");

        var leases = _warmLeases.GetOrAdd(profile.Id, _ => new());
        leases[leaseId] = 0;
        if (_idleClosures.TryRemove(profile.Id, out var idleClosure)) idleClosure.Cancel();

        using var warmTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, warmTimeout.Token);
        var profileLock = _profileLocks.GetOrAdd(profile.Id, _ => new SemaphoreSlim(1, 1));
        try
        {
            await profileLock.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (warmTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            leases.TryRemove(leaseId, out _);
            throw new TimeoutException("The DeepSeek session did not become ready within 75 seconds.");
        }
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!_sessions.TryGetValue(profile.Id, out var context) || context.Browser?.IsConnected != true)
            {
                if (context is not null) _sessions.TryRemove(profile.Id, out _);
                var launched = await LaunchAuthenticatedContextAsync(profile, profile.Headless);
                try
                {
                    var page = launched.Pages.FirstOrDefault() ?? await launched.NewPageAsync();
                    await page.GotoAsync("https://chat.deepseek.com/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
                    if (!await IsAuthenticatedAsync(launched, 10_000))
                        throw new InvalidOperationException("DeepSeek authentication could not be restored. Reconnect this account.");
                    _sessions[profile.Id] = launched;
                    _warmOwnedSessions[profile.Id] = 0;
                    _errors.TryRemove(profile.Id, out _);
                }
                catch
                {
                    await CloseQuietlyAsync(launched);
                    throw;
                }
            }
            return new(profile.Id, leaseId, "ready", (int)WarmIdleTimeout.TotalSeconds);
        }
        catch (OperationCanceledException) when (warmTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            leases.TryRemove(leaseId, out _);
            throw new TimeoutException("The DeepSeek session did not become ready within 75 seconds.");
        }
        catch (OperationCanceledException)
        {
            leases.TryRemove(leaseId, out _);
            throw;
        }
        catch (Exception exception)
        {
            leases.TryRemove(leaseId, out _);
            var error = DescribeBrowserFailure(exception);
            _errors[profile.Id] = error;
            throw new InvalidOperationException(error, exception);
        }
        finally { profileLock.Release(); }
    }

    public BrowserWarmLeaseResponse ReleaseWarmLease(BrowserProfile profile, string leaseId)
    {
        if (_warmLeases.TryGetValue(profile.Id, out var leases)) leases.TryRemove(leaseId, out _);
        if (leases is not null && !leases.IsEmpty)
            return new(profile.Id, leaseId, "ready", (int)WarmIdleTimeout.TotalSeconds);

        var closure = new CancellationTokenSource();
        if (_idleClosures.TryGetValue(profile.Id, out var previous)) previous.Cancel();
        _idleClosures[profile.Id] = closure;
        _ = CloseWarmSessionAfterIdleAsync(profile.Id, closure);
        return new(profile.Id, leaseId, "idle", (int)WarmIdleTimeout.TotalSeconds);
    }

    private async Task CloseWarmSessionAfterIdleAsync(string profileId, CancellationTokenSource closure)
    {
        try
        {
            await Task.Delay(WarmIdleTimeout, closure.Token);
            if (_warmLeases.TryGetValue(profileId, out var leases) && !leases.IsEmpty) return;
            if (!_warmOwnedSessions.TryRemove(profileId, out _)) return;
            if (_sessions.TryRemove(profileId, out var context)) await CloseQuietlyAsync(context);
            _warmLeases.TryRemove(profileId, out _);
            _idleClosures.TryRemove(profileId, out _);
        }
        catch (OperationCanceledException) { }
        finally { closure.Dispose(); }
    }

    private async Task MonitorAuthenticationAsync(BrowserProfile profile, IBrowserContext context)
    {
        try
        {
            while (context.Browser?.IsConnected == true)
            {
                if (await TryCaptureConnectionAsync(profile, context))
                {
                    _sessions.TryRemove(profile.Id, out _);
                    await CloseQuietlyAsync(context);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
        catch (PlaywrightException) { }
    }

    private async Task<bool> TryCaptureConnectionAsync(BrowserProfile profile, IBrowserContext context)
    {
        try
        {
            if (!await IsAuthenticatedAsync(context)) return false;
            await context.StorageStateAsync(new()
            {
                Path = GetStorageStatePath(profile),
                IndexedDB = true
            });
            profiles.MarkConnected(profile.Id);
            return true;
        }
        catch (PlaywrightException) { }
        return false;
    }

    private static async Task<bool> IsAuthenticatedAsync(IBrowserContext context, int timeoutMilliseconds = 0)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        do
        {
            foreach (var page in context.Pages)
            {
                if (!page.Url.StartsWith("https://chat.deepseek.com", StringComparison.OrdinalIgnoreCase)) continue;
                if (await page.Locator("textarea, [contenteditable='true']").CountAsync() > 0) return true;
            }
            if (DateTime.UtcNow < deadline) await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);
        return false;
    }

    public async Task<BrowserProfileResponse> ConfirmConnectionAsync(BrowserProfile profile)
    {
        if (!_sessions.TryGetValue(profile.Id, out var context)
            || context.Browser?.IsConnected != true)
            throw new InvalidOperationException("Launch the browser and sign in before confirming this account.");

        await context.StorageStateAsync(new()
        {
            Path = GetStorageStatePath(profile),
            IndexedDB = true
        });
        profiles.MarkConnected(profile.Id);
        return ToResponse(profiles.Get(profile.Id));
    }

    private static string GetStorageStatePath(BrowserProfile profile) =>
        Path.Combine(profile.UserDataDirectory, "dcode-auth-state.json");

    private async Task<IBrowserContext> LaunchAuthenticatedContextAsync(BrowserProfile profile, bool headless)
    {
        var playwright = await GetPlaywrightAsync();
        var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = profile.BrowserChannel,
            Headless = headless,
            Timeout = 30_000
        });
        try
        {
            return await browser.NewContextAsync(new()
            {
                StorageStatePath = GetStorageStatePath(profile)
            });
        }
        catch
        {
            await CloseBrowserQuietlyAsync(browser);
            throw;
        }
    }

    private async Task<IPlaywright> GetPlaywrightAsync()
    {
        if (_playwright is not null) return _playwright;
        await _playwrightLock.WaitAsync();
        try { return _playwright ??= await Playwright.CreateAsync(); }
        finally { _playwrightLock.Release(); }
    }

    private static async Task CloseQuietlyAsync(IBrowserContext context)
    {
        var browser = context.Browser;
        try { await context.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException) { }
        if (browser is not null) await CloseBrowserQuietlyAsync(browser);
    }

    private static async Task CloseBrowserQuietlyAsync(IBrowser browser)
    {
        try { await browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException) { }
    }

    private static string DescribeBrowserFailure(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("exitCode=21", StringComparison.OrdinalIgnoreCase))
            return "Edge closed during startup. This browser profile may already be in use by another DCode or Edge process. Close that session and retry.";
        var firstLine = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLine) ? "Edge could not start for this browser profile." : firstLine;
    }
}
