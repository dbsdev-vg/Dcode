namespace DCode.Server.Chats.AgentLoop;

using DCode.Server.Tools;

public sealed record ToolActivity(string ToolId, string Label, bool Success);

public static class ToolActivityFormatter
{
    public static string Format(ToolCall call)
    {
        var path = call.Arguments.TryGetProperty("path", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
        return call.ToolId switch
        {
            "filesystem.read" => FormatRead(call, path),
            "filesystem.list" => $"Listing {path ?? "project files"}",
            "filesystem.search" => "Searching project",
            "filesystem.write" => $"Writing {path ?? "file"}",
            "filesystem.edit" => $"Editing {path ?? "file"}",
            "process.run" => FormatCommand(call),
            _ => $"Running {call.ToolId}"
        };
    }

    private static string FormatCommand(ToolCall call)
    {
        var command = call.Arguments.TryGetProperty("command", out var commandValue) && commandValue.ValueKind == System.Text.Json.JsonValueKind.String ? commandValue.GetString() : "command";
        var arguments = call.Arguments.TryGetProperty("arguments", out var argumentValues) && argumentValues.ValueKind == System.Text.Json.JsonValueKind.Array ? string.Join(" ", argumentValues.EnumerateArray().Select(item => item.GetString())) : "";
        return $"Running {command} {arguments}".TrimEnd();
    }

    private static string FormatRead(ToolCall call, string? path)
    {
        var hasStart = call.Arguments.TryGetProperty("startLine", out var start);
        var hasEnd = call.Arguments.TryGetProperty("endLine", out var end);
        return hasStart || hasEnd ? $"Reading {path ?? "file"} lines {(hasStart ? start.GetInt32() : 1)}–{(hasEnd ? end.GetInt32().ToString() : "end")}" : $"Reading {path ?? "file"}";
    }
}
