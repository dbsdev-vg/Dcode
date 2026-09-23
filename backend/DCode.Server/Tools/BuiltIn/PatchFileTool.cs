using System.Text.Json;
using DCode.Server.Tools.Permissions;
using DCode.Server.Tools.Validation;

namespace DCode.Server.Tools.BuiltIn;

public sealed class PatchFileTool(SourceSyntaxValidationService? validation = null) : ITool
{
    private readonly SourceSyntaxValidationService validation = validation ?? SourceSyntaxValidationService.Default;
    private static readonly IReadOnlySet<ToolPermission> Permissions = new HashSet<ToolPermission> { ToolPermission.FileSystemRead, ToolPermission.FileSystemWrite };
    public ToolDefinition Definition { get; } = ToolDefinition.Create(
        "filesystem.patch",
        "Atomically apply multiple ordered, unique exact-text replacement hunks to an existing UTF-8 project file.",
        new { type = "object", properties = new { path = new { type = "string" }, hunks = new { type = "array", minItems = 1, items = new { type = "object", properties = new { oldText = new { type = "string" }, newText = new { type = "string" } }, required = new[] { "oldText", "newText" }, additionalProperties = false } } }, required = new[] { "path", "hunks" }, additionalProperties = false },
        ToolPermission.FileSystemRead, ToolPermission.FileSystemWrite);
    public IReadOnlySet<ToolPermission> RequiredPermissions => Permissions;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context)
    {
        var relativePath = ToolArguments.RequiredString(call.Arguments, "path");
        if (!call.Arguments.TryGetProperty("hunks", out var hunksElement) || hunksElement.ValueKind != JsonValueKind.Array || hunksElement.GetArrayLength() == 0)
            return ToolResult.Fail("invalid_patch", "Argument 'hunks' must be a non-empty array.");
        var fullPath = ProjectPathSandbox.Resolve(context, relativePath, allowProjectRoot: false);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Project file not found: {relativePath}", fullPath);
        var original = await File.ReadAllTextAsync(fullPath, context.CancellationToken);
        var updated = original;
        var applied = 0;
        foreach (var hunk in hunksElement.EnumerateArray())
        {
            if (hunk.ValueKind != JsonValueKind.Object) return InvalidHunk(applied, "Each patch hunk must be an object.");
            string oldText;
            string newText;
            try { oldText = ToolArguments.RequiredString(hunk, "oldText"); newText = ToolArguments.OptionalString(hunk, "newText") ?? throw new ArgumentException(); }
            catch (ArgumentException) { return InvalidHunk(applied, "Each patch hunk requires non-empty oldText and string newText."); }
            var occurrences = TextFileMutation.CountOccurrences(updated, oldText);
            if (occurrences == 0) return ToolResult.Fail("patch_hunk_not_found", $"Patch hunk {applied + 1} did not match. No changes were written.", new Dictionary<string, object?> { ["hunkIndex"] = applied, ["appliedBeforeFailure"] = applied });
            if (occurrences > 1) return ToolResult.Fail("ambiguous_patch_hunk", $"Patch hunk {applied + 1} matched {occurrences} locations. No changes were written.", new Dictionary<string, object?> { ["hunkIndex"] = applied, ["occurrences"] = occurrences, ["appliedBeforeFailure"] = applied });
            updated = TextFileMutation.ReplaceFirst(updated, oldText, newText);
            applied++;
        }
        var validationResult = await validation.ValidateAsync(fullPath, updated, context.ProjectPath, context.CancellationToken);
        if (SourceSyntaxValidationService.Failure(validationResult) is { } failure) return failure;
        await TextFileMutation.WriteAtomicallyAsync(fullPath, updated, context.CancellationToken);
        return ToolResult.Ok(new { path = Path.GetRelativePath(context.ProjectPath, fullPath), hunksApplied = applied, originalLength = original.Length, length = updated.Length }, SourceSyntaxValidationService.Metadata(validationResult));
    }

    private static ToolResult InvalidHunk(int index, string message) => ToolResult.Fail("invalid_patch_hunk", message, new Dictionary<string, object?> { ["hunkIndex"] = index });
}
