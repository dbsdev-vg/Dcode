using System.Diagnostics;
using DCode.Server.Tools.Permissions;

namespace DCode.Server.Tools.BuiltIn;

public sealed class RunCommandTool : ITool
{
    private const int DefaultTimeoutMilliseconds = 120_000;
    private const int MaximumTimeoutMilliseconds = 600_000;
    private static readonly IReadOnlySet<ToolPermission> Permissions = new HashSet<ToolPermission> { ToolPermission.ProcessExecute };

    public ToolDefinition Definition { get; } = ToolDefinition.Create(
        "process.run",
        "Run an executable with arguments inside the active project and capture its output.",
        new { type = "object", properties = new { command = new { type = "string" }, arguments = new { type = "array", items = new { type = "string" } }, workingDirectory = new { type = "string", @default = "." }, timeoutMs = new { type = "integer", minimum = 1, maximum = MaximumTimeoutMilliseconds, @default = DefaultTimeoutMilliseconds } }, required = new[] { "command" }, additionalProperties = false },
        ToolPermission.ProcessExecute
    );

    public IReadOnlySet<ToolPermission> RequiredPermissions => Permissions;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, ToolContext context)
    {
        var command = ToolArguments.RequiredString(call.Arguments, "command");
        var arguments = ToolArguments.OptionalStringArray(call.Arguments, "arguments");
        var requestedDirectory = ToolArguments.OptionalString(call.Arguments, "workingDirectory")
            ?? Path.GetRelativePath(context.ProjectPath, context.WorkingDirectory);
        var timeoutMs = ToolArguments.OptionalInteger(call.Arguments, "timeoutMs", DefaultTimeoutMilliseconds);
        if (timeoutMs is < 1 or > MaximumTimeoutMilliseconds) throw new ArgumentException($"Argument 'timeoutMs' must be between 1 and {MaximumTimeoutMilliseconds}.");

        var workingDirectory = ProjectPathSandbox.Resolve(context, requestedDirectory);
        if (!Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException($"Working directory not found: {requestedDirectory}");

        var launch = ProcessLaunchResolver.Resolve(command, arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.Executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in launch.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) return ToolResult.Fail("process_start_failed", $"Unable to start command: {command}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(context.CancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(context.CancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, timeout.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await DrainAsync(stdoutTask, stderrTask);
            return ToolResult.Fail("timeout", $"Command exceeded the {timeoutMs} ms timeout.", new Dictionary<string, object?> { ["timeoutMs"] = timeoutMs });
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainAsync(stdoutTask, stderrTask);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = new { stdout, stderr, exitCode = process.ExitCode };
        return process.ExitCode == 0
            ? ToolResult.Ok(output)
            : ToolResult.Fail("command_failed", $"Command exited with code {process.ExitCode}.", new Dictionary<string, object?> { ["stdout"] = stdout, ["stderr"] = stderr, ["exitCode"] = process.ExitCode });
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    private static async Task DrainAsync(Task<string> stdout, Task<string> stderr)
    {
        try { await Task.WhenAll(stdout, stderr); }
        catch (OperationCanceledException) { }
    }
}
