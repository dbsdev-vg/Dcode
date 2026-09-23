using System.Diagnostics;
using System.Text.Json;

namespace DCode.Server.Tools.Validation;

public sealed class TypeScriptSyntaxValidator : ISourceSyntaxValidator
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".ts", ".tsx", ".js", ".jsx" };
    private const string ValidationScript = """
const ts = require(process.argv[1]);
const file = process.argv[2];
let content = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => content += chunk);
process.stdin.on('end', () => {
  const kind = file.endsWith('.tsx') ? ts.ScriptKind.TSX : file.endsWith('.jsx') ? ts.ScriptKind.JSX : file.endsWith('.js') ? ts.ScriptKind.JS : ts.ScriptKind.TS;
  const source = ts.createSourceFile(file, content, ts.ScriptTarget.Latest, true, kind);
  const diagnostics = source.parseDiagnostics.map(d => {
    const position = d.start == null ? null : source.getLineAndCharacterOfPosition(d.start);
    return { message: ts.flattenDiagnosticMessageText(d.messageText, ' '), line: position ? position.line + 1 : null, column: position ? position.character + 1 : null };
  });
  process.stdout.write(JSON.stringify(diagnostics));
});
""";

    public bool Supports(string path) => Extensions.Contains(Path.GetExtension(path));

    public async Task<SourceValidationResult> ValidateAsync(string path, string content, string projectPath, CancellationToken cancellationToken)
    {
        var language = Path.GetExtension(path).ToLowerInvariant() switch { ".ts" => "typescript", ".tsx" => "typescriptreact", ".js" => "javascript", _ => "javascriptreact" };
        var compilerPath = ResolveCompiler(projectPath);
        if (compilerPath is null) return new(false, language, [], "TypeScript syntax validation is unavailable because typescript.js could not be resolved.");
        try
        {
            using var process = new Process { StartInfo = new("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            process.StartInfo.ArgumentList.Add("-e"); process.StartInfo.ArgumentList.Add(ValidationScript); process.StartInfo.ArgumentList.Add(compilerPath); process.StartInfo.ArgumentList.Add(path);
            process.Start();
            await process.StandardInput.WriteAsync(content.AsMemory(), cancellationToken); process.StandardInput.Close();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken); var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask; var error = await errorTask;
            if (process.ExitCode != 0) return new(false, language, [], $"TypeScript validator failed: {error.Trim()}");
            var diagnostics = JsonSerializer.Deserialize<List<SourceDiagnostic>>(output, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            return new(true, language, diagnostics);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return new(false, language, [], $"TypeScript validator could not start: {exception.Message}"); }
    }

    private static string? ResolveCompiler(string projectPath)
    {
        var candidates = new List<string> { Path.Combine(projectPath, "node_modules", "typescript", "lib", "typescript.js") };
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            candidates.Add(Path.Combine(current.FullName, "apps", "Dcode", "frontend", "node_modules", "typescript", "lib", "typescript.js"));
            candidates.Add(Path.Combine(current.FullName, "node_modules", "typescript", "lib", "typescript.js"));
            current = current.Parent;
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
