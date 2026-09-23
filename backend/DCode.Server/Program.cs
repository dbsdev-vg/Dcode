using DCode.Server.Chats;
using DCode.Server.Agents;
using DCode.Server.Chats.AgentLoop;
using DCode.Server.Projects;
using DCode.Server.Providers;
using DCode.Server.Storage;
using DCode.Server.Tools;
using DCode.Server.Tools.BuiltIn;
using DCode.Server.Tools.Permissions;
using DCode.Server.Tools.Validation;
using DCode.Server.Runs;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("DCodeFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<DCodeDatabase>();
builder.Services.AddSingleton<ProjectRepository>();
builder.Services.AddSingleton<ProjectSessionRepository>();
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<BrowserProfileRepository>();
builder.Services.AddSingleton<BrowserSessionManager>();
builder.Services.AddSingleton<ChatRepository>();
builder.Services.AddSingleton<ProjectAgentRepository>();
builder.Services.AddSingleton<AgentRunRepository>();
builder.Services.AddSingleton<AgentSessionRepository>();
builder.Services.AddSingleton<AgentDelegationService>();
builder.Services.AddSingleton<PendingToolApprovalRepository>();
builder.Services.AddSingleton<DelegationContinuationService>();
builder.Services.AddSingleton<RunRepository>();
builder.Services.AddSingleton<RunExecutionService>();
builder.Services.AddSingleton<DeepSeekBrowserChatAdapter>();
builder.Services.AddSingleton<IConversationProvider, DeepSeekBrowserConversationProvider>();
builder.Services.AddSingleton<ConversationProviderRegistry>();
builder.Services.AddSingleton<ToolCallParser>();
builder.Services.AddSingleton<ProviderToolProtocol>();
builder.Services.AddSingleton<ConversationRunner>();
builder.Services.AddSingleton<ToolPolicyRepository>();
builder.Services.AddSingleton<IToolPolicyResolver, ToolPolicyResolver>();
builder.Services.AddSingleton<ISourceSyntaxValidator, TypeScriptSyntaxValidator>();
builder.Services.AddSingleton<SourceSyntaxValidationService>();
builder.Services.AddSingleton<ToolRegistry>(services =>
{
    var validation = services.GetRequiredService<SourceSyntaxValidationService>();
    var registry = new ToolRegistry();
    registry.Register(new ReadFileTool());
    registry.Register(new ListFilesTool());
    registry.Register(new SearchFilesTool());
    registry.Register(new WriteFileTool(validation));
    registry.Register(new EditFileTool(validation));
    registry.Register(new InsertFileTool(validation));
    registry.Register(new PatchFileTool(validation));
    registry.Register(new RunCommandTool());
    return registry;
});
builder.Services.AddSingleton<ToolExecutor>();
builder.Services.AddHostedService(services => services.GetRequiredService<BrowserSessionManager>());

var app = builder.Build();

var database = app.Services.GetRequiredService<DCodeDatabase>();
database.Initialize();

app.UseCors("DCodeFrontend");

app.MapGet("/api/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        app = "DCode",
        version = "0.1.0"
    });
});

app.MapPost(
    "/api/projects/open",
    (
        OpenProjectRequest request,
        ProjectService projectService
    ) =>
    {
        try
        {
            var project = projectService.CreateProject(request.Path);

            return Results.Ok(project);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Results.BadRequest(new
            {
                error = exception.Message
            });
        }
    }
);

app.MapGet(
    "/api/projects/{id}/tree",
    (
        string id,
        ProjectService projectService
    ) =>
    {
        try
        {
            var tree = projectService.GetTree(id);

            return Results.Ok(tree);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new
            {
                error = exception.Message
            });
        }
    }
);

app.MapGet(
    "/api/projects",
    (ProjectService projectService) =>
    {
        return Results.Ok(projectService.GetProjects());
    }
);

app.MapGet(
    "/api/projects/active",
    (ProjectService projectService) =>
    {
        return Results.Ok(projectService.GetActiveProject());
    }
);

app.MapGet(
    "/api/projects/{id}/session",
    (
        string id,
        ProjectService projectService,
        ProjectSessionRepository sessionRepository
    ) =>
    {
        try
        {
            projectService.GetProject(id);
            return Results.Ok(sessionRepository.Get(id));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }
);

app.MapPut(
    "/api/projects/{id}/session",
    (
        string id,
        SaveProjectSessionRequest request,
        ProjectService projectService,
        ProjectSessionRepository sessionRepository
    ) =>
    {
        try
        {
            projectService.GetProject(id);
            return Results.Ok(sessionRepository.Save(id, request));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }
);

app.MapGet(
    "/api/projects/{id}/file",
    async (
        string id,
        string path,
        ProjectService projectService
    ) =>
    {
        try
        {
            var file = await projectService.ReadFileAsync(id, path);

            return Results.Ok(file);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (FileNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
);

app.MapPut(
    "/api/projects/{id}/file",
    async (
        string id,
        WriteProjectFileRequest request,
        ProjectService projectService
    ) =>
    {
        try
        {
            var file = await projectService.WriteFileAsync(id, request);

            return Results.Ok(file);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (FileNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
);

app.MapGet("/api/providers/browser/deepseek/profiles",
    (BrowserProfileRepository profiles, BrowserSessionManager sessions) =>
        Results.Ok(profiles.GetDeepSeekProfiles().Select(sessions.ToResponse)));

app.MapPost("/api/providers/browser/deepseek/profiles",
    (CreateBrowserProfileRequest request, BrowserProfileRepository profiles, BrowserSessionManager sessions) =>
    {
        try
        {
            var profile = profiles.Create(request);
            return Results.Created($"/api/providers/browser/deepseek/profiles/{profile.Id}", sessions.ToResponse(profile));
        }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    });

app.MapPost("/api/providers/browser/deepseek/profiles/{id}/start",
    async (string id, BrowserProfileRepository profiles, BrowserSessionManager sessions) =>
    {
        try { return Results.Ok(await sessions.StartProfileAsync(profiles.Get(id))); }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
        catch (TimeoutException exception) { return Results.Conflict(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.Problem(exception.Message, statusCode: 500); }
    });

app.MapPost("/api/providers/browser/deepseek/profiles/{id}/stop",
    async (string id, BrowserProfileRepository profiles, BrowserSessionManager sessions) =>
    {
        try { return Results.Ok(await sessions.StopProfileAsync(profiles.Get(id))); }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    });

app.MapPatch("/api/providers/browser/deepseek/profiles/{id}",
    async (string id, UpdateBrowserProfileRequest request, BrowserProfileRepository profiles, BrowserSessionManager sessions) =>
    {
        try
        {
            var current = profiles.Get(id);
            await sessions.StopProfileAsync(current);
            return Results.Ok(sessions.ToResponse(profiles.UpdateExecutionMode(id, request.Headless)));
        }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    });

app.MapPost("/api/providers/browser/deepseek/profiles/{id}/warm",
    async (string id, BrowserWarmLeaseRequest request, BrowserProfileRepository profiles, BrowserSessionManager sessions, CancellationToken cancellationToken) =>
    {
        try { return Results.Ok(await sessions.AcquireWarmLeaseAsync(profiles.Get(id), request.LeaseId, cancellationToken)); }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
        catch (TimeoutException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status504GatewayTimeout); }
    });

app.MapPost("/api/providers/browser/deepseek/profiles/{id}/warm/release",
    (string id, BrowserWarmLeaseRequest request, BrowserProfileRepository profiles, BrowserSessionManager sessions) =>
    {
        try { return Results.Ok(sessions.ReleaseWarmLease(profiles.Get(id), request.LeaseId)); }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    });

app.MapPost("/api/providers/browser/deepseek/profiles/{id}/confirm-connection",
    async (string id, BrowserProfileRepository profiles, BrowserSessionManager sessions) =>
    {
        try { return Results.Ok(await sessions.ConfirmConnectionAsync(profiles.Get(id))); }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    });

app.MapDelete("/api/providers/browser/deepseek/profiles/{id}",
    async (string id, BrowserProfileRepository profiles, BrowserSessionManager sessions) =>
    {
        try
        {
            await sessions.DeleteProfileAsync(profiles.Get(id));
            return Results.NoContent();
        }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
        catch (IOException exception) { return Results.Conflict(new { error = $"Unable to delete browser data. {exception.Message}" }); }
        catch (UnauthorizedAccessException exception) { return Results.Conflict(new { error = $"Unable to delete browser data. {exception.Message}" }); }
        catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    });

app.MapPost("/api/providers/browser/deepseek/profiles/{id}/test",
    async (string id, BrowserProfileRepository profiles, BrowserSessionManager sessions) =>
    {
        try { return Results.Ok(await sessions.TestConnectionAsync(profiles.Get(id))); }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    });

app.MapGet("/api/chats", (ChatRepository chats) => Results.Ok(chats.GetAll()));

app.MapGet("/api/chats/{id}", (string id, ChatRepository chats) =>
{
    try { return Results.Ok(chats.Get(id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapGet("/api/tools", (ToolRegistry tools) => Results.Ok(tools.GetDefinitions()));

app.MapGet(
    "/api/settings/tool-policies",
    (ToolRegistry tools, IToolPolicyResolver policies) => Results.Ok(
        tools.GetDefinitions().Select(definition =>
        {
            var resolution = policies.Resolve(definition.Id);
            return new ToolPolicyResponse(
                definition.Id,
                ToolPermissionPolicyValues.Serialize(resolution.Policy),
                resolution.Scope
            );
        })
    )
);

app.MapPut(
    "/api/settings/tool-policies/{toolId}",
    (string toolId, UpdateToolPolicyRequest request, ToolRegistry tools, ToolPolicyRepository policies) =>
    {
        if (!tools.TryGet(toolId, out _)) return Results.NotFound(new { error = $"Unknown tool: {toolId}" });
        if (!ToolPermissionPolicyValues.TryParse(request.Policy, out var policy))
        {
            return Results.BadRequest(new { error = "Policy must be allow, ask, or deny." });
        }

        policies.SetGlobal(toolId, policy);
        return Results.Ok(new ToolPolicyResponse(toolId, ToolPermissionPolicyValues.Serialize(policy), "global"));
    }
);

app.MapPost(
    "/api/tools/execute",
    async (
        ExecuteToolRequest request,
        ProjectService projects,
        ToolExecutor executor,
        CancellationToken cancellationToken
    ) =>
    {
        ProjectInfo project;
        try
        {
            project = projects.GetProject(request.ProjectId);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(ToolResult.Fail("project_not_found", exception.Message));
        }

        var context = new ToolContext(
            project.Id,
            project.Path,
            project.Path,
            cancellationToken
        );
        return Results.Ok(await executor.ExecuteAsync(new ToolCall(request.ToolId, request.Arguments), context));
    }
);

app.MapPost("/api/chats", (CreateChatRequest request, ChatRepository chats, BrowserProfileRepository profiles) =>
{
    try
    {
        var chat = chats.Create(profiles.Get(request.BrowserProfileId));
        return Results.Created($"/api/chats/{chat.Id}", chat);
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

app.MapGet("/api/chats/{id}/messages", (string id, ChatRepository chats) =>
{
    try { return Results.Ok(chats.GetMessages(id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapGet("/api/chats/{id}/execution", (string id, ChatRepository chats) =>
{
    try { return Results.Json(chats.GetExecutionState(id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapGet("/api/chats/{id}/execution-turns", (string id, ChatRepository chats, AgentRunRepository runs) =>
{
    try { chats.Get(id); return Results.Ok(runs.ListForConversation(id).Select(run => new ProjectChatExecutionTurn(run.Id, id, run.UserMessageId ?? "", run.Status, run.Error, run.Events, run.CreatedUtc, run.UpdatedUtc))); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapGet("/api/projects/{projectId}/agent-runs", (string projectId, ProjectService projects, AgentRunRepository runs) =>
{
    try { projects.GetProject(projectId); return Results.Ok(runs.ListForProject(projectId)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapGet("/api/agent-runs/{runId}", (string runId, AgentRunRepository runs) =>
{
    try { return Results.Ok(runs.Get(runId)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapGet("/api/projects/{projectId}/agent-sessions", (string projectId, ProjectService projects, AgentSessionRepository sessions) =>
{
    try { projects.GetProject(projectId); return Results.Ok(sessions.ListForProject(projectId)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapDelete("/api/projects/{projectId}/agents/{agentId}/session", (string projectId,string agentId,ProjectService projects,AgentSessionRepository sessions) =>
{
    try { projects.GetProject(projectId); sessions.Reset(projectId,agentId); return Results.NoContent(); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error=exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error=exception.Message }); }
});

app.MapPost("/api/agent-runs/{parentRunId}/delegate", async (string parentRunId, DelegateAgentRequest request, AgentDelegationService delegation, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await delegation.DelegateAsync(parentRunId, request, cancellationToken)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

app.MapPost("/api/projects/{projectId}/chat/messages/stream", (
    string projectId,
    SendProjectChatMessageRequest request,
    ProjectService projects,
    ChatRepository chats,
    ProjectAgentRepository agents,
    AgentRunRepository agentRuns,
    AgentDelegationService delegation,
    PendingToolApprovalRepository pendingApprovals,
    ConversationRunner runner,
    CancellationToken requestCancellationToken) =>
{
    var content = request.Content.Trim();
    if (string.IsNullOrWhiteSpace(content)) return Results.BadRequest(new { error = "Message content is required." });

    try
    {
        var project = projects.GetProject(projectId);
        if (!string.Equals(projects.GetActiveProject()?.Id, project.Id, StringComparison.Ordinal))
            return Results.BadRequest(new { error = "The requested project is not the active project." });

        var chat = chats.Get(request.ChatId);
        chats.SetProjectChat(project.Id, chat.Id);
        return Results.Stream(async stream =>
        {
            var pendingExchange = chats.SaveUserMessage(chat.Id, content);
            var userMessageId = pendingExchange.Messages.Last(message => message.Role == "user").Id;
            var turnId = Guid.NewGuid().ToString("N");
            var owner = agents.GetConversationOwner(project.Id, chat.Id);
            var progressEvents = new List<ConversationProgress>();
            chats.SaveExecutionState(chat.Id, "running", null, progressEvents);
            agentRuns.Start(new(turnId, project.Id, owner.Id, chat.Id, "project_chat", content, chat.ProviderType, chat.Transport, chat.BrowserProfileId, owner.Model, userMessageId));
            await WriteProjectChatEventAsync(stream, new("exchange", Exchange: pendingExchange), requestCancellationToken);
            try
            {
                var run = await runner.RunAsync(
                    new ConversationRunRequest(
                        new ProviderConversationSession(chat.ProviderType, chat.Transport, chat.BrowserProfileId, chat.ProviderConversationUrl),
                        project,
                        content,
                        chat.Id,
                        reference => chats.BindRemoteConversation(chat.Id, reference),
                        async (progress, cancellationToken) =>
                        {
                            var existing = progressEvents.FindIndex(item => item.Id == progress.Id);
                            if (existing < 0) progressEvents.Add(progress);
                            else progressEvents[existing] = progress;
                            chats.SaveExecutionState(chat.Id, "running", null, progressEvents);
                            agentRuns.Update(turnId, "running", null, progressEvents);
                            await WriteProjectChatEventAsync(stream, new("progress", Progress: progress), cancellationToken);
                        },
                        RunId: turnId,
                        Delegation: async (call, childProgress, token) =>
                        {
                            try
                            {
                                var delegated = await delegation.DelegateAsync(turnId, new(call.AgentRole, call.Objective), token, childProgress);
                                return new(delegated.Run.Status, delegated.Run.Id, delegated.Result, delegated.Run.Error);
                            }
                            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
                            {
                                return new("failed", "unstarted", null, exception.Message);
                            }
                        }),
                    requestCancellationToken);

                ProjectChatRunResponse result;
                if (run.Status == "waiting_permission" && run.PendingToolCall is not null && !string.IsNullOrWhiteSpace(run.ConversationReference))
                    pendingApprovals.Save(new(turnId, run.PendingToolCall.ToolCallId, chat.Id, userMessageId, project.Id, run.PendingToolCall.Call.ToolId, run.PendingToolCall.Call.Arguments, run.PendingToolCall.RequiredPermission, run.ConversationReference, run.PendingToolCall.NextIteration, "pending", DateTimeOffset.UtcNow, run.PendingToolCall.EventId));
                if (run.Status == "completed" && !string.IsNullOrWhiteSpace(run.FinalContent) && !string.IsNullOrWhiteSpace(run.ConversationReference))
                {
                    var exchange = chats.SaveAssistantMessage(chat.Id, run.FinalContent, run.ConversationReference);
                    result = new("completed", null, exchange, run.Activities);
                }
                else result = new(run.Status, run.Error, pendingExchange, run.Activities);

                chats.SaveExecutionState(chat.Id, result.Status, result.Error, progressEvents);
                agentRuns.Update(turnId, result.Status, result.Error, progressEvents);
                await WriteProjectChatEventAsync(stream, new("result", Result: result), requestCancellationToken);
            }
            catch (OperationCanceledException) when (requestCancellationToken.IsCancellationRequested)
            {
                for (var index = 0; index < progressEvents.Count; index++)
                    if (progressEvents[index].Status == "running") progressEvents[index] = progressEvents[index] with { Status = "cancelled" };
                chats.SaveExecutionState(chat.Id, "cancelled", "Request cancelled. Completed steps were preserved.", progressEvents);
                agentRuns.Update(turnId, "cancelled", "Request cancelled. Completed steps were preserved.", progressEvents);
            }
            catch (Exception exception)
            {
                for (var index = 0; index < progressEvents.Count; index++)
                    if (progressEvents[index].Status == "running") progressEvents[index] = progressEvents[index] with { Status = "failed" };
                chats.SaveExecutionState(chat.Id, "provider_error", exception.Message, progressEvents);
                agentRuns.Update(turnId, "provider_error", exception.Message, progressEvents);
                var failure = new ProjectChatRunResponse("provider_error", exception.Message, pendingExchange, []);
                await WriteProjectChatEventAsync(stream, new("result", Result: failure), CancellationToken.None);
            }
        }, "application/x-ndjson");
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

app.MapGet("/api/chats/{chatId}/pending-approval", (string chatId, PendingToolApprovalRepository pending) =>
    pending.GetForChat(chatId) is { } value ? Results.Ok(value) : Results.NoContent());
app.MapGet("/api/chats/{chatId}/tool-approvals", (string chatId, PendingToolApprovalRepository pending) => Results.Ok(pending.ListForChat(chatId)));
app.MapGet("/api/projects/{projectId}/tool-approvals", (string projectId, ProjectService projects, PendingToolApprovalRepository pending) =>
{
    try { projects.GetProject(projectId); return Results.Ok(pending.ListForProject(projectId)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapPost("/api/project-chat/runs/{runId}/tool-calls/{toolCallId}/approve", (
    string runId, string toolCallId, ApproveToolCallRequest approval, PendingToolApprovalRepository pending,
    ChatRepository chats, ProjectService projects, ToolPolicyRepository policies, ConversationRunner runner, AgentRunRepository agentRuns,
    DelegationContinuationService continuation,
    CancellationToken cancellationToken) =>
{
    var saved = pending.Get(runId, toolCallId);
    if (saved is null) return Results.NotFound(new { error = "Pending tool approval was not found." });
    if (saved.Status != "pending" || !pending.TryResolve(runId, toolCallId, approval.AlwaysAllow ? "always_allowed" : "approved")) return Results.Conflict(new { error = "This pending tool call has already been resolved." });
    if (approval.AlwaysAllow) policies.SetGlobal(saved.ToolId, ToolPermissionPolicy.Allow);
    var chat = chats.Get(saved.ChatId); var project = projects.GetProject(saved.ProjectId);
    return Results.Stream(async stream =>
    {
        var persistedRun = agentRuns.Get(runId); var events = persistedRun.Events.ToList();
        var request = new ConversationRunRequest(
            new ProviderConversationSession(chat.ProviderType, chat.Transport, chat.BrowserProfileId, saved.ConversationReference), project, "resume", chat.Id,
            reference => chats.BindRemoteConversation(chat.Id, reference),
            async (progress, token) =>
            {
                var index = events.FindIndex(item => item.Id == progress.Id); if (index < 0) events.Add(progress); else events[index] = progress;
                chats.SaveExecutionState(chat.Id, "running", null, events); agentRuns.Update(runId, "running", null, events);
                await WriteProjectChatEventAsync(stream, new("progress", Progress: progress), token);
            },
            RunId: runId, IterationLimit: persistedRun.Trigger == "delegated" ? 20 : null);
        var run = await runner.ResumeApprovedAsync(request, new(saved.ToolCallId, new ToolCall(saved.ToolId, saved.Arguments), saved.NextIteration, saved.RequiredPermission), cancellationToken);
        ChatExchangeResponse? exchange = null;
        if (run.Status == "waiting_permission" && run.PendingToolCall is not null && !string.IsNullOrWhiteSpace(run.ConversationReference))
            pending.Save(new(runId, run.PendingToolCall.ToolCallId, chat.Id, saved.UserMessageId, project.Id, run.PendingToolCall.Call.ToolId, run.PendingToolCall.Call.Arguments, run.PendingToolCall.RequiredPermission, run.ConversationReference, run.PendingToolCall.NextIteration, "pending", DateTimeOffset.UtcNow, run.PendingToolCall.EventId));
        if (run.Status == "completed" && run.FinalContent is not null && run.ConversationReference is not null) exchange = chats.SaveAssistantMessage(chat.Id, run.FinalContent, run.ConversationReference);
        var result = new ProjectChatRunResponse(run.Status, run.Error, exchange ?? new(chat, chats.GetMessages(chat.Id)), run.Activities);
        chats.SaveExecutionState(chat.Id, run.Status, run.Error, events); agentRuns.Update(runId, run.Status, run.Error, events);
        await WriteProjectChatEventAsync(stream, new("result", Result: result), cancellationToken);
        if (run.Status is "completed" or "failed" or "cancelled" or "provider_error")
        {
            var parentResult = await continuation.ResumeParentAsync(runId, run.FinalContent, async (progress, token) => await WriteProjectChatEventAsync(stream, new("progress", Progress: progress), token), cancellationToken);
            if (parentResult is not null) await WriteProjectChatEventAsync(stream, new("result", Result: parentResult), cancellationToken);
        }
    }, "application/x-ndjson");
});

app.MapPost("/api/project-chat/runs/{runId}/tool-calls/{toolCallId}/deny", async (
    string runId, string toolCallId, PendingToolApprovalRepository pending, ChatRepository chats, AgentRunRepository agentRuns, DelegationContinuationService continuation, CancellationToken cancellationToken) =>
{
    var saved = pending.Get(runId, toolCallId);
    if (saved is null) return Results.NotFound(new { error = "Pending tool approval was not found." });
    if (saved.Status != "pending" || !pending.TryResolve(runId, toolCallId, "denied")) return Results.Conflict(new { error = "This pending tool call has already been resolved." });
    var events = agentRuns.Get(runId).Events.ToList();
    var progress = new ConversationProgress($"deny-{toolCallId}", "permission_denied", $"Denied {saved.ToolId}", "failed", Detail: JsonSerializer.Serialize(new { toolId = saved.ToolId, arguments = saved.Arguments }), Timestamp: DateTimeOffset.UtcNow);
    events.Add(progress); chats.SaveExecutionState(saved.ChatId, "failed", "Permission denied by user.", events); agentRuns.Update(runId, "failed", "Permission denied by user.", events);
    var parentResult=await continuation.ResumeParentAsync(runId,null,null,cancellationToken);
    return Results.Ok(new { progress, result = parentResult ?? new ProjectChatRunResponse("failed", "Permission denied by user.", new ChatExchangeResponse(chats.Get(saved.ChatId), chats.GetMessages(saved.ChatId)), Array.Empty<ToolActivity>()) });
});

app.MapPost("/api/agent-runs/{parentRunId}/resume-delegation", (string parentRunId, AgentRunRepository runs, ChatRepository chats, DelegationContinuationService continuation, CancellationToken cancellationToken) =>
{
    try
    {
        var parent=runs.Get(parentRunId);
        if(parent.Status!="waiting_delegation")return Results.Conflict(new{error="The Lead run is not waiting for a delegation."});
        var waiting=parent.Events.LastOrDefault(item=>item.Stage=="delegation_waiting");
        if(waiting?.Detail is null)return Results.Conflict(new{error="The waiting delegation has no persisted child reference."});
        var outcome=JsonSerializer.Deserialize<AgentDelegationOutcome>(waiting.Detail);
        if(outcome is null||string.IsNullOrWhiteSpace(outcome.ChildRunId))return Results.Conflict(new{error="The waiting delegation child reference is invalid."});
        var child=runs.Get(outcome.ChildRunId);
        if(child.Status is not ("completed" or "failed" or "cancelled" or "provider_error")||string.IsNullOrWhiteSpace(child.ConversationId))return Results.Conflict(new{error="The delegated Coder run has not reached a terminal state yet."});
        var childResult=chats.GetMessages(child.ConversationId).LastOrDefault(message=>message.Role=="assistant")?.Content;
        return Results.Stream(async stream=>
        {
            var result=await continuation.ResumeParentAsync(child.Id,childResult,async(progress,token)=>await WriteProjectChatEventAsync(stream,new("progress",Progress:progress),token),cancellationToken);
            if(result is not null)await WriteProjectChatEventAsync(stream,new("result",Result:result),cancellationToken);
        },"application/x-ndjson");
    }
    catch(KeyNotFoundException exception){return Results.NotFound(new{error=exception.Message});}
});

app.MapPost("/api/projects/{projectId}/chat/messages", async (
    string projectId,
    SendProjectChatMessageRequest request,
    ProjectService projects,
    ChatRepository chats,
    ConversationRunner runner,
    CancellationToken cancellationToken) =>
{
    var content = request.Content.Trim();
    if (string.IsNullOrWhiteSpace(content)) return Results.BadRequest(new { error = "Message content is required." });

    try
    {
        var project = projects.GetProject(projectId);
        if (!string.Equals(projects.GetActiveProject()?.Id, project.Id, StringComparison.Ordinal))
        {
            return Results.BadRequest(new ProjectChatRunResponse("no_active_project", "The requested project is not the active project.", null, []));
        }

        var chat = chats.Get(request.ChatId);
        chats.SetProjectChat(project.Id, chat.Id);
        var pendingExchange = chats.SaveUserMessage(chat.Id, content);
        var run = await runner.RunAsync(
            new ConversationRunRequest(
                new ProviderConversationSession(chat.ProviderType, chat.Transport, chat.BrowserProfileId, chat.ProviderConversationUrl),
                project,
                content,
                chat.Id,
                reference => chats.BindRemoteConversation(chat.Id, reference)),
            cancellationToken);

        if (run.Status != "completed" || string.IsNullOrWhiteSpace(run.FinalContent) || string.IsNullOrWhiteSpace(run.ConversationReference))
        {
            return Results.Ok(new ProjectChatRunResponse(run.Status, run.Error, pendingExchange, run.Activities));
        }

        var exchange = chats.SaveAssistantMessage(chat.Id, run.FinalContent, run.ConversationReference);
        return Results.Ok(new ProjectChatRunResponse("completed", null, exchange, run.Activities));
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (TimeoutException exception) { return Results.Problem(exception.Message, statusCode: 504); }
    catch (Microsoft.Playwright.PlaywrightException exception) { return Results.Problem($"Provider browser automation failed. {exception.Message}", statusCode: 502); }
});

app.MapGet("/api/projects/{projectId}/chat", (string projectId, ProjectService projects, ChatRepository chats) =>
{
    try
    {
        projects.GetProject(projectId);
        return Results.Json(chats.GetProjectChat(projectId));
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapGet("/api/projects/{projectId}/chats", (string projectId, ProjectService projects, ChatRepository chats, ProjectAgentRepository agents) =>
{
    try { projects.GetProject(projectId); agents.EnsureDefaults(projectId); return Results.Ok(chats.GetProjectChats(projectId)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapPost("/api/projects/{projectId}/chats", (string projectId, CreateChatRequest request, ProjectService projects, ChatRepository chats, BrowserProfileRepository profiles, ProjectAgentRepository agents) =>
{
    try { projects.GetProject(projectId); agents.EnsureDefaults(projectId); return Results.Created($"/api/projects/{projectId}/chats", chats.CreateProjectChat(projectId, profiles.Get(request.BrowserProfileId))); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

app.MapGet("/api/provider-accounts", (BrowserProfileRepository profiles) =>
    Results.Ok(profiles.GetDeepSeekProfiles().Select(profile => new ProviderAccountSummary(profile.Id, profile.ProviderType, "browser", profile.Name, profile.Connected))));

app.MapGet("/api/projects/{projectId}/agents", (string projectId, ProjectService projects, ProjectAgentRepository agents) =>
{
    try { projects.GetProject(projectId); return Results.Ok(agents.GetForProject(projectId)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapGet("/api/projects/{projectId}/run-configurations", (string projectId, RunExecutionService runs) =>
{
    try{return Results.Ok(runs.ListConfigurations(projectId));}catch(KeyNotFoundException exception){return Results.NotFound(new{error=exception.Message});}
});
app.MapPost("/api/projects/{projectId}/run-configurations", (string projectId, SaveRunConfigurationRequest request, RunExecutionService runs) =>
{
    try{var saved=runs.Save(projectId,null,request);return Results.Created($"/api/projects/{projectId}/run-configurations/{saved.Id}",saved);}catch(KeyNotFoundException exception){return Results.NotFound(new{error=exception.Message});}catch(Exception exception)when(exception is ArgumentException or UnauthorizedAccessException or DirectoryNotFoundException){return Results.BadRequest(new{error=exception.Message});}
});
app.MapPut("/api/projects/{projectId}/run-configurations/{configurationId}", (string projectId,string configurationId,SaveRunConfigurationRequest request,RunExecutionService runs) =>
{
    try{return Results.Ok(runs.Save(projectId,configurationId,request));}catch(KeyNotFoundException exception){return Results.NotFound(new{error=exception.Message});}catch(Exception exception)when(exception is ArgumentException or UnauthorizedAccessException or DirectoryNotFoundException){return Results.BadRequest(new{error=exception.Message});}
});
app.MapDelete("/api/projects/{projectId}/run-configurations/{configurationId}", (string projectId,string configurationId,RunExecutionService runs) =>
{
    try{runs.Delete(projectId,configurationId);return Results.NoContent();}catch(KeyNotFoundException exception){return Results.NotFound(new{error=exception.Message});}catch(InvalidOperationException exception){return Results.Conflict(new{error=exception.Message});}
});
app.MapPost("/api/projects/{projectId}/run-configurations/{configurationId}/start", (string projectId,string configurationId,RunExecutionService runs) =>
{
    try{return Results.Accepted($"/api/projects/{projectId}/run-executions",runs.Start(projectId,configurationId));}catch(KeyNotFoundException exception){return Results.NotFound(new{error=exception.Message});}catch(Exception exception)when(exception is InvalidOperationException or UnauthorizedAccessException or DirectoryNotFoundException){return Results.Conflict(new{error=exception.Message});}
});
app.MapGet("/api/projects/{projectId}/run-executions", (string projectId,RunExecutionService runs) =>
{
    try{return Results.Ok(runs.ListExecutions(projectId));}catch(KeyNotFoundException exception){return Results.NotFound(new{error=exception.Message});}
});
app.MapPost("/api/projects/{projectId}/run-executions/{executionId}/stop", (string projectId,string executionId,RunExecutionService runs) =>
{
    try{return Results.Accepted($"/api/projects/{projectId}/run-executions",runs.Stop(projectId,executionId));}catch(KeyNotFoundException exception){return Results.NotFound(new{error=exception.Message});}
});

app.MapPost("/api/projects/{projectId}/agents", (string projectId, CreateProjectAgentRequest request, ProjectService projects, ProjectAgentRepository agents) =>
{
    try { projects.GetProject(projectId); return Results.Created($"/api/projects/{projectId}/agents", agents.Create(projectId, request)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

app.MapPut("/api/projects/{projectId}/agents/{agentId}", (string projectId, string agentId, UpdateProjectAgentRequest request, ProjectService projects, ProjectAgentRepository agents) =>
{
    try { projects.GetProject(projectId); return Results.Ok(agents.Update(projectId, agentId, request)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

app.MapPut("/api/projects/{projectId}/chats/{chatId}/active", (string projectId, string chatId, ProjectService projects, ChatRepository chats) =>
{
    try { projects.GetProject(projectId); chats.SetProjectChat(projectId, chatId); return Results.Ok(chats.Get(chatId)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

app.MapPatch("/api/projects/{projectId}/chats/{chatId}", async (string projectId, string chatId, UpdateChatRequest request, ProjectService projects, ChatRepository chats, ConversationProviderRegistry providers, CancellationToken cancellationToken) =>
{
    try
    {
        projects.GetProject(projectId);
        var title = ChatRepository.NormalizeTitle(request.Title);
        var chat = chats.Get(chatId);
        if (!string.IsNullOrWhiteSpace(chat.ProviderConversationUrl))
            await providers.Get(chat.ProviderType, chat.Transport).RenameAsync(
                new(chat.ProviderType, chat.Transport, chat.BrowserProfileId, chat.ProviderConversationUrl), title, cancellationToken);
        return Results.Ok(chats.RenameProjectChat(projectId, chatId, title));
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (RemoteConversationUnavailableException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or Microsoft.Playwright.PlaywrightException) { return Results.Problem(exception.Message, statusCode: 502); }
});

app.MapPost("/api/projects/{projectId}/chats/{chatId}/archive", (string projectId, string chatId, ProjectService projects, ChatRepository chats) =>
{
    try { projects.GetProject(projectId); chats.ArchiveProjectChat(projectId, chatId); return Results.NoContent(); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapDelete("/api/projects/{projectId}/chats/{chatId}", (string projectId, string chatId, ProjectService projects, ChatRepository chats) =>
{
    try { projects.GetProject(projectId); chats.DeleteProjectChat(projectId, chatId); return Results.NoContent(); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
});

app.MapPost("/api/projects/{projectId}/chats/{chatId}/open", async (string projectId, string chatId, ProjectService projects, ChatRepository chats, ConversationProviderRegistry providers, CancellationToken cancellationToken) =>
{
    try
    {
        projects.GetProject(projectId); var chat = chats.Get(chatId);
        await providers.Get(chat.ProviderType, chat.Transport).OpenAsync(
            new(chat.ProviderType, chat.Transport, chat.BrowserProfileId, chat.ProviderConversationUrl), cancellationToken);
        return Results.NoContent();
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (RemoteConversationUnavailableException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or Microsoft.Playwright.PlaywrightException) { return Results.Problem(exception.Message, statusCode: 502); }
});

app.MapPost("/api/chats/{id}/messages", async (
    string id,
    SendChatMessageRequest request,
    ChatRepository chats,
    BrowserProfileRepository profiles,
    DeepSeekBrowserChatAdapter deepSeek,
    CancellationToken cancellationToken) =>
{
    var content = request.Content.Trim();
    if (string.IsNullOrWhiteSpace(content)) return Results.BadRequest(new { error = "Message content is required." });
    try
    {
        var chat = chats.Get(id);
        var profile = profiles.Get(chat.BrowserProfileId);
        var result = await deepSeek.SendAsync(profile, chat.ProviderConversationUrl, content, cancellationToken);
        return Results.Ok(chats.SaveExchange(id, content, result.Content, result.ConversationUrl));
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (TimeoutException exception) { return Results.Problem(exception.Message, statusCode: 504); }
    catch (Microsoft.Playwright.PlaywrightException exception) { return Results.Problem($"DeepSeek browser automation failed. {exception.Message}", statusCode: 502); }
});

app.Run();

static async ValueTask WriteProjectChatEventAsync(
    Stream stream,
    ProjectChatStreamEvent chatEvent,
    CancellationToken cancellationToken)
{
    await JsonSerializer.SerializeAsync(stream, chatEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
    await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
    await stream.FlushAsync(cancellationToken);
}

public sealed record ApproveToolCallRequest(bool AlwaysAllow = false);
