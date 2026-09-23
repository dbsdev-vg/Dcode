using System.Diagnostics;
using System.Text.Json;
using DCode.Server.Tools.Permissions;

namespace DCode.Server.Tools;

public sealed class ToolExecutor(ToolRegistry registry, IToolPolicyResolver policyResolver)
{
    public async Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context)
        => await ExecuteCoreAsync(call, context, approved: false);

    public async Task<ToolResult> ExecuteApprovedAsync(ToolCall call, ToolContext context)
        => await ExecuteCoreAsync(call, context, approved: true);

    private async Task<ToolResult> ExecuteCoreAsync(ToolCall call, ToolContext context, bool approved)
    {
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(call.ToolId) || !registry.TryGet(call.ToolId, out var tool))
        {
            return WithDuration(ToolResult.Fail("unknown_tool", $"Unknown tool: {call.ToolId}"), stopwatch);
        }

        if (call.Arguments.ValueKind != JsonValueKind.Object)
        {
            return WithDuration(ToolResult.Fail("invalid_arguments", "Tool arguments must be a JSON object."), stopwatch);
        }

        var resolution = policyResolver.Resolve(call.ToolId, context.ProjectId);
        if (!approved && resolution.Policy == ToolPermissionPolicy.Deny)
        {
            return WithDuration(ToolResult.Fail(
                "permission_denied",
                $"Execution of '{call.ToolId}' is denied by the {resolution.Scope} tool policy.",
                PolicyMetadata(resolution)
            ), stopwatch);
        }
        if (!approved && resolution.Policy == ToolPermissionPolicy.Ask)
        {
            return WithDuration(ToolResult.Fail(
                "approval_required",
                $"Execution of '{call.ToolId}' requires user approval.",
                PolicyMetadata(resolution)
            ), stopwatch);
        }

        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return WithDuration(await tool!.ExecuteAsync(call, context), stopwatch);
        }
        catch (OperationCanceledException)
        {
            return WithDuration(ToolResult.Fail("cancelled", "Tool execution was cancelled."), stopwatch);
        }
        catch (JsonException exception)
        {
            return WithDuration(ToolResult.Fail("invalid_arguments", exception.Message), stopwatch);
        }
        catch (ArgumentException exception)
        {
            return WithDuration(ToolResult.Fail("invalid_arguments", exception.Message), stopwatch);
        }
        catch (UnauthorizedAccessException exception)
        {
            return WithDuration(ToolResult.Fail("path_outside_project", exception.Message), stopwatch);
        }
        catch (FileNotFoundException exception)
        {
            return WithDuration(ToolResult.Fail("file_not_found", exception.Message), stopwatch);
        }
        catch (DirectoryNotFoundException exception)
        {
            return WithDuration(ToolResult.Fail("directory_not_found", exception.Message), stopwatch);
        }
        catch (Exception exception)
        {
            return WithDuration(ToolResult.Fail("execution_failed", exception.Message), stopwatch);
        }
    }

    private static ToolResult WithDuration(ToolResult result, Stopwatch stopwatch) =>
        result with { DurationMilliseconds = stopwatch.ElapsedMilliseconds };

    private static IReadOnlyDictionary<string, object?> PolicyMetadata(ToolPolicyResolution resolution) =>
        new Dictionary<string, object?>
        {
            ["policy"] = ToolPermissionPolicyValues.Serialize(resolution.Policy),
            ["scope"] = resolution.Scope
        };
}
