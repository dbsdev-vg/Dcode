using DCode.Server.Tools.Permissions;
using DCode.Server.Tools.Validation;

namespace DCode.Server.Tools.BuiltIn;

public sealed class InsertFileTool(SourceSyntaxValidationService? validation = null) : ITool
{
    private readonly SourceSyntaxValidationService validation = validation ?? SourceSyntaxValidationService.Default;
    public const int MaximumAnchorLength = 2 * 1024;
    private static readonly IReadOnlySet<ToolPermission> Permissions = new HashSet<ToolPermission> { ToolPermission.FileSystemRead, ToolPermission.FileSystemWrite };
    public ToolDefinition Definition { get; } = ToolDefinition.Create(
        "filesystem.insert",
        "Insert content immediately before or after a stable anchor in an existing UTF-8 project file. Repeated anchors require an explicit first, last, or 1-based numeric occurrence.",
        new { type = "object", properties = new { path = new { type = "string" }, anchor = new { type = "string" }, position = new { type = "string", @enum = new[] { "before", "after" } }, occurrence = new { oneOf = new object[] { new { type = "string", @enum = new[] { "first", "last" } }, new { type = "integer", minimum = 1 } } }, content = new { type = "string" } }, required = new[] { "path", "anchor", "position", "content" }, additionalProperties = false },
        ToolPermission.FileSystemRead, ToolPermission.FileSystemWrite);
    public IReadOnlySet<ToolPermission> RequiredPermissions => Permissions;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context)
    {
        var relativePath = ToolArguments.RequiredString(call.Arguments, "path");
        var anchor = ToolArguments.RequiredString(call.Arguments, "anchor");
        var position = ToolArguments.RequiredString(call.Arguments, "position");
        var insertedContent = ToolArguments.OptionalString(call.Arguments, "content") ?? throw new ArgumentException("Argument 'content' must be a string.");
        if (anchor.Length > MaximumAnchorLength)
            return ToolResult.Fail("anchor_too_large", $"filesystem.insert anchors are limited to {MaximumAnchorLength} characters. Use the shortest unique stable anchor, or use filesystem.patch when no compact anchor exists.", new Dictionary<string, object?> { ["maximumAnchorLength"] = MaximumAnchorLength, ["anchorLength"] = anchor.Length });
        if (position is not ("before" or "after")) return ToolResult.Fail("invalid_position", "Argument 'position' must be 'before' or 'after'.");
        var fullPath = ProjectPathSandbox.Resolve(context, relativePath, allowProjectRoot: false);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Project file not found: {relativePath}", fullPath);
        var original = await File.ReadAllTextAsync(fullPath, context.CancellationToken);
        var offsets = FindOccurrences(original, anchor);
        if (offsets.Count == 0) return ToolResult.Fail("anchor_not_found", "The insertion anchor was not found.", Metadata(0, null, null));
        var selection = SelectOccurrence(call, offsets.Count);
        if (!selection.Success) return ToolResult.Fail(selection.ErrorCode!, selection.ErrorMessage!, Metadata(offsets.Count, selection.SelectedOccurrence, null));
        var selectedOccurrence = selection.SelectedOccurrence!.Value;
        var anchorOffset = offsets[selectedOccurrence - 1];
        var index = anchorOffset + (position == "after" ? anchor.Length : 0);
        var updated = string.Concat(original.AsSpan(0, index), insertedContent, original.AsSpan(index));
        var validationResult = await validation.ValidateAsync(fullPath, updated, context.ProjectPath, context.CancellationToken);
        if (SourceSyntaxValidationService.Failure(validationResult) is { } failure) return failure;
        await TextFileMutation.WriteAtomicallyAsync(fullPath, updated, context.CancellationToken);
        var metadata = Metadata(offsets.Count, selectedOccurrence, index, validationResult);
        return ToolResult.Ok(new { path = Path.GetRelativePath(context.ProjectPath, fullPath), position, anchorLength = anchor.Length, insertedLength = insertedContent.Length, length = updated.Length }, metadata);
    }

    private static (bool Success, int? SelectedOccurrence, string? ErrorCode, string? ErrorMessage) SelectOccurrence(ToolCall call, int count)
    {
        if (!call.Arguments.TryGetProperty("occurrence", out var occurrence))
            return count == 1 ? (true, 1, null, null) : (false, null, "anchor_ambiguous", "The insertion anchor occurs more than once. Supply occurrence as 'first', 'last', or a 1-based number; DCode will not guess.");
        int selected;
        if (occurrence.ValueKind == System.Text.Json.JsonValueKind.String)
            selected = occurrence.GetString() switch { "first" => 1, "last" => count, _ => 0 };
        else if (occurrence.ValueKind == System.Text.Json.JsonValueKind.Number && occurrence.TryGetInt32(out var numeric)) selected = numeric;
        else return (false, null, "invalid_occurrence", "Argument 'occurrence' must be 'first', 'last', or a 1-based integer.");
        if (selected < 1) return (false, selected, "invalid_occurrence", "Numeric occurrence values are 1-based and must be at least 1.");
        if (selected > count) return (false, selected, "occurrence_out_of_range", $"Occurrence {selected} was requested, but the anchor matched {count} time(s).");
        return (true, selected, null, null);
    }

    private static List<int> FindOccurrences(string content, string anchor)
    {
        var offsets = new List<int>();
        for (var index = 0; (index = content.IndexOf(anchor, index, StringComparison.Ordinal)) >= 0; index += anchor.Length) offsets.Add(index);
        return offsets;
    }

    private static IReadOnlyDictionary<string, object?> Metadata(int count, int? selected, int? offset, SourceValidationResult? validation = null) => new Dictionary<string, object?>
    {
        ["matchedOccurrenceCount"] = count,
        ["selectedOccurrence"] = selected,
        ["insertionOffset"] = offset,
        ["validationPerformed"] = validation?.Performed ?? false,
        ["language"] = validation?.Language,
        ["diagnosticCount"] = validation?.Diagnostics.Count ?? 0
    };
}
