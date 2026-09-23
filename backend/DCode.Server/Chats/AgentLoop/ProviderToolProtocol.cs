using System.Text;
using DCode.Server.Tools;

namespace DCode.Server.Chats.AgentLoop;

public sealed class ProviderToolProtocol
{
    public string FormatUserMessage(string userMessage, IReadOnlyList<ToolDefinition> tools)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are working inside a DCode project workspace.");
        builder.AppendLine("When local project information is required, request exactly one tool using this strict format:");
        builder.AppendLine("<tool>");
        builder.AppendLine("{\"name\":\"filesystem.read\",\"arguments\":{\"path\":\"package.json\"}}");
        builder.AppendLine("</tool>");
        builder.AppendLine("When requesting a tool, output only the tool block with no prose around it.");
        builder.AppendLine("Never merely announce that you will read, search, list, write, or edit a project file. Request the corresponding tool immediately.");
        builder.AppendLine("Evidence rules: never claim that a file contains a problem, that a change succeeded, or that a build passed unless a successful tool/delegation result proves it. A failed read, list, search, edit, or command provides no evidence for such a claim.");
        builder.AppendLine("Do not repeat an identical successful read/search/list request unless a modifying tool changed the relevant files afterward.");
        var canDelegate = tools.Any(tool => tool.Id == "agent.delegate");
        if (canDelegate)
        {
            builder.AppendLine("You are the Lead Agent. You may inspect the project, but ALL code-changing work must be delegated to Coder with agent.delegate.");
            builder.AppendLine("Do not request filesystem.write, filesystem.edit, filesystem.insert, or filesystem.patch directly, even for a small change or a new file.");
            builder.AppendLine("You cannot execute project commands directly. When the user asks to test, build, typecheck, lint, run, preview, or otherwise validate the project, delegate that verification objective to Coder with agent.delegate. Do not tell the user to run it manually.");
        }
        else
        {
            builder.AppendLine("For code changes, inspect the current file first, then request an appropriate editing tool. Do not claim a change was made unless the tool result confirms it.");
            builder.AppendLine("Editing tool guidance:");
            builder.AppendLine("- filesystem.edit: small exact replacements where oldText is short and unique.");
            builder.AppendLine("- filesystem.insert: use a short but structurally unique anchor and never copy a large existing object into anchor. For arrays, prefer a closing delimiter with nearby unique context. If repetition is intentional, occurrence may be 'first', 'last', or a 1-based number; use 'last' only when it is semantically correct.");
            builder.AppendLine("- filesystem.patch: larger or multi-location changes using ordered exact-text hunks.");
            builder.AppendLine("- filesystem.write: create a new file or intentionally replace an entire file.");
            builder.AppendLine("Verification is mandatory after code changes. Inspect package.json or the detected project scripts, then use process.run to run the narrowest relevant typecheck, test, or build command before reporting completion. Never claim success when verification has not run or has failed.");
            builder.AppendLine("- process.run: execute verification inside the project. For npm scripts use command 'npm' with arguments such as ['run','build']. Fix reported errors and rerun until it succeeds, permission is denied, or recovery is exhausted.");
            builder.AppendLine("When the delegated objective is specifically to test, build, typecheck, lint, run, or preview the project, use process.run even when no source edit is needed. Return the actual command, exit code, and concise outcome to Lead.");
        }
        builder.AppendLine("- filesystem.read: when diagnostics contain a line number, read a small surrounding range with startLine/endLine (for line 333, roughly 323–343) instead of rereading the whole file.");
        builder.AppendLine("If a ranged read reports invalid_line_range, use totalLineCount from metadata and retry with a bounded valid range; do not guess another out-of-range interval.");
        builder.AppendLine("Do not inspect dependency directories through node_modules links or junctions. Inspect package manifests, workspace source packages, or a safe in-project source path instead.");
        builder.AppendLine("If a tool returns a size, ambiguity, or match error, choose a more suitable editing tool and retry.");
        builder.AppendLine("If an edit returns syntax_validation_failed, use the diagnostics to issue a corrected smaller edit; the invalid candidate was rolled back.");
        if (canDelegate)
        {
            builder.AppendLine("Delegation guidance:");
            builder.AppendLine("- agent.delegate: delegate every bounded coding, file-modification, or project-verification objective to Coder.");
            builder.AppendLine("- Remain responsible for the user-facing plan and final answer. Wait for the returned child result, evaluate it, and continue in this same conversation.");
            builder.AppendLine("- Answer simple questions directly, but never implement project changes directly. Do not delegate to Lead.");
        }
        builder.AppendLine("Available tools:");
        foreach (var tool in tools)
        {
            builder.Append("- ").Append(tool.Id).Append(": ").AppendLine(tool.Description);
            builder.Append("  arguments schema: ").AppendLine(tool.ArgumentSchema.GetRawText());
        }
        builder.AppendLine();
        builder.AppendLine("User request:");
        builder.Append(userMessage);
        return builder.ToString();
    }

    public string FormatDelegationResult(AgentDelegationOutcome outcome)
    {
        var payload=System.Text.Json.JsonSerializer.Serialize(new{status=outcome.Status,childRunId=outcome.ChildRunId,result=outcome.Result,error=outcome.Error});
        var instruction = outcome.Status == "completed"
            ? "The delegated work is verified complete. Produce the final user-facing answer now using only this evidence. Output prose only: do not request any tool, do not delegate again, and do not expose protocol markup."
            : "The delegated attempt did not complete. Explain the blocker honestly, or perform at most one corrected delegation if the error contains enough evidence to define it. Never claim success.";
        return $"<delegation_result>\n{payload}\n</delegation_result>\n{instruction}";
    }

    public string FormatDelegationFinalRecovery(AgentDelegationOutcome outcome, string? errorCode, string? reason)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { status = outcome.Status, childRunId = outcome.ChildRunId, result = outcome.Result, error = outcome.Error });
        return $"Your previous finalization response was rejected ({errorCode ?? "invalid_response"}: {reason ?? "invalid response"}).\n<delegation_result>\n{payload}\n</delegation_result>\nReturn one concise final user-facing answer based only on this verified result. Output prose only. Do not emit a tool block, JSON, delegation, or protocol markup.";
    }

    public string FormatDelegationLimitReached(string reason) =>
        $"Delegation cannot continue: {reason}\nReturn a concise honest final response explaining that the work was not completed. Output prose only and do not request another tool or delegation.";

    public string FormatDelegationRecovery(string reason) =>
        $"Your previous request was rejected: {reason}\nRetry the SAME operation by outputting exactly one agent.delegate tool block targeting agentRole 'coder'. Put the complete bounded implementation objective in its objective argument. Output no prose around the tool block.";

    public string FormatToolResult(ToolCall call, ToolResult result)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            name = call.ToolId,
            success = result.Success,
            output = result.Output,
            error = result.Error,
            metadata = result.Metadata
        });
        return $"<tool_result>\n{payload}\n</tool_result>\nUse this result to continue the same user request. If another tool is required, output only one strict <tool> block. Otherwise return the final answer without tool markup.";
    }

    public string FormatToolRecovery(string errorCode, string reason, IReadOnlyList<ToolDefinition> tools, string? rejectedToolId = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Your previous tool request could not be executed.");
        builder.Append("Error: ").Append(errorCode).Append(" — ").AppendLine(reason);
        builder.AppendLine("Retry the SAME operation in this conversation.");
        builder.AppendLine("Rules:");
        builder.AppendLine("- output exactly one valid <tool> request and no prose");
        builder.AppendLine("- keep the request compact and do not resend large existing file sections");
        builder.AppendLine("- prefer filesystem.insert for additions and filesystem.patch for larger or multi-location edits");
        builder.AppendLine("- use short unique anchors and split large operations into smaller sequential calls");
        builder.AppendLine("- for array appends, use a closing delimiter plus nearby unique context; use occurrence:'last' only when the last match is semantically correct");
        builder.AppendLine("- never use a large existing object as an insertion anchor");
        if (errorCode == "tool_call_truncated") builder.AppendLine("- the previous response was truncated; produce a complete closing </tool> tag");
        if (errorCode == "tool_call_too_large") builder.AppendLine("- the previous payload was too large; split it into smaller sequential tool calls");
        if (string.Equals(rejectedToolId, "filesystem.edit", StringComparison.Ordinal)) builder.AppendLine("- avoid large oldText/newText; use filesystem.insert or filesystem.patch instead");
        if (errorCode == "tool_call_unknown") builder.Append("Available tool IDs: ").AppendLine(string.Join(", ", tools.Select(tool => tool.Id)));
        return builder.ToString().Trim();
    }

    public string FormatRecoverableToolFailure(ToolCall call, ToolResult result)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { name = call.ToolId, success = false, error = result.Error, metadata = result.Metadata });
        if (call.ToolId is "filesystem.read" or "filesystem.list" or "filesystem.search")
        {
            var totalLineGuidance = result.Metadata.TryGetValue("totalLineCount", out var totalLines)
                ? $" The file has {totalLines} line(s); choose a valid range within that bound."
                : string.Empty;
            return $"<tool_result>\n{payload}\n</tool_result>\nInspection failed: {result.Error?.Code ?? "inspection_failed"}\n{result.Error?.Message}{totalLineGuidance}\nNo evidence was obtained. Correct the safe project-relative path or range and retry once with one strict <tool> block. Do not infer file contents from this failure. Do not traverse node_modules symlinks or junctions.";
        }
        if (call.ToolId == "process.run")
            return $"<tool_result>\n{payload}\n</tool_result>\nVerification failed: {result.Error?.Code ?? "command_failed"}\n{result.Error?.Message}\nInspect the stdout/stderr diagnostics, correct the project code, and rerun the relevant verification command. Do not claim completion until verification succeeds. Use one strict <tool> block with no surrounding prose.";
        var builder = new StringBuilder();
        builder.Append("<tool_result>\n").Append(payload).AppendLine("\n</tool_result>");
        builder.Append("Tool failed: ").AppendLine(result.Error?.Code ?? "unknown");
        builder.AppendLine(result.Error?.Message ?? "The edit could not be applied.");
        builder.AppendLine("The attempted edit was rolled back; the project file still contains its previous valid content.");
        builder.AppendLine("Inspect the affected area and retry the SAME operation with a corrected tool call.");
        builder.AppendLine("When diagnostics include a line number, use filesystem.read with a small startLine/endLine range around it before editing again.");
        builder.AppendLine("Use one strict <tool> block with no surrounding prose. Do not claim success until a tool result confirms it.");
        return builder.ToString().Trim();
    }
}
