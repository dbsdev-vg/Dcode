using System.Diagnostics;
using System.Text.Json;
using DCode.Server.Projects;
using DCode.Server.Tools;

namespace DCode.Server.Chats.AgentLoop;

public sealed record AgentDelegationCall(string AgentRole, string Objective);
public sealed record AgentDelegationOutcome(string Status, string ChildRunId, string? Result, string? Error);
public sealed record ConversationRunRequest(ProviderConversationSession ProviderSession, ProjectInfo? Project, string UserMessage, string? LocalConversationId = null, Action<string>? ConversationReferenceChanged = null, Func<ConversationProgress, CancellationToken, ValueTask>? Progress = null, string? ContinuationContent = null, int StartIteration = 0, string? RunId = null, int RecoverableFailureCount = 0, Func<AgentDelegationCall, Func<ConversationProgress, CancellationToken, ValueTask>, CancellationToken, Task<AgentDelegationOutcome>>? Delegation = null, int? IterationLimit = null, AgentDelegationOutcome? FinalizingDelegation = null, int DelegationAttemptCount = 0);
public sealed record ConversationProgress(string Id, string Stage, string Label, string Status, long? DurationMilliseconds = null, string? Detail = null, DateTimeOffset? Timestamp = null);
public sealed record PendingConversationToolCall(string ToolCallId, ToolCall Call, int NextIteration, string RequiredPermission, string? EventId = null);
public sealed record ConversationRunResult(string Status, string? FinalContent, string? Error, string? ConversationReference, IReadOnlyList<ToolActivity> Activities, PendingConversationToolCall? PendingToolCall = null);

public sealed class ConversationRunner(ConversationProviderRegistry providers, ToolRegistry tools, ToolExecutor executor, ToolCallParser parser, ProviderToolProtocol protocol, ILogger<ConversationRunner> logger)
{
    public const int MaximumIterations = 8;
    public const int MaximumRecoveryAttempts = 3;
    public const int MaximumRecoverableToolFailures = 3;
    public const int MaximumDelegationAttempts = 2;
    public const int MaximumFinalizationRecoveryAttempts = 1;
    private static readonly HashSet<string> EnabledToolIds = new(StringComparer.Ordinal) { "filesystem.read", "filesystem.list", "filesystem.search", "filesystem.write", "filesystem.edit", "filesystem.insert", "filesystem.patch", "process.run" };
    private static readonly HashSet<string> LeadToolIds = new(StringComparer.Ordinal) { "filesystem.read", "filesystem.list", "filesystem.search" };

    public async Task<ConversationRunResult> RunAsync(ConversationRunRequest request, CancellationToken cancellationToken)
    {
        if (request.Project is null) return Failed("no_active_project", "An active project is required for project chat tools.", null, []);
        if (string.IsNullOrWhiteSpace(request.UserMessage)) return Failed("invalid_message", "Message content is required.", request.ProviderSession.ConversationReference, []);
        IConversationProvider provider;
        try { provider = providers.Get(request.ProviderSession.ProviderId, request.ProviderSession.Transport); }
        catch (KeyNotFoundException exception) { return Failed("provider_unavailable", exception.Message, request.ProviderSession.ConversationReference, []); }

        var definitions = tools.GetDefinitions().Where(definition => (request.Delegation is null ? EnabledToolIds : LeadToolIds).Contains(definition.Id)).ToList();
        if (request.Delegation is not null) definitions.Add(ToolDefinition.Create("agent.delegate", "Delegate one bounded implementation or project-verification objective to a specialized project Agent and wait for its result.", new { type = "object", properties = new { agentRole = new { type = "string" }, objective = new { type = "string" } }, required = new[] { "agentRole", "objective" } }));
        var runId = request.RunId ?? Guid.NewGuid().ToString("N");
        request = request with { RunId = runId };
        var session = request.ProviderSession;
        var nextContent = request.ContinuationContent ?? protocol.FormatUserMessage(request.UserMessage.Trim(), definitions);
        var activities = new List<ToolActivity>();
        var recoveryAttempts = 0;
        var recoveryInFlight = false;
        var recoverableFailureCount = request.RecoverableFailureCount;
        var toolRecoveryInFlight = recoverableFailureCount > 0;
        var finalizingDelegation = request.FinalizingDelegation;
        var finalizationRecoveryAttempts = 0;
        var delegationAttemptCount = request.DelegationAttemptCount;
        var completedToolResults = new Dictionary<string, ToolResult>(StringComparer.Ordinal);

        var iterationLimit = Math.Clamp(request.IterationLimit ?? MaximumIterations, MaximumIterations, 24);
        for (var iteration = request.StartIteration; iteration < iterationLimit; iteration++)
        {
            var providerId = EventId(runId, "provider", iteration);
            var providerLabel = iteration == 0 ? "Contacting provider" : "Continuing with tool result";
            await ReportAsync(request, new(providerId, "provider_started", providerLabel, "running"), cancellationToken);
            var timer = Stopwatch.StartNew();
            ProviderConversationTurn turn;
            try { turn = await provider.SendAsync(session, nextContent, cancellationToken); }
            catch (RemoteConversationUnavailableException exception)
            {
                timer.Stop(); await ReportAsync(request, new(providerId, "error", exception.Message, "failed", timer.ElapsedMilliseconds), cancellationToken);
                return Failed("remote_conversation_unavailable", exception.Message, session.ConversationReference, activities);
            }
            timer.Stop();
            await ReportAsync(request, new(EventId(runId, "provider-response", iteration), "provider_response", providerLabel, "completed", timer.ElapsedMilliseconds), cancellationToken);
            if (string.IsNullOrWhiteSpace(turn.ConversationReference)) return Failed("remote_conversation_unavailable", "The provider did not return a remote conversation reference.", session.ConversationReference, activities);
            if (!string.IsNullOrWhiteSpace(session.ConversationReference) && !string.Equals(session.ConversationReference, turn.ConversationReference, StringComparison.Ordinal)) return Failed("remote_conversation_unavailable", "The provider did not restore the bound remote conversation.", session.ConversationReference, activities);
            if (!string.Equals(session.ConversationReference, turn.ConversationReference, StringComparison.Ordinal))
            {
                request.ConversationReferenceChanged?.Invoke(turn.ConversationReference);
                logger.LogInformation("Conversation binding {LocalConversationId} -> {RemoteConversationId}", request.LocalConversationId ?? "untracked", turn.ConversationReference);
            }
            session = session with { ConversationReference = turn.ConversationReference };

            var parsed = parser.Parse(turn.Content);
            if (!parsed.Success)
            {
                if (finalizingDelegation?.Status == "completed")
                {
                    if (finalizationRecoveryAttempts < MaximumFinalizationRecoveryAttempts)
                    {
                        finalizationRecoveryAttempts++;
                        nextContent = protocol.FormatDelegationFinalRecovery(finalizingDelegation, parsed.ErrorCode, parsed.Error);
                        await ReportAsync(request, new(EventId(runId, "provider-retry-started", iteration), "provider_retry_started", "Finalizing verified delegated work…", "running", Detail: JsonSerializer.Serialize(new { attempt = finalizationRecoveryAttempts, code = parsed.ErrorCode })), cancellationToken);
                        continue;
                    }

                    return await CompleteFromDelegationAsync(request, runId, iteration, finalizingDelegation, session.ConversationReference, activities, cancellationToken);
                }
                if (recoveryInFlight)
                    await ReportAsync(request, new(EventId(runId, "provider-retry-failed", iteration), "provider_retry_failed", "Provider retry was still invalid", "failed", Detail: JsonSerializer.Serialize(new { code = parsed.ErrorCode, reason = parsed.Error })), cancellationToken);
                await ReportAsync(request, new(EventId(runId, "tool-call-rejected", iteration), "tool_call_rejected", "Rejected invalid tool request", "failed", Detail: JsonSerializer.Serialize(new { code = parsed.ErrorCode, reason = parsed.Error })), cancellationToken);
                if (recoveryAttempts >= (request.Delegation is null ? MaximumRecoveryAttempts : 1))
                {
                    if (request.Delegation is not null && delegationAttemptCount < MaximumDelegationAttempts)
                    {
                        delegationAttemptCount++;
                        var fallback = new AgentDelegationCall("coder", request.UserMessage.Trim());
                        await ReportAsync(request, new(EventId(runId, "delegation-started", iteration), "delegation_started", "Recovering the request with Coder", "running", Detail: JsonSerializer.Serialize(new { reason = parsed.Error, originalObjective = fallback.Objective })), cancellationToken);
                        var outcome = await request.Delegation(fallback, async (childProgress, token) => await ReportAsync(request, childProgress, token), cancellationToken);
                        var delegationStage = outcome.Status == "completed" ? "delegation_completed" : outcome.Status == "waiting_permission" ? "delegation_waiting" : "delegation_failed";
                        var delegationStatus = outcome.Status == "completed" ? "completed" : outcome.Status == "waiting_permission" ? "waiting" : "failed";
                        await ReportAsync(request, new(EventId(runId, "delegation-result", iteration), delegationStage, outcome.Status == "completed" ? "Coder completed recovered work" : outcome.Status == "waiting_permission" ? "Coder is waiting for approval" : "Coder could not complete recovered work", delegationStatus, Detail: JsonSerializer.Serialize(outcome)), cancellationToken);
                        if (outcome.Status == "waiting_permission") return new("waiting_delegation", null, "The delegated Agent is waiting for permission.", session.ConversationReference, activities);
                        nextContent = protocol.FormatDelegationResult(outcome);
                        if (outcome.Status == "completed") finalizingDelegation = outcome;
                        recoveryAttempts = 0;
                        recoveryInFlight = false;
                        continue;
                    }
                    await ReportAsync(request, new(EventId(runId, "error", iteration), "error", "Tool request recovery limit reached", "failed", Detail: parsed.Error), cancellationToken);
                    return Failed(parsed.ErrorCode ?? "tool_call_malformed", parsed.Error!, session.ConversationReference, activities);
                }
                recoveryAttempts++;
                recoveryInFlight = true;
                nextContent = protocol.FormatToolRecovery(parsed.ErrorCode ?? "tool_call_malformed", parsed.Error!, definitions, parsed.ToolIdHint);
                await ReportAsync(request, new(EventId(runId, "provider-retry-started", iteration), "provider_retry_started", "Retrying malformed tool request…", "running", Detail: JsonSerializer.Serialize(new { attempt = recoveryAttempts, maximumAttempts = MaximumRecoveryAttempts, code = parsed.ErrorCode })), cancellationToken);
                continue;
            }

            var providerTool = parsed.ToolCall;
            if (providerTool is not null && finalizingDelegation?.Status == "completed")
            {
                await ReportAsync(request, new(EventId(runId, "tool-call-rejected", iteration), "tool_call_rejected", "Ignored tool request after verified delegated work", "failed", Detail: JsonSerializer.Serialize(new { code = "post_delegation_tool_rejected", toolId = providerTool.Call.ToolId })), cancellationToken);
                if (finalizationRecoveryAttempts < MaximumFinalizationRecoveryAttempts)
                {
                    finalizationRecoveryAttempts++;
                    nextContent = protocol.FormatDelegationFinalRecovery(finalizingDelegation, "post_delegation_tool_rejected", "The delegated work is already complete. No further tool call is allowed while finalizing.");
                    await ReportAsync(request, new(EventId(runId, "provider-retry-started", iteration), "provider_retry_started", "Finalizing verified delegated work…", "running"), cancellationToken);
                    continue;
                }

                return await CompleteFromDelegationAsync(request, runId, iteration, finalizingDelegation, session.ConversationReference, activities, cancellationToken);
            }
            if (providerTool is not null && request.Delegation is not null && IsEditingTool(providerTool.Call.ToolId))
            {
                const string errorCode = "tool_requires_delegation";
                var reason = $"Lead cannot execute '{providerTool.Call.ToolId}' directly. Code-changing work must be delegated to Coder.";
                await ReportAsync(request, new(EventId(runId, "tool-call-rejected", iteration), "tool_call_rejected", "Routing code change to Coder", "failed", Detail: JsonSerializer.Serialize(new { code = errorCode, reason, toolId = providerTool.Call.ToolId })), cancellationToken);
                if (recoveryAttempts >= MaximumRecoveryAttempts)
                    return Failed(errorCode, reason, session.ConversationReference, activities);
                recoveryAttempts++;
                recoveryInFlight = true;
                nextContent = protocol.FormatDelegationRecovery(reason);
                await ReportAsync(request, new(EventId(runId, "provider-retry-started", iteration), "provider_retry_started", "Asking Lead to delegate the code change", "running", Detail: JsonSerializer.Serialize(new { attempt = recoveryAttempts, maximumAttempts = MaximumRecoveryAttempts, code = errorCode })), cancellationToken);
                continue;
            }
            if (providerTool is not null && providerTool.Call.ToolId != "agent.delegate" && !tools.TryGet(providerTool.Call.ToolId, out _))
            {
                const string errorCode = "tool_call_unknown";
                var reason = $"Tool '{providerTool.Call.ToolId}' is not registered.";
                if (recoveryInFlight)
                    await ReportAsync(request, new(EventId(runId, "provider-retry-failed", iteration), "provider_retry_failed", "Provider retry used an unknown tool", "failed", Detail: JsonSerializer.Serialize(new { code = errorCode, reason })), cancellationToken);
                await ReportAsync(request, new(EventId(runId, "tool-call-rejected", iteration), "tool_call_rejected", $"Rejected unknown tool: {providerTool.Call.ToolId}", "failed", Detail: JsonSerializer.Serialize(new { code = errorCode, reason, toolId = providerTool.Call.ToolId })), cancellationToken);
                if (recoveryAttempts >= MaximumRecoveryAttempts)
                {
                    await ReportAsync(request, new(EventId(runId, "error", iteration), "error", "Tool request recovery limit reached", "failed", Detail: reason), cancellationToken);
                    return Failed(errorCode, reason, session.ConversationReference, activities);
                }
                recoveryAttempts++;
                recoveryInFlight = true;
                nextContent = protocol.FormatToolRecovery(errorCode, reason, definitions, providerTool.Call.ToolId);
                await ReportAsync(request, new(EventId(runId, "provider-retry-started", iteration), "provider_retry_started", "Retrying with an available tool…", "running", Detail: JsonSerializer.Serialize(new { attempt = recoveryAttempts, maximumAttempts = MaximumRecoveryAttempts, code = errorCode })), cancellationToken);
                continue;
            }
            if (recoveryInFlight)
            {
                await ReportAsync(request, new(EventId(runId, "provider-retry-completed", iteration), "provider_retry_completed", "Provider recovered", "completed", Detail: JsonSerializer.Serialize(new { attempts = recoveryAttempts })), cancellationToken);
                recoveryInFlight = false;
            }
            if (providerTool is null)
            {
                var final = string.Join("\n\n", parsed.Text.Select(text => text.Content)).Trim();
                if (string.IsNullOrWhiteSpace(final)) return Failed("empty_provider_response", "The provider returned an empty response.", session.ConversationReference, activities);
                await ReportAsync(request, new(EventId(runId, "final", iteration), "final_response", "Response completed", "completed", Detail: JsonSerializer.Serialize(new { content = final })), cancellationToken);
                return new("completed", final, null, session.ConversationReference, activities);
            }

            var segmentIndex = 0;
            foreach (var segment in parsed.Segments)
            {
                if (segment is ProviderText text)
                    await ReportAsync(request, new(EventId(runId, "text", iteration, segmentIndex++), "provider_text", text.Content, "completed", Detail: JsonSerializer.Serialize(new { content = text.Content })), cancellationToken);
                else if (segment is ProviderToolCall requestedTool)
                {
                    var requestedLabel = ToolActivityFormatter.Format(requestedTool.Call);
                    var requestedDetail = JsonSerializer.Serialize(new { name = requestedTool.Call.ToolId, arguments = JsonSerializer.Deserialize<JsonElement>(requestedTool.Call.Arguments.GetRawText()) });
                    await ReportAsync(request, new(EventId(runId, "request", iteration), "tool_requested", requestedLabel, "completed", Detail: requestedDetail), cancellationToken);
                }
            }

            var toolCall = providerTool.Call;
            var toolLabel = ToolActivityFormatter.Format(toolCall);
            var toolDetail = JsonSerializer.Serialize(new { name = toolCall.ToolId, arguments = JsonSerializer.Deserialize<JsonElement>(toolCall.Arguments.GetRawText()) });
            if (tools.TryGet(toolCall.ToolId, out _) && !EnabledToolIds.Contains(toolCall.ToolId)) return Failed("tool_not_enabled", $"Tool '{toolCall.ToolId}' is not enabled in project chat.", session.ConversationReference, activities);

            if (toolCall.ToolId == "agent.delegate")
            {
                if (request.Delegation is null) return Failed("delegation_not_available", "This Agent run is not allowed to delegate work.", session.ConversationReference, activities);
                if (!TryParseDelegation(toolCall.Arguments, out var delegation, out var delegationError))
                {
                    nextContent = protocol.FormatDelegationResult(new("failed", "unstarted", null, delegationError));
                    await ReportAsync(request, new(EventId(runId,"delegation-failed",iteration),"delegation_failed",delegationError!,"failed",Detail:toolDetail),cancellationToken);
                    continue;
                }
                if (delegationAttemptCount >= MaximumDelegationAttempts)
                {
                    nextContent = protocol.FormatDelegationLimitReached(delegationError ?? "The maximum number of delegated attempts was reached.");
                    finalizingDelegation = new("failed", "limit", null, "The maximum number of delegated attempts was reached.");
                    continue;
                }
                delegationAttemptCount++;
                await ReportAsync(request,new(EventId(runId,"delegation-started",iteration),"delegation_started",$"Delegating to {delegation!.AgentRole}","running",Detail:toolDetail),cancellationToken);
                var outcome=await request.Delegation(delegation,async(childProgress,token)=>await ReportAsync(request,childProgress,token),cancellationToken);
                var delegationStage=outcome.Status=="completed"?"delegation_completed":outcome.Status=="waiting_permission"?"delegation_waiting":"delegation_failed";
                var delegationStatus=outcome.Status=="completed"?"completed":outcome.Status=="waiting_permission"?"waiting":"failed";
                await ReportAsync(request,new(EventId(runId,"delegation-result",iteration),delegationStage,outcome.Status=="completed"?$"{delegation.AgentRole} completed delegated work":outcome.Status=="waiting_permission"?$"{delegation.AgentRole} is waiting for approval":$"{delegation.AgentRole} could not complete delegated work",delegationStatus,Detail:JsonSerializer.Serialize(outcome)),cancellationToken);
                if(outcome.Status=="waiting_permission") return new("waiting_delegation",null,"The delegated Agent is waiting for permission.",session.ConversationReference,activities);
                nextContent=protocol.FormatDelegationResult(outcome);
                if (outcome.Status == "completed") finalizingDelegation = outcome;
                continue;
            }

            var context = new ToolContext(request.Project.Id, request.Project.Path, request.Project.Path, cancellationToken);
            var toolCallId = EventId(runId, "call", iteration);
            var toolSignature = $"{toolCall.ToolId}:{toolCall.Arguments.GetRawText()}";
            if (IsInspectionTool(toolCall.ToolId) && completedToolResults.TryGetValue(toolSignature, out var previousResult))
            {
                await ReportAsync(request, new(EventId(runId, "tool-reused", iteration), "tool_reused", $"Reused previous {toolLabel.ToLowerInvariant()}", "completed", Detail: toolDetail), cancellationToken);
                nextContent = protocol.FormatToolResult(toolCall, previousResult);
                continue;
            }
            await ReportAsync(request, new(EventId(runId, "tool-started", iteration), "tool_started", toolLabel, "running", Detail: toolDetail), cancellationToken);
            var result = await executor.ExecuteAsync(toolCall, context);
            var resultDetail = JsonSerializer.Serialize(new { toolId = toolCall.ToolId, arguments = JsonSerializer.Deserialize<JsonElement>(toolCall.Arguments.GetRawText()), result = new { result.Success, result.Output, result.Error, result.Metadata } });
            activities.Add(new(toolCall.ToolId, toolLabel, result.Success));

            if (!result.Success)
            {
                if (result.Error?.Code == "approval_required")
                {
                    var permissionEventId = EventId(runId, "permission-required", iteration);
                    await ReportAsync(request, new(permissionEventId, "permission_required", $"Approval required: {toolLabel}", "waiting", result.DurationMilliseconds, resultDetail), cancellationToken);
                    var permission = tools.TryGet(toolCall.ToolId, out var pendingTool) ? string.Join(",", pendingTool!.RequiredPermissions.Select(item => item.Id)) : "unknown";
                    return new("waiting_permission", null, result.Error.Message, session.ConversationReference, activities, new(toolCallId, toolCall, iteration + 1, permission, permissionEventId));
                }
                if (result.Error?.Code == "permission_denied")
                {
                    await ReportAsync(request, new(EventId(runId, "permission-denied", iteration), "permission_denied", $"Permission denied: {toolLabel}", "failed", result.DurationMilliseconds, resultDetail), cancellationToken);
                    return Failed("failed", result.Error.Message, session.ConversationReference, activities);
                }
                if (IsRecoverableToolError(result.Error?.Code))
                {
                    await ReportAsync(request, new(EventId(runId, "tool-failed", iteration), "tool_failed", $"{toolLabel}: {result.Error!.Message}", "failed", result.DurationMilliseconds, resultDetail), cancellationToken);
                    if (recoverableFailureCount >= MaximumRecoverableToolFailures)
                    {
                        await ReportAsync(request, new(EventId(runId, "recovery-exhausted", iteration), "recovery_exhausted", "Automatic edit recovery exhausted", "failed", Detail: resultDetail), cancellationToken);
                        return Failed("recovery_exhausted", result.Error.Message, session.ConversationReference, activities);
                    }
                    recoverableFailureCount++;
                    toolRecoveryInFlight = true;
                    await ReportAsync(request, new(EventId(runId, "recovery-started", iteration), "recovery_started", result.Error.Code == "syntax_validation_failed" ? "Edit failed validation; recovering…" : "Recovering failed edit…", "running", Detail: JsonSerializer.Serialize(new { attempt = recoverableFailureCount, maximumAttempts = MaximumRecoverableToolFailures, error = result.Error })), cancellationToken);
                    nextContent = protocol.FormatRecoverableToolFailure(toolCall, result);
                    continue;
                }
                await ReportAsync(request, new(EventId(runId, "tool-failed", iteration), "tool_failed", toolLabel, "failed", result.DurationMilliseconds, resultDetail), cancellationToken);
                return Failed("failed", result.Error?.Message ?? "Tool execution failed.", session.ConversationReference, activities);
            }

            await ReportAsync(request, new(EventId(runId, "tool-completed", iteration), "tool_completed", toolLabel, "completed", result.DurationMilliseconds, resultDetail), cancellationToken);
            if (IsInspectionTool(toolCall.ToolId)) completedToolResults[toolSignature] = result;
            if (toolRecoveryInFlight && (IsEditingTool(toolCall.ToolId) || toolCall.ToolId == "process.run"))
            {
                await ReportAsync(request, new(EventId(runId, "recovery-completed", iteration), "recovery_completed", "Edit recovery completed", "completed", Detail: JsonSerializer.Serialize(new { attempts = recoverableFailureCount })), cancellationToken);
                toolRecoveryInFlight = false;
                recoverableFailureCount = 0;
            }
            await ReportAsync(request, new(EventId(runId, "continuation", iteration), "continuation_started", "Continuing provider conversation", "completed"), cancellationToken);
            nextContent = protocol.FormatToolResult(toolCall, result);
        }

        await ReportAsync(request, new(EventId(runId, "iteration-limit", iterationLimit), "error", "Tool iteration limit reached", "failed"), cancellationToken);
        return Failed("failed", $"The provider exceeded the {iterationLimit}-turn tool limit.", session.ConversationReference, activities);
    }

    public async Task<ConversationRunResult> ResumeApprovedAsync(ConversationRunRequest request, PendingConversationToolCall pending, CancellationToken cancellationToken)
    {
        if (request.Project is null) return Failed("failed", "The pending approval project is unavailable.", request.ProviderSession.ConversationReference, []);
        var label = ToolActivityFormatter.Format(pending.Call);
        var detail = JsonSerializer.Serialize(new { name = pending.Call.ToolId, arguments = JsonSerializer.Deserialize<JsonElement>(pending.Call.Arguments.GetRawText()) });
        var runId = request.RunId ?? Guid.NewGuid().ToString("N");
        request = request with { RunId = runId };
        await ReportAsync(request, new(EventId(runId, "grant", pending.NextIteration), "permission_granted", $"Approved once: {label}", "completed", Detail: detail), cancellationToken);
        await ReportAsync(request, new(EventId(runId, "approved-tool-started", pending.NextIteration), "tool_started", label, "running", Detail: detail), cancellationToken);
        var context = new ToolContext(request.Project.Id, request.Project.Path, request.Project.Path, cancellationToken);
        var result = await executor.ExecuteApprovedAsync(pending.Call, context);
        var resultDetail = JsonSerializer.Serialize(new { toolId = pending.Call.ToolId, arguments = pending.Call.Arguments, result = new { result.Success, result.Output, result.Error, result.Metadata } });
        await ReportAsync(request, new(EventId(runId, result.Success ? "approved-tool-completed" : "approved-tool-failed", pending.NextIteration), result.Success ? "tool_completed" : "tool_failed", label, result.Success ? "completed" : "failed", result.DurationMilliseconds, resultDetail), cancellationToken);
        if (!result.Success && IsRecoverableToolError(result.Error?.Code))
        {
            await ReportAsync(request, new(EventId(runId, "recovery-started", pending.NextIteration), "recovery_started", result.Error!.Code == "syntax_validation_failed" ? "Edit failed validation; recovering…" : "Recovering failed edit…", "running", Detail: resultDetail), cancellationToken);
            return await RunAsync(request with { ContinuationContent = protocol.FormatRecoverableToolFailure(pending.Call, result), StartIteration = pending.NextIteration, RecoverableFailureCount = 1 }, cancellationToken);
        }
        if (!result.Success) return Failed("failed", result.Error?.Message ?? "Approved tool execution failed.", request.ProviderSession.ConversationReference, [new(pending.Call.ToolId, label, false)]);
        await ReportAsync(request, new(EventId(runId, "continuation", pending.NextIteration), "continuation_started", "Continuing provider conversation", "completed"), cancellationToken);
        return await RunAsync(request with { ContinuationContent = protocol.FormatToolResult(pending.Call, result), StartIteration = pending.NextIteration }, cancellationToken);
    }

    private static ConversationRunResult Failed(string status, string error, string? reference, IReadOnlyList<ToolActivity> activities) => new(status, null, error, reference, activities);
    private static string EventId(string runId, string kind, int iteration, int? segment = null) => segment is null ? $"{runId}:{kind}:{iteration}" : $"{runId}:{kind}:{iteration}:{segment}";
    private static bool IsRecoverableToolError(string? code) => code is "edit_too_large" or "edit_match_not_found" or "text_not_found" or "ambiguous_edit" or "anchor_too_large" or "anchor_not_found" or "anchor_ambiguous" or "ambiguous_anchor" or "invalid_occurrence" or "occurrence_out_of_range" or "invalid_position" or "patch_failed" or "invalid_patch" or "invalid_patch_hunk" or "patch_hunk_not_found" or "ambiguous_patch_hunk" or "syntax_validation_failed" or "command_failed" or "invalid_line_range" or "path_outside_project" or "file_not_found" or "directory_not_found";
    private static bool IsEditingTool(string toolId) => toolId is "filesystem.write" or "filesystem.edit" or "filesystem.insert" or "filesystem.patch";
    private static bool IsInspectionTool(string toolId) => toolId is "filesystem.read" or "filesystem.list" or "filesystem.search";
    private static async Task<ConversationRunResult> CompleteFromDelegationAsync(ConversationRunRequest request, string runId, int iteration, AgentDelegationOutcome outcome, string? reference, IReadOnlyList<ToolActivity> activities, CancellationToken cancellationToken)
    {
        var final = string.IsNullOrWhiteSpace(outcome.Result)
            ? "The delegated work completed successfully."
            : outcome.Result.Trim();
        await ReportAsync(request, new(EventId(runId, "final", iteration), "final_response", "Response completed from verified delegated result", "completed", Detail: JsonSerializer.Serialize(new { content = final, source = "delegated_result", childRunId = outcome.ChildRunId })), cancellationToken);
        return new("completed", final, null, reference, activities);
    }
    private static bool TryParseDelegation(JsonElement arguments,out AgentDelegationCall? call,out string? error){call=null;error=null;if(!arguments.TryGetProperty("agentRole",out var role)||role.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(role.GetString())||!arguments.TryGetProperty("objective",out var objective)||objective.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(objective.GetString())){error="agent.delegate requires non-empty agentRole and objective strings.";return false;}call=new(role.GetString()!.Trim(),objective.GetString()!.Trim());return true;}
    private static ValueTask ReportAsync(ConversationRunRequest request, ConversationProgress progress, CancellationToken cancellationToken) => request.Progress?.Invoke(progress with { Timestamp = progress.Timestamp ?? DateTimeOffset.UtcNow }, cancellationToken) ?? ValueTask.CompletedTask;
}
