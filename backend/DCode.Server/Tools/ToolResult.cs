namespace DCode.Server.Tools;

public sealed record ToolError(string Code, string Message);

public sealed record ToolResult(
    bool Success,
    object? Output,
    ToolError? Error,
    IReadOnlyDictionary<string, object?> Metadata,
    long DurationMilliseconds
)
{
    public static ToolResult Ok(object? output = null, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(true, output, null, metadata ?? new Dictionary<string, object?>(), 0);

    public static ToolResult Fail(string code, string message, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(false, null, new ToolError(code, message), metadata ?? new Dictionary<string, object?>(), 0);
}
