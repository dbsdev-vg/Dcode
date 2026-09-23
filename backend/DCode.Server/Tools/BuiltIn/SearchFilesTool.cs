using DCode.Server.Tools.Permissions;
using System.Text;

namespace DCode.Server.Tools.BuiltIn;

public sealed class SearchFilesTool : ITool
{
    private const int MaximumMatches = 500;
    private const long MaximumFileBytes = 2 * 1024 * 1024;
    private static readonly IReadOnlySet<ToolPermission> Permissions = new HashSet<ToolPermission> { ToolPermission.FileSystemRead };

    public ToolDefinition Definition { get; } = ToolDefinition.Create(
        "filesystem.search",
        "Search UTF-8 project files for plain text and return matching lines.",
        new { type = "object", properties = new { query = new { type = "string" }, path = new { type = "string", @default = "." }, caseSensitive = new { type = "boolean", @default = false } }, required = new[] { "query" }, additionalProperties = false },
        ToolPermission.FileSystemRead
    );

    public IReadOnlySet<ToolPermission> RequiredPermissions => Permissions;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context)
    {
        var query = ToolArguments.RequiredString(call.Arguments, "query");
        var relativePath = ToolArguments.OptionalString(call.Arguments, "path") ?? ".";
        var caseSensitive = ToolArguments.OptionalBoolean(call.Arguments, "caseSensitive");
        var searchRoot = ProjectPathSandbox.Resolve(context, relativePath);
        if (!Directory.Exists(searchRoot)) throw new DirectoryNotFoundException($"Project directory not found: {relativePath}");

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<object>();
        var filesSearched = 0;
        var skippedCount = 0;
        var pendingDirectories = new Queue<string>();
        pendingDirectories.Enqueue(searchRoot);

        while (pendingDirectories.Count > 0)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Dequeue();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                skippedCount++;
                continue;
            }

            foreach (var entry in entries)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    ProjectPathSandbox.Resolve(context, Path.GetRelativePath(context.ProjectPath, entry));

                    if (Directory.Exists(entry))
                    {
                        pendingDirectories.Enqueue(entry);
                        continue;
                    }
                    if (!File.Exists(entry) || new FileInfo(entry).Length > MaximumFileBytes) continue;

                    var lines = await File.ReadAllLinesAsync(entry, context.CancellationToken);
                    filesSearched++;
                    for (var index = 0; index < lines.Length; index++)
                    {
                        if (!lines[index].Contains(query, comparison)) continue;
                        matches.Add(new { path = Path.GetRelativePath(context.ProjectPath, entry), line = index + 1, content = lines[index] });
                        if (matches.Count >= MaximumMatches)
                        {
                            return ToolResult.Ok(matches, Metadata(matches.Count, filesSearched, skippedCount, truncated: true));
                        }
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException or DecoderFallbackException)
                {
                    skippedCount++;
                }
            }
        }

        return ToolResult.Ok(matches, Metadata(matches.Count, filesSearched, skippedCount, truncated: false));
    }

    private static IReadOnlyDictionary<string, object?> Metadata(int count, int filesSearched, int skippedCount, bool truncated) =>
        new Dictionary<string, object?>
        {
            ["count"] = count,
            ["filesSearched"] = filesSearched,
            ["skippedCount"] = skippedCount,
            ["truncated"] = truncated
        };
}
