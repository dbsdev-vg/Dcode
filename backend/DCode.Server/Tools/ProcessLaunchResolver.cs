namespace DCode.Server.Tools;

internal static class ProcessLaunchResolver
{
    internal static ProcessLaunch Resolve(string command, IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsWindows() || !string.Equals(command, "npm", StringComparison.OrdinalIgnoreCase))
            return new(command, arguments);

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var node = Path.Combine(directory, "node.exe");
            var cli = Path.Combine(directory, "node_modules", "npm", "bin", "npm-cli.js");
            if (File.Exists(node) && File.Exists(cli)) return new(node, [cli, .. arguments]);
        }
        throw new FileNotFoundException("A complete Node.js/npm installation was not found on PATH. DCode requires node.exe and node_modules/npm/bin/npm-cli.js from the same installation.");
    }
}

internal sealed record ProcessLaunch(string Executable, IReadOnlyList<string> Arguments);
