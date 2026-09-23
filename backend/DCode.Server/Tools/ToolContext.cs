namespace DCode.Server.Tools;

public sealed record ToolContext(
    string ProjectId,
    string ProjectPath,
    string WorkingDirectory,
    CancellationToken CancellationToken
);
