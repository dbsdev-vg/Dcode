using DCode.Server.Tools.Permissions;
using DCode.Server.Tools.Validation;

namespace DCode.Server.Tools.BuiltIn;

public sealed class EditFileTool(SourceSyntaxValidationService? validation = null) : ITool
{
    private readonly SourceSyntaxValidationService validation = validation ?? SourceSyntaxValidationService.Default;
    public const int MaximumTextLength = 16 * 1024;
    private static readonly IReadOnlySet<ToolPermission> Permissions = new HashSet<ToolPermission> { ToolPermission.FileSystemRead, ToolPermission.FileSystemWrite };

    public ToolDefinition Definition { get; } = ToolDefinition.Create(
        "filesystem.edit",
        "Replace a short, unique exact-text match in an existing UTF-8 project file. Use insert or patch for larger changes.",
        new { type = "object", properties = new { path = new { type = "string" }, oldText = new { type = "string" }, newText = new { type = "string" }, replaceAll = new { type = "boolean", @default = false } }, required = new[] { "path", "oldText", "newText" }, additionalProperties = false },
        ToolPermission.FileSystemRead,
        ToolPermission.FileSystemWrite
    );

    public IReadOnlySet<ToolPermission> RequiredPermissions => Permissions;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context)
    {
        var relativePath = ToolArguments.RequiredString(call.Arguments, "path");
        var oldText = ToolArguments.RequiredString(call.Arguments, "oldText");
        var newText = ToolArguments.OptionalString(call.Arguments, "newText")
            ?? throw new ArgumentException("Argument 'newText' must be a string.");
        var replaceAll = ToolArguments.OptionalBoolean(call.Arguments, "replaceAll");
        if (oldText.Length > MaximumTextLength || newText.Length > MaximumTextLength)
            return ToolResult.Fail("edit_too_large", $"filesystem.edit accepts at most {MaximumTextLength} characters in oldText or newText. Use filesystem.insert or filesystem.patch for larger changes.", new Dictionary<string, object?> { ["maximumTextLength"] = MaximumTextLength, ["oldTextLength"] = oldText.Length, ["newTextLength"] = newText.Length });
        var fullPath = ProjectPathSandbox.Resolve(context, relativePath, allowProjectRoot: false);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Project file not found: {relativePath}", fullPath);

        var content = await File.ReadAllTextAsync(fullPath, context.CancellationToken);
        var count = TextFileMutation.CountOccurrences(content, oldText);
        if (count == 0) return ToolResult.Fail("text_not_found", "The requested text was not found in the file.");
        if (!replaceAll && count > 1) return ToolResult.Fail("ambiguous_edit", "The requested text occurs more than once. Set replaceAll or provide a unique match.", new Dictionary<string, object?> { ["occurrences"] = count });

        var updated = replaceAll ? content.Replace(oldText, newText, StringComparison.Ordinal) : TextFileMutation.ReplaceFirst(content, oldText, newText);
        var validationResult = await validation.ValidateAsync(fullPath, updated, context.ProjectPath, context.CancellationToken);
        if (SourceSyntaxValidationService.Failure(validationResult) is { } failure) return failure;
        await TextFileMutation.WriteAtomicallyAsync(fullPath, updated, context.CancellationToken);
        return ToolResult.Ok(new { path = Path.GetRelativePath(context.ProjectPath, fullPath), replacements = replaceAll ? count : 1, length = updated.Length }, SourceSyntaxValidationService.Metadata(validationResult));
    }

}
