using System.Text.Json;
using DCode.Server.Chats.AgentLoop;
using DCode.Server.Projects;
using DCode.Server.Tools;
using DCode.Server.Tools.BuiltIn;
using DCode.Server.Tools.Permissions;
using DCode.Server.Providers;
using DCode.Server.Chats;
using DCode.Server.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DCode.Server.Tests;

public sealed class ConversationRunnerTests : IDisposable
{
    private readonly string _projectPath = Path.Combine(Path.GetTempPath(), "dcode-runner-tests", Guid.NewGuid().ToString("N"));

    public ConversationRunnerTests() => Directory.CreateDirectory(_projectPath);

    [Theory]
    [InlineData("<tool>{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}</tool>")]
    [InlineData("<tool> {\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}} </tool>")]
    [InlineData("<tool>\n{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}\n</tool>")]
    [InlineData("  <tool>\n  {\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}  \n</tool>  ")]
    public void Parser_ParsesSameLineMultilineAndWhitespaceToolCalls(string response)
    {
        var parsed = new ToolCallParser().Parse(response);

        Assert.True(parsed.Success);
        var tool = Assert.IsType<ProviderToolCall>(Assert.Single(parsed.Segments));
        Assert.Equal("filesystem.read", tool.Call.ToolId);
        Assert.Equal("package.json", tool.Call.Arguments.GetProperty("path").GetString());
    }

    [Theory]
    [InlineData("<tool>{not json}</tool>")]
    [InlineData("<tool>{\"name\":\"filesystem.read\"}</tool>")]
    [InlineData("<tool>{\"name\":\"filesystem.read\",\"arguments\":{}}")] // Missing closing tag.
    [InlineData("<tool>{\"name\":\"filesystem.read\",\"arguments\":{}}</tool><tool>{\"name\":\"filesystem.list\",\"arguments\":{}}</tool>")]
    public void Parser_RejectsMalformedToolCall(string response)
    {
        Assert.False(new ToolCallParser().Parse(response).Success);
    }

    [Fact]
    public void Parser_PreservesToolHintFromMalformedDelegation()
    {
        var parsed = new ToolCallParser().Parse("<tool>{\"name\":\"agent.delegate\",\"arguments\":{\"agentRole\":\"coder\"");

        Assert.False(parsed.Success);
        Assert.Equal("tool_call_truncated", parsed.ErrorCode);
        Assert.Equal("agent.delegate", parsed.ToolIdHint);
    }

    [Theory]
    [InlineData("I will inspect it. <tool>{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}</tool>")]
    [InlineData("<tool>{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}</tool> I will continue after reading.")]
    public void Parser_ExtractsOneValidToolBlockDespiteSurroundingBrowserModelProse(string response)
    {
        var parsed = new ToolCallParser().Parse(response);
        Assert.True(parsed.Success);
        Assert.Contains(parsed.Segments, segment => segment is ProviderToolCall tool && tool.Call.ToolId == "filesystem.read");
    }

    [Fact]
    public void Parser_PreservesTextAndToolInSourceOrder()
    {
        var parsed = new ToolCallParser().Parse("Before. <tool>{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}</tool> After.");

        Assert.True(parsed.Success);
        Assert.Collection(parsed.Segments,
            segment => Assert.Equal("Before.", Assert.IsType<ProviderText>(segment).Content),
            segment => Assert.Equal("filesystem.read", Assert.IsType<ProviderToolCall>(segment).Call.ToolId),
            segment => Assert.Equal("After.", Assert.IsType<ProviderText>(segment).Content));
    }

    [Fact]
    public async Task Runner_EmitsIntermediateProviderTextWithoutDuplicatingItInFinalContent()
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "package.json"), "{\"name\":\"dcode-test\"}");
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider([
            "I will edit the file. <tool>{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}</tool>",
            "I edited successfully."
        ]);

        var result = await CreateRunner(provider).RunAsync(Request() with { RunId = "stable-run", Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } }, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("I edited successfully.", result.FinalContent);
        var intermediate = Assert.Single(progress, item => item.Stage == "provider_text");
        var final = Assert.Single(progress, item => item.Stage == "final_response");
        Assert.Equal("I will edit the file.", intermediate.Label);
        Assert.NotEqual(intermediate.Id, final.Id);
        Assert.Equal(progress.Count, progress.Select(item => item.Id).Distinct().Count());
        Assert.Equal("I will edit the file.", intermediate.Label);
    }

    [Fact]
    public async Task ProjectChat_ReloadPreservesIntermediateToolAndFinalAsDistinctRecords()
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "package.json"), "{\"name\":\"dcode-test\"}");
        var events = new List<ConversationProgress>();
        var provider = new FakeProvider([
            "I will edit the file. <tool>{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}</tool>",
            "I edited successfully."
        ]);
        var result = await CreateRunner(provider).RunAsync(Request() with
        {
            RunId = "identity-run",
            Progress = (progress, _) =>
            {
                var index = events.FindIndex(item => item.Id == progress.Id);
                if (index < 0) events.Add(progress); else events[index] = progress;
                return ValueTask.CompletedTask;
            }
        }, CancellationToken.None);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:DatabasePath"] = Path.Combine(_projectPath, "identity.db")
        }).Build();
        var database = new DCodeDatabase(configuration); database.Initialize();
        var profiles = new BrowserProfileRepository(database); var profile = profiles.Create(new("Identity account")); profiles.MarkConnected(profile.Id);
        var chats = new ChatRepository(database); var chat = chats.Create(profiles.Get(profile.Id));
        var user = chats.SaveUserMessage(chat.Id, "Edit it").Messages.Last();
        chats.SaveExecutionTurn("identity-run", chat.Id, user.Id, result.Status, result.Error, events);
        chats.SaveAssistantMessage(chat.Id, result.FinalContent!, result.ConversationReference!);

        var restoredRepository = new ChatRepository(new DCodeDatabase(configuration));
        var restoredEvents = Assert.Single(restoredRepository.GetExecutionTurns(chat.Id)).Events;
        var intermediate = Assert.Single(restoredEvents, item => item.Stage == "provider_text");
        var final = Assert.Single(restoredEvents, item => item.Stage == "final_response");
        Assert.Equal("I will edit the file.", intermediate.Label);
        Assert.NotEqual(intermediate.Id, final.Id);
        Assert.Equal("I edited successfully.", restoredRepository.GetMessages(chat.Id).Last().Content);
    }

    [Fact]
    public async Task Runner_AskPolicyPersistsPendingCallAsWaitingPermissionEvent()
    {
        var progress = new List<ConversationProgress>();
        var result = await CreateRunner(new FakeProvider([Tool("filesystem.write", new { path = "new.txt", content = "value" })]), ToolPermissionPolicy.Ask)
            .RunAsync(Request() with { Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } }, CancellationToken.None);

        Assert.Equal("waiting_permission", result.Status);
        var pending = Assert.Single(progress, item => item.Stage == "permission_required");
        Assert.Equal("waiting", pending.Status);
        Assert.Contains("filesystem.write", pending.Detail);
    }

    [Fact]
    public void DeepSeekResponseAggregation_RemovesDuplicateAndNestedMarkdownFragments()
    {
        const string toolBlock = "<tool>\n{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}\n</tool>";
        var combined = DeepSeekBrowserChatAdapter.CombineResponseParts([toolBlock, toolBlock, "filesystem.read"]);

        Assert.Equal(toolBlock, combined);
        Assert.True(new ToolCallParser().Parse(combined).Success);
    }

    [Theory]
    [InlineData("https://chat.deepseek.com/a/chat/s/abc", "https://chat.deepseek.com/a/chat/s/abc", true)]
    [InlineData("https://chat.deepseek.com/a/chat/s/abc/", "https://chat.deepseek.com/a/chat/s/abc", true)]
    [InlineData("https://chat.deepseek.com/a/chat/s/abc", "https://chat.deepseek.com/a/chat/s/other", false)]
    [InlineData("https://chat.deepseek.com/a/chat/s/abc", "https://chat.deepseek.com/", false)]
    public void DeepSeekConversationMatching_DoesNotAllowConversationDrift(string expected, string actual, bool matches)
    {
        Assert.Equal(matches, DeepSeekBrowserChatAdapter.SameConversation(expected, actual));
    }

    [Fact]
    public void DeepSeekDiagnostics_ExtractConversationId()
    {
        Assert.Equal("abc", DeepSeekBrowserChatAdapter.GetConversationId("https://chat.deepseek.com/a/chat/s/abc"));
        Assert.Equal("new", DeepSeekBrowserChatAdapter.GetConversationId(null));
    }

    [Fact]
    public async Task Runner_UnknownToolRecoversWithAvailableToolInSameConversation()
    {
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider([Tool("unknown.tool", new { }), Tool("filesystem.list", new { path = "." }), "Recovered."]);
        var result = await CreateRunner(provider).RunAsync(Request() with { Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } }, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("Recovered.", result.FinalContent);
        Assert.Contains(progress, item => item.Stage == "tool_call_rejected" && item.Detail!.Contains("tool_call_unknown"));
        Assert.Contains(progress, item => item.Stage == "provider_retry_completed");
        Assert.Contains("filesystem.read", provider.Messages[1]);
        Assert.All(provider.Sessions.Skip(1), session => Assert.Equal("conversation-1", session.ConversationReference));
    }

    [Theory]
    [InlineData("<tool>{not json}</tool>", "tool_call_malformed")]
    [InlineData("<tool>{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}", "tool_call_truncated")]
    public async Task Runner_InvalidToolPayloadRetriesThenContinuesNormally(string invalidResponse, string expectedCode)
    {
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider([invalidResponse, Tool("filesystem.list", new { path = "." }), "Recovered final response."]);

        var result = await CreateRunner(provider).RunAsync(Request() with { Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } }, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("Recovered final response.", result.FinalContent);
        Assert.Contains(progress, item => item.Stage == "tool_call_rejected" && item.Detail!.Contains(expectedCode));
        Assert.Contains(progress, item => item.Stage == "provider_retry_started");
        Assert.Contains(progress, item => item.Stage == "provider_retry_completed");
        Assert.DoesNotContain(provider.Messages[1], result.FinalContent!);
        Assert.All(provider.Sessions.Skip(1), session => Assert.Equal("conversation-1", session.ConversationReference));
    }

    [Fact]
    public async Task Runner_OversizedToolPayloadRetriesWithSmallerSequentialCall()
    {
        var oversized = $"<tool>{{\"name\":\"filesystem.edit\",\"arguments\":{{\"path\":\"large.txt\",\"oldText\":\"{new string('x', ToolCallParser.MaximumPayloadCharacters)}\",\"newText\":\"small\"}}}}</tool>";
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider([oversized, Tool("filesystem.list", new { path = "." }), "Split into a smaller operation."]);

        var result = await CreateRunner(provider).RunAsync(Request() with { Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } }, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Contains(progress, item => item.Stage == "tool_call_rejected" && item.Detail!.Contains("tool_call_too_large"));
        Assert.Contains("split it into smaller sequential tool calls", provider.Messages[1]);
        Assert.Contains("avoid large oldText/newText", provider.Messages[1]);
    }

    [Fact]
    public async Task Runner_RecoveryLimitReturnsControlledFailureWithoutExposingRecoveryAsFinalContent()
    {
        var invalid = "<tool>{bad}</tool>";
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider([invalid, invalid, invalid, invalid]);

        var result = await CreateRunner(provider).RunAsync(Request() with { Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } }, CancellationToken.None);

        Assert.Equal("tool_call_malformed", result.Status);
        Assert.Null(result.FinalContent);
        Assert.Equal(ConversationRunner.MaximumRecoveryAttempts, progress.Count(item => item.Stage == "provider_retry_started"));
        Assert.Contains(progress, item => item.Stage == "provider_retry_failed");
        Assert.All(provider.Sessions.Skip(1), session => Assert.Equal("conversation-1", session.ConversationReference));
    }

    [Fact]
    public async Task Runner_EnablesWriteToolButStillRequiresStoredPermissionApproval()
    {
        var result = await CreateRunner(
            new FakeProvider([Tool("filesystem.write", new { path = "unsafe.txt", content = "no" })]),
            ToolPermissionPolicy.Ask).RunAsync(Request(), CancellationToken.None);

        Assert.Equal("waiting_permission", result.Status);
        Assert.False(File.Exists(Path.Combine(_projectPath, "unsafe.txt")));
    }

    [Fact]
    public async Task Runner_ExecutesMultipleToolsInSameConversationThenReturnsFinalResponse()
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "package.json"), "{\"name\":\"dcode-test\"}");
        var provider = new FakeProvider([
            Tool("filesystem.read", new { path = "package.json" }),
            Tool("filesystem.search", new { query = "dcode-test", path = "." }),
            "The project name is dcode-test."
        ]);

        var result = await CreateRunner(provider).RunAsync(Request(), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("The project name is dcode-test.", result.FinalContent);
        Assert.Equal(2, result.Activities.Count);
        Assert.Equal(3, provider.Messages.Count);
        Assert.Null(provider.Sessions[0].ConversationReference);
        Assert.Equal("conversation-1", provider.Sessions[1].ConversationReference);
        Assert.Contains("<tool_result>", provider.Messages[1]);
    }

    [Fact]
    public async Task Runner_FirstMessageCreatesAndReportsRemoteConversationBinding()
    {
        string? boundReference = null;
        var provider = new FakeProvider(["Created."]);

        var result = await CreateRunner(provider).RunAsync(
            Request() with { LocalConversationId = "local-1", ConversationReferenceChanged = value => boundReference = value },
            CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Null(provider.Sessions[0].ConversationReference);
        Assert.Equal("conversation-1", boundReference);
    }

    [Fact]
    public async Task Runner_SecondMessageReusesExistingRemoteConversation()
    {
        var provider = new FakeProvider(["Continued."]);
        var request = Request() with
        {
            ProviderSession = Request().ProviderSession with { ConversationReference = "conversation-1" }
        };

        var result = await CreateRunner(provider).RunAsync(request, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("conversation-1", provider.Sessions[0].ConversationReference);
    }

    [Fact]
    public async Task Runner_ToolResultContinuationReusesCreatedRemoteConversation()
    {
        var provider = new FakeProvider([Tool("filesystem.list", new { path = "." }), "Done."]);

        var result = await CreateRunner(provider).RunAsync(Request(), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Null(provider.Sessions[0].ConversationReference);
        Assert.Equal("conversation-1", provider.Sessions[1].ConversationReference);
    }

    [Fact]
    public async Task Runner_ReportsProviderAndToolProgressInExecutionOrder()
    {
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider([Tool("filesystem.list", new { path = "." }), "Done."]);
        var request = Request() with
        {
            Progress = (item, _) =>
            {
                progress.Add(item);
                return ValueTask.CompletedTask;
            }
        };

        var result = await CreateRunner(provider).RunAsync(request, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Collection(progress,
            item => Assert.Equal(("provider_started", "running"), (item.Stage, item.Status)),
            item => Assert.Equal(("provider_response", "completed"), (item.Stage, item.Status)),
            item => Assert.Equal(("tool_requested", "completed"), (item.Stage, item.Status)),
            item => Assert.Equal(("tool_started", "running"), (item.Stage, item.Status)),
            item => Assert.Equal(("tool_completed", "completed"), (item.Stage, item.Status)),
            item => Assert.Equal(("continuation_started", "completed"), (item.Stage, item.Status)),
            item => Assert.Equal(("provider_started", "running"), (item.Stage, item.Status)),
            item => Assert.Equal(("provider_response", "completed"), (item.Stage, item.Status)),
            item => Assert.Equal(("final_response", "completed"), (item.Stage, item.Status)));
        Assert.Contains("filesystem.list", progress[2].Detail);
    }

    [Fact]
    public async Task Runner_ReadsThenEditsFileAndReportsRealToolLifecycle()
    {
        var file = Path.Combine(_projectPath, "src", "mock", "fornecedores.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "export const fornecedores = ['Existing'];");
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider([
            Tool("filesystem.read", new { path = "src/mock/fornecedores.ts" }),
            Tool("filesystem.edit", new { path = "src/mock/fornecedores.ts", oldText = "['Existing']", newText = "['Existing', 'Fictional']" }),
            "## Updated\n\nAdded one fictional supplier."
        ]);
        var request = Request() with { Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } };

        var result = await CreateRunner(provider).RunAsync(request, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Contains("Fictional", await File.ReadAllTextAsync(file));
        Assert.Contains(progress, item => item.Label == "Reading src/mock/fornecedores.ts" && item.Status == "completed");
        Assert.Contains(progress, item => item.Label == "Editing src/mock/fornecedores.ts" && item.Status == "completed");
        Assert.Equal("## Updated\n\nAdded one fictional supplier.", result.FinalContent);
    }

    [Fact]
    public async Task Runner_SyntaxFailureReturnsDiagnosticsAndProviderRetryCanSucceed()
    {
        var path = Path.Combine(_projectPath, "retry-validation.ts");
        await File.WriteAllTextAsync(path, "export const value = { count: 1 };\n");
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider([
            Tool("filesystem.edit", new { path = "retry-validation.ts", oldText = " };", newText = ";" }),
            Tool("filesystem.read", new { path = "retry-validation.ts", startLine = 1, endLine = 4 }),
            Tool("filesystem.edit", new { path = "retry-validation.ts", oldText = "count: 1", newText = "count: 2" }),
            "The valid edit was applied."
        ]);

        var result = await CreateRunner(provider).RunAsync(Request() with { Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } }, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("The valid edit was applied.", result.FinalContent);
        Assert.Contains(progress, item => item.Stage == "tool_failed" && item.Detail!.Contains("syntax_validation_failed"));
        Assert.Contains(progress, item => item.Stage == "recovery_started");
        Assert.Contains(progress, item => item.Label.Contains("lines 1–4") && item.Stage == "tool_completed");
        Assert.Contains(progress, item => item.Stage == "recovery_completed");
        Assert.Contains(progress, item => item.Stage == "tool_completed");
        Assert.Single(progress, item => item.Stage == "final_response");
        Assert.All(provider.Sessions.Skip(1), session => Assert.Equal("conversation-1", session.ConversationReference));
        Assert.Equal("export const value = { count: 2 };\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Runner_RepeatedRecoverableEditFailuresExhaustRecoveryLimit()
    {
        var path = Path.Combine(_projectPath, "exhaust.ts");
        const string original = "export const value = { count: 1 };\n";
        await File.WriteAllTextAsync(path, original);
        var failedEdit = Tool("filesystem.edit", new { path = "exhaust.ts", oldText = " };", newText = ";" });
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider(Enumerable.Repeat(failedEdit, ConversationRunner.MaximumRecoverableToolFailures + 1).ToArray());

        var result = await CreateRunner(provider).RunAsync(Request() with { Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } }, CancellationToken.None);

        Assert.Equal("recovery_exhausted", result.Status);
        Assert.Contains(progress, item => item.Stage == "recovery_exhausted");
        Assert.Equal(ConversationRunner.MaximumRecoverableToolFailures, progress.Count(item => item.Stage == "recovery_started"));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.All(provider.Sessions.Skip(1), session => Assert.Equal("conversation-1", session.ConversationReference));
    }

    [Fact]
    public async Task Runner_ReturnsEditGuardrailAndAllowsProviderToRetryWithInsert()
    {
        var file = Path.Combine(_projectPath, "retry.ts");
        await File.WriteAllTextAsync(file, "const values = [];\n");
        var provider = new FakeProvider([
            Tool("filesystem.edit", new { path = "retry.ts", oldText = new string('x', EditFileTool.MaximumTextLength + 1), newText = "large" }),
            Tool("filesystem.insert", new { path = "retry.ts", anchor = "[]", position = "after", content = " as string[]" }),
            "Updated with a compact insertion."
        ]);

        var result = await CreateRunner(provider).RunAsync(Request(), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal(3, provider.Messages.Count);
        Assert.Contains("string[]", await File.ReadAllTextAsync(file));
        Assert.Contains("edit_too_large", provider.Messages[1]);
    }

    [Fact]
    public async Task Runner_ReturnsControlledStateWhenRemoteConversationIsMissing()
    {
        var result = await CreateRunner(new MissingConversationProvider()).RunAsync(
            Request() with { ProviderSession = Request().ProviderSession with { ConversationReference = "missing" } },
            CancellationToken.None);

        Assert.Equal("remote_conversation_unavailable", result.Status);
        Assert.Contains("no longer exists", result.Error);
    }

    [Fact]
    public async Task Runner_StopsAtIterationLimit()
    {
        var provider = new FakeProvider(Enumerable.Repeat(Tool("filesystem.list", new { path = "." }), ConversationRunner.MaximumIterations).ToArray());

        var result = await CreateRunner(provider).RunAsync(Request(), CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal(ConversationRunner.MaximumIterations, provider.Messages.Count);
    }

    [Fact]
    public async Task Runner_RequiresActiveProject()
    {
        var provider = new FakeProvider(["unused"]);
        var request = Request() with { Project = null };

        var result = await CreateRunner(provider).RunAsync(request, CancellationToken.None);

        Assert.Equal("no_active_project", result.Status);
        Assert.Empty(provider.Messages);
    }

    [Theory]
    [InlineData(ToolPermissionPolicy.Ask, "waiting_permission")]
    [InlineData(ToolPermissionPolicy.Deny, "failed")]
    public async Task Runner_StopsForNonAllowPolicy(ToolPermissionPolicy policy, string expectedStatus)
    {
        var provider = new FakeProvider([Tool("filesystem.list", new { path = "." })]);

        var result = await CreateRunner(provider, policy).RunAsync(Request(), CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Single(provider.Messages);
    }

    [Fact]
    public async Task Runner_ReturnsNormalProviderResponseWithoutDisplayingProtocol()
    {
        var result = await CreateRunner(new FakeProvider(["A normal final response."])).RunAsync(Request(), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("A normal final response.", result.FinalContent);
        Assert.Empty(result.Activities);
    }

    [Fact]
    public void ProjectChat_UserMessagePersistsBeforeAssistantContinuation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DatabasePath"] = Path.Combine(_projectPath, "chat.db") })
            .Build();
        var database = new DCodeDatabase(configuration);
        database.Initialize();
        var profileRepository = new BrowserProfileRepository(database);
        var profile = profileRepository.Create(new CreateBrowserProfileRequest("Test account"));
        profileRepository.MarkConnected(profile.Id);
        var chats = new ChatRepository(database);
        var chat = chats.Create(profileRepository.Get(profile.Id));

        var exchange = chats.SaveUserMessage(chat.Id, "Read package.json");

        var message = Assert.Single(exchange.Messages);
        Assert.Equal("user", message.Role);
        Assert.Equal("Read package.json", message.Content);
        Assert.DoesNotContain(exchange.Messages, item => item.Role == "assistant");
    }

    [Fact]
    public void ProjectChat_AppRestartRestoresRemoteConversationBinding()
    {
        var databasePath = Path.Combine(_projectPath, "restart-chat.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DatabasePath"] = databasePath })
            .Build();
        var database = new DCodeDatabase(configuration);
        database.Initialize();
        var profiles = new BrowserProfileRepository(database);
        var profile = profiles.Create(new CreateBrowserProfileRequest("Test account"));
        profiles.MarkConnected(profile.Id);
        var chat = new ChatRepository(database).Create(profiles.Get(profile.Id));
        new ChatRepository(database).BindRemoteConversation(chat.Id, "https://chat.deepseek.com/a/chat/s/remote-1");

        var restoredDatabase = new DCodeDatabase(configuration);
        restoredDatabase.Initialize();
        var restored = new ChatRepository(restoredDatabase).Get(chat.Id);

        Assert.Equal("remote-1", restored.ProviderConversationId);
        Assert.Equal("https://chat.deepseek.com/a/chat/s/remote-1", restored.ProviderConversationUrl);
    }

    [Fact]
    public void ProjectChat_AppRestartRestoresExecutionTimelineAndError()
    {
        var databasePath = Path.Combine(_projectPath, "execution-state.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DatabasePath"] = databasePath })
            .Build();
        var database = new DCodeDatabase(configuration);
        database.Initialize();
        var profiles = new BrowserProfileRepository(database);
        var profile = profiles.Create(new CreateBrowserProfileRequest("Test account"));
        profiles.MarkConnected(profile.Id);
        var chats = new ChatRepository(database);
        var chat = chats.Create(profiles.Get(profile.Id));
        chats.SaveExecutionState(chat.Id, "tool_failed", "Unsafe path skipped.",
            [new ConversationProgress("tool-1", "tool", "Listing files", "failed", 12, "{\"name\":\"filesystem.list\"}")]);

        var restoredDatabase = new DCodeDatabase(configuration);
        restoredDatabase.Initialize();
        var restored = new ChatRepository(restoredDatabase).GetExecutionState(chat.Id);

        Assert.NotNull(restored);
        Assert.Equal("tool_failed", restored.Status);
        Assert.Equal("Unsafe path skipped.", restored.Error);
        Assert.Contains("filesystem.list", restored.Progress[0].Detail);
    }

    [Fact]
    public void ProjectChat_PersistsOrderedExecutionTurnsPerUserMessage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DatabasePath"] = Path.Combine(_projectPath, "turns.db") })
            .Build();
        var database = new DCodeDatabase(configuration);
        database.Initialize();
        var profiles = new BrowserProfileRepository(database);
        var profile = profiles.Create(new CreateBrowserProfileRequest("Test account"));
        profiles.MarkConnected(profile.Id);
        var chats = new ChatRepository(database);
        var chat = chats.Create(profiles.Get(profile.Id));
        var first = chats.SaveUserMessage(chat.Id, "First").Messages.Last();
        var second = chats.SaveUserMessage(chat.Id, "Second").Messages.Last();
        chats.SaveExecutionTurn("turn-1", chat.Id, first.Id, "completed", null, [new("tool-1", "tool", "Reading a", "completed")]);
        chats.SaveExecutionTurn("turn-2", chat.Id, second.Id, "tool_failed", "failed", [new("tool-2", "tool", "Reading b", "failed")]);

        var turns = new ChatRepository(database).GetExecutionTurns(chat.Id);

        Assert.Equal(2, turns.Count);
        Assert.Equal(first.Id, turns[0].UserMessageId);
        Assert.Equal(second.Id, turns[1].UserMessageId);
        Assert.Equal("Reading b", turns[1].Events[0].Label);
    }

    [Fact]
    public void PendingApproval_ReloadsAndCanOnlyBeResolvedOnce()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DatabasePath"] = Path.Combine(_projectPath, "pending.db") }).Build();
        var database = new DCodeDatabase(configuration); database.Initialize();
        var profiles = new BrowserProfileRepository(database); var profile = profiles.Create(new("Approval account")); profiles.MarkConnected(profile.Id);
        var chats = new ChatRepository(database); var chat = chats.Create(profiles.Get(profile.Id)); var user = chats.SaveUserMessage(chat.Id, "Edit file").Messages.Last();
        var repository = new PendingToolApprovalRepository(database);
        repository.Save(new("run-1", "tool-1", chat.Id, user.Id, "project-1", "filesystem.edit", JsonSerializer.SerializeToElement(new { path = "a.ts", oldText = "a", newText = "b" }), "filesystem.write", "conversation-1", 1, "pending", DateTimeOffset.UtcNow));
        repository.Save(new("run-1", "tool-2", chat.Id, user.Id, "project-1", "filesystem.edit", JsonSerializer.SerializeToElement(new { path = "a.ts", oldText = "b", newText = "c" }), "filesystem.write", "conversation-1", 2, "pending", DateTimeOffset.UtcNow.AddMilliseconds(1), "run-1:permission-required:1"));

        var restored = new PendingToolApprovalRepository(new DCodeDatabase(configuration)).GetForChat(chat.Id);
        Assert.NotNull(restored); Assert.Equal("tool-2", restored.ToolCallId); Assert.Equal("run-1:permission-required:1", restored.EventId);
        Assert.True(repository.TryResolve("run-1", "tool-1", "denied"));
        Assert.False(repository.TryResolve("run-1", "tool-1", "approved"));
        Assert.Equal("pending", repository.Get("run-1", "tool-2")!.Status);
        var history = new PendingToolApprovalRepository(new DCodeDatabase(configuration)).ListForChat(chat.Id);
        Assert.Equal(2, history.Count); Assert.Equal("denied", history[0].Status); Assert.Equal("pending", history[1].Status);
        var projectHistory = repository.ListForProject("project-1");
        Assert.Equal(2, projectHistory.Count); Assert.All(projectHistory, approval => Assert.Equal("project-1", approval.ProjectId));
        Assert.True(repository.TryResolve("run-1", "tool-2", "denied"));
        Assert.Null(repository.Get("missing", "missing"));
        Assert.Null(repository.GetForChat(chat.Id));
    }

    [Fact]
    public async Task Runner_AllowOnceExecutesPendingToolAndResumesSameRemoteConversation()
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "approval.txt"), "before");
        var provider = new FakeProvider(["Approved edit completed."]);
        var progress = new List<ConversationProgress>();
        var runner = CreateRunner(provider, ToolPermissionPolicy.Ask);
        var request = Request() with { ProviderSession = Request().ProviderSession with { ConversationReference = "conversation-1" }, Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } };
        var pending = new PendingConversationToolCall("tool-0", new("filesystem.edit", JsonSerializer.SerializeToElement(new { path = "approval.txt", oldText = "before", newText = "after" })), 1, "filesystem.write");

        var result = await runner.ResumeApprovedAsync(request, pending, CancellationToken.None);

        Assert.Equal("completed", result.Status); Assert.Equal("after", await File.ReadAllTextAsync(Path.Combine(_projectPath, "approval.txt")));
        Assert.Equal("conversation-1", provider.Sessions.Single().ConversationReference);
        Assert.Contains(progress, item => item.Stage == "permission_granted");
        Assert.Contains(progress, item => item.Stage == "tool_completed");
    }

    [Fact]
    public async Task Runner_TwoSequentialAskCallsUseDistinctApprovalsAndSecondResumesSameRun()
    {
        var path = Path.Combine(_projectPath, "sequential.ts");
        await File.WriteAllTextAsync(path, "export const value = { count: 1 };\n");
        var provider = new FakeProvider([
            Tool("filesystem.edit", new { path = "sequential.ts", oldText = " };", newText = ";" }),
            Tool("filesystem.edit", new { path = "sequential.ts", oldText = "count: 1", newText = "count: 2" }),
            "Recovered and edited."
        ]);
        var runner = CreateRunner(provider, ToolPermissionPolicy.Ask);
        var request = Request() with { RunId = "same-run" };

        var first = await runner.RunAsync(request, CancellationToken.None);
        var firstPending = Assert.IsType<PendingConversationToolCall>(first.PendingToolCall);
        var bound = request with { ProviderSession = request.ProviderSession with { ConversationReference = first.ConversationReference } };
        var second = await runner.ResumeApprovedAsync(bound, firstPending, CancellationToken.None);
        var secondPending = Assert.IsType<PendingConversationToolCall>(second.PendingToolCall);

        Assert.Equal("waiting_permission", second.Status);
        Assert.NotEqual(firstPending.ToolCallId, secondPending.ToolCallId);
        Assert.NotEqual(firstPending.EventId, secondPending.EventId);
        var completed = await runner.ResumeApprovedAsync(bound, secondPending, CancellationToken.None);
        Assert.Equal("completed", completed.Status);
        Assert.Equal("Recovered and edited.", completed.FinalContent);
        Assert.Equal("export const value = { count: 2 };\n", await File.ReadAllTextAsync(path));
        Assert.All(provider.Sessions.Skip(1), session => Assert.Equal("conversation-1", session.ConversationReference));
    }

    [Fact]
    public async Task Runner_AlwaysAllowAfterFirstPromptPreventsSecondPrompt()
    {
        var path = Path.Combine(_projectPath, "always-sequential.ts");
        await File.WriteAllTextAsync(path, "export const value = { count: 1 };\n");
        var policy = new MutablePolicyResolver(ToolPermissionPolicy.Ask);
        var provider = new FakeProvider([
            Tool("filesystem.edit", new { path = "always-sequential.ts", oldText = "count: 1", newText = "count: 2" }),
            Tool("filesystem.edit", new { path = "always-sequential.ts", oldText = "count: 2", newText = "count: 3" }),
            "Both edits completed."
        ]);
        var runner = CreateRunner(provider, resolver: policy);
        var request = Request() with { RunId = "always-run" };
        var first = await runner.RunAsync(request, CancellationToken.None);
        policy.Policy = ToolPermissionPolicy.Allow;

        var completed = await runner.ResumeApprovedAsync(request with { ProviderSession = request.ProviderSession with { ConversationReference = first.ConversationReference } }, first.PendingToolCall!, CancellationToken.None);

        Assert.Equal("completed", completed.Status);
        Assert.Null(completed.PendingToolCall);
        Assert.Equal("export const value = { count: 3 };\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void AlwaysAllowPolicyPersistsForLaterExecutions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DatabasePath"] = Path.Combine(_projectPath, "always.db") }).Build();
        var database = new DCodeDatabase(configuration); database.Initialize(); var policies = new ToolPolicyRepository(database);
        policies.SetGlobal("filesystem.edit", ToolPermissionPolicy.Allow);
        Assert.Equal(ToolPermissionPolicy.Allow, new ToolPolicyRepository(new DCodeDatabase(configuration)).GetGlobal("filesystem.edit"));
    }

    [Fact]
    public async Task LeadDelegation_StreamsChildActivityAndContinuesSameProviderConversation()
    {
        var provider=new FakeProvider([Tool("agent.delegate",new{agentRole="coder",objective="Implement the parser"}),"Coder finished and I verified the result."]);var progress=new List<ConversationProgress>();var calls=0;
        var result=await CreateRunner(provider).RunAsync(Request() with{RunId="lead-run",Progress=(item,_)=>{progress.Add(item);return ValueTask.CompletedTask;},Delegation=async(call,report,_)=>{calls++;Assert.Equal("coder",call.AgentRole);Assert.Equal("Implement the parser",call.Objective);await report(new("child:event","tool_completed","Edited parser.ts","completed"),CancellationToken.None);return new("completed","child-run","Parser implemented",null);}},CancellationToken.None);
        Assert.Equal("completed",result.Status);Assert.Equal(1,calls);Assert.Equal("Coder finished and I verified the result.",result.FinalContent);Assert.Contains(progress,item=>item.Stage=="delegation_started");Assert.Contains(progress,item=>item.Id=="child:event");Assert.Contains(progress,item=>item.Stage=="delegation_completed");Assert.Contains("delegation_result",provider.Messages[1]);Assert.All(provider.Sessions.Skip(1),session=>Assert.Equal("conversation-1",session.ConversationReference));
    }

    [Fact]
    public async Task LeadDelegation_MalformedPostCompletionOutputFallsBackToVerifiedChildResult()
    {
        var malformed = "<tool>{\"name\":\"filesystem.read\",\"arguments\":{}";
        var provider = new FakeProvider([
            Tool("agent.delegate", new { agentRole = "coder", objective = "Implement and build the dashboard" }),
            malformed,
            malformed
        ]);
        var progress = new List<ConversationProgress>();

        var result = await CreateRunner(provider).RunAsync(Request() with
        {
            RunId = "lead-final-fallback",
            Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; },
            Delegation = (_, _, _) => Task.FromResult(new AgentDelegationOutcome("completed", "child-run", "Dashboard implemented. npm run build passed.", null))
        }, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("Dashboard implemented. npm run build passed.", result.FinalContent);
        Assert.Single(progress, item => item.Stage == "provider_retry_started");
        Assert.Contains(progress, item => item.Stage == "final_response" && item.Detail!.Contains("delegated_result"));
        Assert.All(provider.Sessions.Skip(1), session => Assert.Equal("conversation-1", session.ConversationReference));
    }

    [Fact]
    public async Task LeadMalformedPlanningOutputFallsBackToCoderWithOriginalObjective()
    {
        const string objective = "Redesign the dashboard to fit the existing project layout.";
        var malformed = "<tool>{\"name\":\"agent.delegate\",\"arguments\":{\"agentRole\":\"coder\",\"objective\":\"Redesign";
        var provider = new FakeProvider([malformed, malformed, "The dashboard was redesigned and the build passed."]);
        var delegated = 0;
        var progress = new List<ConversationProgress>();

        var result = await CreateRunner(provider).RunAsync(Request() with
        {
            UserMessage = objective,
            RunId = "lead-malformed-fallback",
            Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; },
            Delegation = (call, _, _) =>
            {
                delegated++;
                Assert.Equal("coder", call.AgentRole);
                Assert.Equal(objective, call.Objective);
                return Task.FromResult(new AgentDelegationOutcome("completed", "child-run", "Dashboard redesigned; build passed.", null));
            }
        }, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal(1, delegated);
        Assert.Equal("The dashboard was redesigned and the build passed.", result.FinalContent);
        Assert.Single(progress, item => item.Stage == "provider_retry_started");
        Assert.Contains(progress, item => item.Stage == "delegation_started" && item.Label.Contains("Recovering"));
        Assert.DoesNotContain(progress, item => item.Stage == "error" && item.Label.Contains("recovery limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LeadDelegation_RejectsExtraToolAfterCompletedChildAndRequestsFinalProse()
    {
        var provider = new FakeProvider([
            Tool("agent.delegate", new { agentRole = "coder", objective = "Implement the dashboard" }),
            Tool("filesystem.read", new { path = "src/App.tsx" }),
            "The dashboard was implemented and verified successfully."
        ]);
        var progress = new List<ConversationProgress>();

        var result = await CreateRunner(provider).RunAsync(Request() with
        {
            RunId = "lead-final-tool-rejected",
            Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; },
            Delegation = (_, _, _) => Task.FromResult(new AgentDelegationOutcome("completed", "child-run", "Dashboard implemented and verified.", null))
        }, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("The dashboard was implemented and verified successfully.", result.FinalContent);
        Assert.Empty(result.Activities);
        Assert.Contains(progress, item => item.Stage == "tool_call_rejected" && item.Detail!.Contains("post_delegation_tool_rejected"));
        Assert.Contains("Output prose only", provider.Messages[2], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runner_InvalidReadRangeIsRecoverableAndUsesReturnedBounds()
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "short.ts"), "export const value = 1;\n");
        var provider = new FakeProvider([
            Tool("filesystem.read", new { path = "short.ts", startLine = 200, endLine = 300 }),
            Tool("filesystem.read", new { path = "short.ts", startLine = 1, endLine = 1 }),
            "The file contains one exported value."
        ]);

        var result = await CreateRunner(provider).RunAsync(Request(), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Contains("choose a valid range within that bound", provider.Messages[1]);
        Assert.Equal("The file contains one exported value.", result.FinalContent);
    }

    [Fact]
    public async Task Runner_ReusesIdenticalSuccessfulInspectionWithinOneRun()
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "package.json"), "{\"name\":\"stable\"}");
        var progress = new List<ConversationProgress>();
        var provider = new FakeProvider([
            Tool("filesystem.read", new { path = "package.json" }),
            Tool("filesystem.read", new { path = "package.json" }),
            "The project name is stable."
        ]);

        var result = await CreateRunner(provider).RunAsync(Request() with { Progress = (item, _) => { progress.Add(item); return ValueTask.CompletedTask; } }, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Single(result.Activities);
        Assert.Contains(progress, item => item.Stage == "tool_reused");
    }

    [Fact]
    public async Task LeadDelegation_PreservesWaitingChildWithoutInventingFinalResponse()
    {
        var provider=new FakeProvider([Tool("agent.delegate",new{agentRole="coder",objective="Edit the file"})]);
        var result=await CreateRunner(provider).RunAsync(Request() with{RunId="lead-run",Delegation=(call,report,_)=>Task.FromResult(new AgentDelegationOutcome("waiting_permission","child-run",null,"Approval required"))},CancellationToken.None);
        Assert.Equal("waiting_delegation",result.Status);Assert.Null(result.FinalContent);Assert.Equal("conversation-1",result.ConversationReference);
    }

    [Fact]
    public async Task LeadDelegation_FailedChildReturnsControlInsteadOfWaiting()
    {
        var provider=new FakeProvider([Tool("agent.delegate",new{agentRole="coder",objective="Implement dashboard"}),"The Coder could not finish within this run, so I stopped without claiming success."]);var progress=new List<ConversationProgress>();
        var result=await CreateRunner(provider).RunAsync(Request() with{RunId="lead-failed-child",Progress=(item,_)=>{progress.Add(item);return ValueTask.CompletedTask;},Delegation=(_,_,_)=>Task.FromResult(new AgentDelegationOutcome("failed","child-run",null,"Tool iteration limit reached"))},CancellationToken.None);
        Assert.Equal("completed",result.Status);Assert.Contains(progress,item=>item.Stage=="delegation_failed"&&item.Status=="failed");Assert.DoesNotContain(progress,item=>item.Stage=="delegation_waiting");Assert.Contains("Tool iteration limit reached",provider.Messages[1]);
    }

    [Fact]
    public async Task SpecializedRun_CanUseExplicitLargerIterationBudget()
    {
        var turns=Enumerable.Range(0,9).Select(_=>Tool("filesystem.list",new{path=".",recursive=false})).Append("Finished after the ninth tool call.").ToArray();
        var result=await CreateRunner(new FakeProvider(turns)).RunAsync(Request() with{RunId="long-child",IterationLimit=12},CancellationToken.None);
        Assert.Equal("completed",result.Status);Assert.Equal("Finished after the ninth tool call.",result.FinalContent);
    }

    [Fact]
    public async Task LeadCodeChange_IsRejectedAndRetriedAsCoderDelegation()
    {
        var target=Path.Combine(_projectPath,"clients.ts");
        var provider=new FakeProvider([
            Tool("filesystem.write",new{path="clients.ts",content="export const clients = [];"}),
            Tool("agent.delegate",new{agentRole="coder",objective="Create clients.ts with mock clients"}),
            "Coder created the client data and I reviewed the result."
        ]);
        var delegated=0;
        var result=await CreateRunner(provider).RunAsync(Request() with{RunId="lead-routing",Delegation=(call,_,_)=>{delegated++;Assert.Equal("coder",call.AgentRole);return Task.FromResult(new AgentDelegationOutcome("completed","child-run","Created clients.ts",null));}},CancellationToken.None);

        Assert.Equal("completed",result.Status);
        Assert.Equal(1,delegated);
        Assert.False(File.Exists(target));
        Assert.Contains("ALL code-changing work must be delegated",provider.Messages[0]);
        Assert.DoesNotContain("filesystem.write:",provider.Messages[0]);
        Assert.Contains("agent.delegate",provider.Messages[1]);
    }

    private ConversationRunner CreateRunner(IConversationProvider provider, ToolPermissionPolicy policy = ToolPermissionPolicy.Allow, IToolPolicyResolver? resolver = null)
    {
        var registry = new ToolRegistry();
        registry.Register(new ReadFileTool());
        registry.Register(new ListFilesTool());
        registry.Register(new SearchFilesTool());
        registry.Register(new WriteFileTool());
        registry.Register(new EditFileTool());
        registry.Register(new InsertFileTool());
        registry.Register(new PatchFileTool());
        return new(
            new ConversationProviderRegistry([provider]),
            registry,
            new ToolExecutor(registry, resolver ?? new FixedPolicyResolver(policy)),
            new ToolCallParser(),
            new ProviderToolProtocol(),
            NullLogger<ConversationRunner>.Instance
        );
    }

    private ConversationRunRequest Request() => new(
        new ProviderConversationSession("deepseek", "browser", "account-1", null),
        new ProjectInfo("project-1", "Test", _projectPath),
        "Read package.json and tell me the project name."
    );

    private static string Tool(string name, object arguments) =>
        $"<tool>\n{JsonSerializer.Serialize(new { name, arguments })}\n</tool>";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_projectPath)) Directory.Delete(_projectPath, recursive: true);
    }

    private sealed class FixedPolicyResolver(ToolPermissionPolicy policy) : IToolPolicyResolver
    {
        public ToolPolicyResolution Resolve(string toolId, string? projectId = null, string? agentId = null) => new(toolId, policy, "global");
    }

    private sealed class MutablePolicyResolver(ToolPermissionPolicy policy) : IToolPolicyResolver
    {
        public ToolPermissionPolicy Policy { get; set; } = policy;
        public ToolPolicyResolution Resolve(string toolId, string? projectId = null, string? agentId = null) => new(toolId, Policy, "global");
    }

    private sealed class FakeProvider(IReadOnlyList<string> responses) : IConversationProvider
    {
        private int _index;
        public string ProviderId => "deepseek";
        public string Transport => "browser";
        public List<string> Messages { get; } = [];
        public List<ProviderConversationSession> Sessions { get; } = [];

        public Task<ProviderConversationTurn> SendAsync(ProviderConversationSession session, string content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sessions.Add(session);
            Messages.Add(content);
            var response = responses[Math.Min(_index++, responses.Count - 1)];
            return Task.FromResult(new ProviderConversationTurn(response, "conversation-1"));
        }
    }

    private sealed class MissingConversationProvider : IConversationProvider
    {
        public string ProviderId => "deepseek";
        public string Transport => "browser";
        public Task<ProviderConversationTurn> SendAsync(ProviderConversationSession session, string content, CancellationToken cancellationToken) =>
            throw new RemoteConversationUnavailableException("The saved remote conversation no longer exists.");
    }
}
