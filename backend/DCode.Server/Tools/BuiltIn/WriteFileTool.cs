using DCode.Server.Tools.Permissions;
using DCode.Server.Tools.Validation;

namespace DCode.Server.Tools.BuiltIn;

public sealed class WriteFileTool(SourceSyntaxValidationService? validation = null) : ITool
{
    private readonly SourceSyntaxValidationService validation = validation ?? SourceSyntaxValidationService.Default;
    private static readonly IReadOnlySet<ToolPermission> Permissions = new HashSet<ToolPermission> { ToolPermission.FileSystemWrite };

    public ToolDefinition Definition { get; } = ToolDefinition.Create(
        "filesystem.write",
        "Create or overwrite a UTF-8 text file inside the active project.",
        new { type = "object", properties = new { path = new { type = "string" }, content = new { type = "string" }, createDirectories = new { type = "boolean", @default = false } }, required = new[] { "path", "content" }, additionalProperties = false },
        ToolPermission.FileSystemWrite
    );

    public IReadOnlySet<ToolPermission> RequiredPermissions => Permissions;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context)
    {
        var relativePath = ToolArguments.RequiredString(call.Arguments, "path");
        var content = ToolArguments.OptionalString(call.Arguments, "content")
            ?? throw new ArgumentException("Argument 'content' must be a string.");
        var createDirectories = ToolArguments.OptionalBoolean(call.Arguments, "createDirectories");
        var fullPath = ProjectPathSandbox.Resolve(context, relativePath, allowProjectRoot: false);
        var validationResult = await validation.ValidateAsync(fullPath, content, context.ProjectPath, context.CancellationToken);
        if (SourceSyntaxValidationService.Failure(validationResult) is { } failure) return failure;
        var parent = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(parent))
        {
            if (!createDirectories) throw new DirectoryNotFoundException($"Parent directory not found: {Path.GetRelativePath(context.ProjectPath, parent)}");
            Directory.CreateDirectory(parent);
        }

        var created = !File.Exists(fullPath);
        await TextFileMutation.WriteAtomicallyAsync(fullPath, content, context.CancellationToken);
        return ToolResult.Ok(new { path = Path.GetRelativePath(context.ProjectPath, fullPath), created, length = content.Length }, SourceSyntaxValidationService.Metadata(validationResult));
    }
}
