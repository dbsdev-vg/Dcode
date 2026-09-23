using DCode.Server.Chats.AgentLoop;
using Microsoft.Playwright;

namespace DCode.Server.Providers;

public sealed record DeepSeekChatResult(string Content, string ConversationUrl);

public sealed class DeepSeekBrowserChatAdapter(
    BrowserSessionManager sessions,
    ILogger<DeepSeekBrowserChatAdapter> logger)
{
    private const string HomeUrl = "https://chat.deepseek.com/";
    private const int ChatReadyTimeoutMilliseconds = 30_000;
    private static readonly string[] ComposerSelectors =
    [
        "main form textarea",
        "form textarea[placeholder]",
        "textarea[placeholder]",
        "textarea",
        "main [role='textbox'][contenteditable='true']",
        "[role='textbox'][contenteditable='true']",
        "main [contenteditable='true']",
        "[contenteditable='true']"
    ];

    public Task<DeepSeekChatResult> SendAsync(BrowserProfile profile, string? conversationUrl, string content, CancellationToken cancellationToken = default) =>
        sessions.UseAuthenticatedProfileAsync(profile, context => SendInContextAsync(profile, context, conversationUrl, content, cancellationToken), cancellationToken);

    public async Task RenameAsync(BrowserProfile profile, string conversationUrl, string title, CancellationToken cancellationToken = default) =>
        _ = await sessions.UseAuthenticatedProfileAsync(profile, async context =>
        {
            await RenameInContextAsync(context, conversationUrl, title, cancellationToken);
            return true;
        }, cancellationToken);

    public Task OpenAsync(BrowserProfile profile, string conversationUrl, CancellationToken cancellationToken = default) =>
        sessions.OpenAuthenticatedPageAsync(profile, conversationUrl, cancellationToken);

    private static async Task RenameInContextAsync(IBrowserContext context, string conversationUrl, string title, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(conversationUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "chat.deepseek.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stored DeepSeek conversation URL is invalid.");
        var page = context.Pages.FirstOrDefault(candidate => SameConversation(conversationUrl, candidate.Url))
            ?? context.Pages.FirstOrDefault()
            ?? await context.NewPageAsync();
        await page.GotoAsync(conversationUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ChatReadyTimeoutMilliseconds });
        if (!SameConversation(conversationUrl, page.Url))
            throw new RemoteConversationUnavailableException("The saved DeepSeek conversation no longer exists or could not be restored.");
        cancellationToken.ThrowIfCancellationRequested();

        var conversationId = uri.AbsolutePath.TrimEnd('/').Split('/').Last();
        var remoteTitle = await page.EvaluateAsync<string>("""
        async id => {
          const response = await fetch('/api/v0/chat_session/fetch_page?count=500');
          if (!response.ok) return '';
          const body = await response.json();
          const sessions = body?.data?.biz_data?.chat_sessions || [];
          return sessions.find(item => item.id === id)?.title || '';
        }
        """, conversationId);
        if (string.IsNullOrWhiteSpace(remoteTitle))
            throw new InvalidOperationException("DeepSeek did not return the saved conversation in its conversation list.");
        var conversationLabel = page.GetByText(remoteTitle, new() { Exact = true }).Last;
        if (await conversationLabel.CountAsync() == 0 || !await conversationLabel.IsVisibleAsync())
        {
            var expand = page.Locator("button[aria-label*='sidebar' i], button[title*='sidebar' i]").First;
            if (await expand.CountAsync() > 0 && await expand.IsVisibleAsync()) await expand.ClickAsync();
            await conversationLabel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        await conversationLabel.HoverAsync();
        var row = conversationLabel.Locator("xpath=ancestor::*[self::li or @role='listitem' or count(.//button)>0][1]");
        if (await row.CountAsync() == 0) row = conversationLabel.Locator("xpath=parent::*");
        var menuButton = row.Locator("button").Last;
        if (await menuButton.CountAsync() == 0)
            throw new InvalidOperationException("DeepSeek conversation actions are unavailable.");
        await menuButton.ClickAsync();
        var renameAction = page.GetByText("Rename", new() { Exact = true }).Last;
        await renameAction.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await renameAction.ClickAsync();
        var dialog = page.Locator("[role='dialog']").Last;
        var input = dialog.Locator("input").Last;
        await input.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await input.FillAsync(title);
        await input.PressAsync("Enter");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    private async Task<DeepSeekChatResult> SendInContextAsync(BrowserProfile profile, IBrowserContext context, string? conversationUrl, string content, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(conversationUrl)
            && (!Uri.TryCreate(conversationUrl, UriKind.Absolute, out var remoteUri)
                || !string.Equals(remoteUri.Host, "chat.deepseek.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The stored DeepSeek conversation URL is invalid.");

        var page = !string.IsNullOrWhiteSpace(conversationUrl)
            ? context.Pages.FirstOrDefault(candidate => SameConversation(conversationUrl, candidate.Url))
            : context.Pages.FirstOrDefault();
        page ??= await context.NewPageAsync();
        var targetUrl = string.IsNullOrWhiteSpace(conversationUrl) ? HomeUrl : conversationUrl;
        await page.GotoAsync(targetUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ChatReadyTimeoutMilliseconds });
        if (!string.IsNullOrWhiteSpace(conversationUrl)
            && !SameConversation(conversationUrl, page.Url))
            throw new RemoteConversationUnavailableException("The saved DeepSeek conversation no longer exists or could not be restored.");

        var composer = await WaitForChatReadyAsync(profile, page, conversationUrl, cancellationToken);

        // Readiness checks can span client-side navigation. Never send into a page
        // that drifted away from an already-bound conversation.
        if (!string.IsNullOrWhiteSpace(conversationUrl) && !SameConversation(conversationUrl, page.Url))
            throw new RemoteConversationUnavailableException("The saved DeepSeek conversation changed before the message could be sent.");

        var responses = page.Locator(".ds-markdown");
        if (await responses.CountAsync() == 0)
            responses = page.Locator("[class*='ds-markdown'], [class*='markdown']");
        var previousCount = await responses.CountAsync();
        var previousLastText = previousCount > 0
            ? await ExtractMarkdownAsync(responses.Last)
            : "";
        await composer.FillAsync(content);
        await composer.PressAsync("Enter");

        var responseText = await WaitForResponseAsync(responses, previousCount, previousLastText, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseText))
            throw new InvalidOperationException("DeepSeek returned an empty response.");

        var resolvedConversationUrl = await WaitForConversationUrlAsync(page, conversationUrl, cancellationToken);
        return new(responseText, resolvedConversationUrl);
    }

    internal async Task<ILocator> WaitForChatReadyAsync(
        BrowserProfile profile,
        IPage page,
        string? conversationUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = ChatReadyTimeoutMilliseconds });
            var deadline = DateTime.UtcNow.AddMilliseconds(ChatReadyTimeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsLoginUrl(page.Url) || await HasVisibleLoginFormAsync(page))
                    throw new InvalidOperationException("DeepSeek authentication has expired. Reconnect this browser account.");
                if (!string.IsNullOrWhiteSpace(conversationUrl) && !SameConversation(conversationUrl, page.Url))
                    throw new RemoteConversationUnavailableException("The saved DeepSeek conversation no longer exists or could not be restored.");

                var composer = await GetComposerAsync(page);
                if (composer is not null && !await HasBlockingUiAsync(page)) return composer;
                await Task.Delay(250, cancellationToken);
            }

            throw new TimeoutException("DeepSeek chat did not become ready within 30 seconds.");
        }
        catch (Exception exception) when (exception is TimeoutException or PlaywrightException)
        {
            var screenshotPath = await LogFailureDiagnosticsAsync(profile, page, conversationUrl, captureScreenshot: true);
            throw new TimeoutException(
                $"DeepSeek chat input was not ready within 30 seconds. Diagnostic screenshot: {screenshotPath}",
                exception);
        }
        catch (Exception exception) when (exception is InvalidOperationException or RemoteConversationUnavailableException)
        {
            await LogFailureDiagnosticsAsync(profile, page, conversationUrl, captureScreenshot: false);
            throw;
        }
    }

    internal static async Task<ILocator?> GetComposerAsync(IPage page)
    {
        foreach (var selector in ComposerSelectors)
        {
            var candidates = page.Locator(selector);
            var count = await candidates.CountAsync();
            for (var index = 0; index < count; index++)
            {
                var candidate = candidates.Nth(index);
                if (!await candidate.IsVisibleAsync() || !await candidate.IsEnabledAsync()) continue;
                if (await candidate.GetAttributeAsync("readonly") is not null) continue;
                if (string.Equals(await candidate.GetAttributeAsync("contenteditable"), "false", StringComparison.OrdinalIgnoreCase)) continue;
                return candidate;
            }
        }
        return null;
    }

    private static async Task<bool> HasBlockingUiAsync(IPage page)
    {
        var blockers = page.Locator("[aria-busy='true'], [aria-modal='true'], [role='dialog'], [class*='loading-mask']");
        var count = await blockers.CountAsync();
        for (var index = 0; index < count; index++)
            if (await blockers.Nth(index).IsVisibleAsync()) return true;
        return false;
    }

    private static bool IsLoginUrl(string url) =>
        url.Contains("login", StringComparison.OrdinalIgnoreCase)
        || url.Contains("sign_in", StringComparison.OrdinalIgnoreCase)
        || url.Contains("signin", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> HasVisibleLoginFormAsync(IPage page)
    {
        var loginInputs = page.Locator("input[type='password'], form input[name*='password' i]");
        var count = await loginInputs.CountAsync();
        for (var index = 0; index < count; index++)
            if (await loginInputs.Nth(index).IsVisibleAsync()) return true;
        return false;
    }

    private async Task<string> LogFailureDiagnosticsAsync(
        BrowserProfile profile,
        IPage page,
        string? conversationUrl,
        bool captureScreenshot)
    {
        var title = "<unavailable>";
        var contentEditableCount = -1;
        var loginDetected = IsLoginUrl(page.Url);
        try { title = await page.TitleAsync(); } catch (PlaywrightException) { }
        try { contentEditableCount = await page.Locator("[contenteditable='true']").CountAsync(); } catch (PlaywrightException) { }
        try { loginDetected |= await HasVisibleLoginFormAsync(page); } catch (PlaywrightException) { }

        var conversationId = GetConversationId(conversationUrl);
        var path = "not-captured";
        if (captureScreenshot)
        {
            var directory = Path.Combine(Path.GetTempPath(), "DCode", "diagnostics");
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, $"deepseek-composer-{profile.Id}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png");
            try { await page.ScreenshotAsync(new() { Path = path, FullPage = true }); }
            catch (PlaywrightException exception) { path = $"capture-failed: {exception.Message}"; }
        }

        logger.LogError(
            "DeepSeek composer readiness failed. Url={CurrentUrl} Title={PageTitle} ContentEditableCount={ContentEditableCount} LoginDetected={LoginDetected} ConversationId={ConversationId} Screenshot={ScreenshotPath}",
            page.Url, title, contentEditableCount, loginDetected, conversationId, path);
        return path;
    }

    internal static string GetConversationId(string? conversationUrl) =>
        string.IsNullOrWhiteSpace(conversationUrl)
            ? "new"
            : conversationUrl.TrimEnd('/').Split('/').LastOrDefault() ?? "unknown";

    private static async Task<string> WaitForConversationUrlAsync(IPage page, string? expectedUrl, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(expectedUrl))
            {
                if (!SameConversation(HomeUrl, page.Url)) return page.Url;
            }
            else if (SameConversation(expectedUrl, page.Url)) return expectedUrl;
            await Task.Delay(250, cancellationToken);
        }
        throw new RemoteConversationUnavailableException(string.IsNullOrWhiteSpace(expectedUrl)
            ? "DeepSeek did not create a remote conversation for this chat."
            : "The saved DeepSeek conversation no longer exists or could not be restored.");
    }

    internal static bool SameConversation(string expectedUrl, string actualUrl)
    {
        if (!Uri.TryCreate(expectedUrl, UriKind.Absolute, out var expected)
            || !Uri.TryCreate(actualUrl, UriKind.Absolute, out var actual)) return false;
        return string.Equals(expected.Host, actual.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(expected.AbsolutePath.TrimEnd('/'), actual.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);
    }

    private static async Task<string> WaitForResponseAsync(
        ILocator responses,
        int previousCount,
        string previousLastText,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        string? previousText = null;
        var stableChecks = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await responses.CountAsync();
            if (count > 0)
            {
                string text;
                if (count > previousCount)
                {
                    var newParts = new List<string>();
                    for (var index = previousCount; index < count; index++)
                    {
                        var part = await ExtractMarkdownAsync(responses.Nth(index));
                        if (!string.IsNullOrWhiteSpace(part)) newParts.Add(part);
                    }
                    text = CombineResponseParts(newParts);
                }
                else
                {
                    text = await ExtractMarkdownAsync(responses.Last);
                }
                var isNewResponse = count > previousCount
                    || !string.Equals(text, previousLastText, StringComparison.Ordinal);
                if (!isNewResponse)
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(text) && text == previousText) stableChecks++;
                else stableChecks = 0;
                previousText = text;
                if (stableChecks >= 4) return text;
            }
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException("DeepSeek did not finish responding within three minutes.");
    }

    internal static string CombineResponseParts(IEnumerable<string> parts)
    {
        var distinct = parts
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var maximal = distinct
            .Where(candidate => !distinct.Any(other =>
                other.Length > candidate.Length
                && other.Contains(candidate, StringComparison.Ordinal)))
            .ToArray();

        return string.Join(Environment.NewLine + Environment.NewLine, maximal);
    }

    internal static Task<string> ExtractMarkdownAsync(ILocator response) => response.EvaluateAsync<string>("""
        root => {
          const clean = value => value.replace(/\u00a0/g, ' ').replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
          const inline = node => {
            if (node.nodeType === Node.TEXT_NODE) return node.textContent || '';
            if (!(node instanceof HTMLElement)) return '';
            const tag = node.tagName.toLowerCase();
            const body = Array.from(node.childNodes).map(inline).join('');
            if (tag === 'strong' || tag === 'b') return `**${body}**`;
            if (tag === 'em' || tag === 'i') return `*${body}*`;
            if (tag === 'code' && node.parentElement?.tagName.toLowerCase() !== 'pre') return `\`${body}\``;
            if (tag === 'a') return `[${body}](${node.getAttribute('href') || ''})`;
            if (tag === 'br') return '\n';
            return body;
          };
          const block = (node, depth = 0) => {
            if (node.nodeType === Node.TEXT_NODE) return node.textContent || '';
            if (!(node instanceof HTMLElement)) return '';
            const tag = node.tagName.toLowerCase();
            if (['button', 'svg', 'style', 'script'].includes(tag) || node.getAttribute('role') === 'button') return '';
            if (/^h[1-6]$/.test(tag)) return `${'#'.repeat(Number(tag[1]))} ${inline(node)}\n\n`;
            if (tag === 'p') return `${inline(node)}\n\n`;
            if (tag === 'pre') {
              const code = node.querySelector('code');
              const language = (code?.className || '').match(/language-([\w-]+)/)?.[1] || '';
              return `\`\`\`${language}\n${(code?.textContent || node.textContent || '').trimEnd()}\n\`\`\`\n\n`;
            }
            if (tag === 'blockquote') return `${clean(Array.from(node.childNodes).map(child => block(child, depth)).join('')).split('\n').map(line => `> ${line}`).join('\n')}\n\n`;
            if (tag === 'ul' || tag === 'ol') return Array.from(node.children).map((child, index) => `${tag === 'ol' ? `${index + 1}.` : '-'} ${clean(Array.from(child.childNodes).map(item => block(item, depth + 1)).join(''))}`).join('\n') + '\n\n';
            if (tag === 'li') return inline(node);
            if (tag === 'table') {
              const rows = Array.from(node.querySelectorAll('tr')).map(row => Array.from(row.querySelectorAll('th,td')).map(cell => clean(inline(cell)).replace(/\|/g, '\\|')));
              if (!rows.length) return '';
              const width = Math.max(...rows.map(row => row.length));
              const header = rows[0];
              return `| ${header.join(' | ')} |\n| ${Array(width).fill('---').join(' | ')} |\n${rows.slice(1).map(row => `| ${row.join(' | ')} |`).join('\n')}\n\n`;
            }
            if (tag === 'hr') return '---\n\n';
            return Array.from(node.childNodes).map(child => block(child, depth)).join('');
          };
          return clean(Array.from(root.childNodes).map(node => block(node)).join(''));
        }
        """);
}
