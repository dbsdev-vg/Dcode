using DCode.Server.Tools.Permissions;

namespace DCode.Server.Tools.BuiltIn;

public sealed class ListFilesTool : ITool
{
    private const int MaximumEntries = 2000;
    private static readonly IReadOnlySet<ToolPermission> Permissions = new HashSet<ToolPermission> { ToolPermission.FileSystemRead };

    public ToolDefinition Definition { get; } = ToolDefinition.Create(
        "filesystem.list",
        "List files and directories inside the active project.",
        new { type = "object", properties = new { path = new { type = "string", @default = "." }, recursive = new { type = "boolean", @default = false } }, additionalProperties = false },
        ToolPermission.FileSystemRead
    );

    public IReadOnlySet<ToolPermission> RequiredPermissions => Permissions;

    public Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context)
    {
        var relativePath = ToolArguments.OptionalString(call.Arguments, "path") ?? ".";
        var recursive = ToolArguments.OptionalBoolean(call.Arguments, "recursive");
        var directory = ProjectPathSandbox.Resolve(context, relativePath);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Project directory not found: {relativePath}");

        var entries = new List<object>();
        var pending = new Queue<string>();
        pending.Enqueue(directory);
        var truncated = false;
        var skippedCount = 0;

        while (pending.Count > 0 && !truncated)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var current = pending.Dequeue();
            string[] currentEntries;
            try { currentEntries = Directory.GetFileSystemEntries(current); }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                skippedCount++;
                continue;
            }

            foreach (var path in currentEntries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    ProjectPathSandbox.Resolve(context, Path.GetRelativePath(context.ProjectPath, path));
                    var isDirectory = Directory.Exists(path);
                    entries.Add(new
                    {
                        path = Path.GetRelativePath(context.ProjectPath, path),
                        name = Path.GetFileName(path),
                        isDirectory,
                        size = isDirectory ? (long?)null : new FileInfo(path).Length
                    });
                    if (recursive && isDirectory) pending.Enqueue(path);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException)
                {
                    skippedCount++;
                    continue;
                }
                if (entries.Count >= MaximumEntries) { truncated = true; break; }
            }
        }

        return Task.FromResult(ToolResult.Ok(entries, new Dictionary<string, object?>
        {
            ["count"] = entries.Count,
            ["skippedCount"] = skippedCount,
            ["truncated"] = truncated
        }));
    }
}
