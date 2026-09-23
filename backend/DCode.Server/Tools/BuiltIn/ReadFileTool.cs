using DCode.Server.Tools.Permissions;

namespace DCode.Server.Tools.BuiltIn;

public sealed class ReadFileTool : ITool
{
    private static readonly IReadOnlySet<ToolPermission> Permissions = new HashSet<ToolPermission> { ToolPermission.FileSystemRead };

    public ToolDefinition Definition { get; } = ToolDefinition.Create(
        "filesystem.read",
        "Read a UTF-8 text file inside the active project, optionally limiting output to a 1-based inclusive line range.",
        new { type = "object", properties = new { path = new { type = "string" }, startLine = new { type = "integer", minimum = 1 }, endLine = new { type = "integer", minimum = 1 } }, required = new[] { "path" }, additionalProperties = false },
        ToolPermission.FileSystemRead
    );

    public IReadOnlySet<ToolPermission> RequiredPermissions => Permissions;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context)
    {
        var relativePath = ToolArguments.RequiredString(call.Arguments, "path");
        var fullPath = ProjectPathSandbox.Resolve(context, relativePath, allowProjectRoot: false);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Project file not found: {relativePath}", fullPath);

        var content = await File.ReadAllTextAsync(fullPath, context.CancellationToken);
        var hasStart = call.Arguments.TryGetProperty("startLine", out var startElement);
        var hasEnd = call.Arguments.TryGetProperty("endLine", out var endElement);
        if (!hasStart && !hasEnd)
        {
            var totalLines = CountLines(content);
            return ToolResult.Ok(new { path = Path.GetRelativePath(context.ProjectPath, fullPath), content, length = content.Length }, RangeMetadata(totalLines == 0 ? 0 : 1, totalLines, totalLines, false));
        }
        if ((hasStart && (startElement.ValueKind != System.Text.Json.JsonValueKind.Number || !startElement.TryGetInt32(out _))) || (hasEnd && (endElement.ValueKind != System.Text.Json.JsonValueKind.Number || !endElement.TryGetInt32(out _))))
            return ToolResult.Fail("invalid_line_range", "startLine and endLine must be 1-based integers.");
        var lines = SplitLines(content);
        var requestedStart = hasStart ? startElement.GetInt32() : 1;
        var requestedEnd = hasEnd ? endElement.GetInt32() : lines.Length;
        if (requestedStart < 1 || requestedEnd < 1 || requestedEnd < requestedStart || requestedStart > lines.Length)
            return ToolResult.Fail("invalid_line_range", $"Requested line range {requestedStart}-{requestedEnd} is invalid for a file with {lines.Length} line(s).", RangeMetadata(0, 0, lines.Length, true));
        var actualEnd = Math.Min(requestedEnd, lines.Length);
        var selected = lines[(requestedStart - 1)..actualEnd];
        var rangedContent = string.Join(Environment.NewLine, selected);
        var numberedContent = string.Join(Environment.NewLine, selected.Select((line, index) => $"{requestedStart + index}: {line}"));
        return ToolResult.Ok(new { path = Path.GetRelativePath(context.ProjectPath, fullPath), content = rangedContent, numberedContent, length = rangedContent.Length }, RangeMetadata(requestedStart, actualEnd, lines.Length, true));
    }

    private static int CountLines(string content) => content.Length == 0 ? 0 : content.Count(character => character == '\n') + 1;
    private static string[] SplitLines(string content) => content.Length == 0 ? [] : content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    private static IReadOnlyDictionary<string, object?> RangeMetadata(int start, int end, int total, bool ranged) => new Dictionary<string, object?> { ["actualStartLine"] = start, ["actualEndLine"] = end, ["totalLineCount"] = total, ["ranged"] = ranged };
}
