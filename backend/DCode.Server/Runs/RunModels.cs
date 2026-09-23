namespace DCode.Server.Runs;

public sealed record RunConfiguration(string Id,string ProjectId,string Name,string Command,IReadOnlyList<string> Arguments,string WorkingDirectory,bool IsDetected,DateTimeOffset CreatedUtc,DateTimeOffset UpdatedUtc);
public sealed record SaveRunConfigurationRequest(string Name,string Command,IReadOnlyList<string>? Arguments,string? WorkingDirectory);
public sealed record RunExecution(string Id,string ProjectId,string ConfigurationId,string Status,string Command,IReadOnlyList<string> Arguments,string WorkingDirectory,string Stdout,string Stderr,int? ExitCode,string? Error,DateTimeOffset CreatedUtc,DateTimeOffset StartedUtc,DateTimeOffset? CompletedUtc,DateTimeOffset UpdatedUtc);
