namespace DCode.Server.Tools.Validation;

public sealed record SourceDiagnostic(string Message, int? Line = null, int? Column = null);
public sealed record SourceValidationResult(bool Performed, string? Language, IReadOnlyList<SourceDiagnostic> Diagnostics, string? Error = null)
{
    public bool Success => Performed && Diagnostics.Count == 0 && Error is null;
}

public interface ISourceSyntaxValidator
{
    bool Supports(string path);
    Task<SourceValidationResult> ValidateAsync(string path, string content, string projectPath, CancellationToken cancellationToken);
}
