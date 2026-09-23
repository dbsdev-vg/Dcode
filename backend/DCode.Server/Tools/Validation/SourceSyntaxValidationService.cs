namespace DCode.Server.Tools.Validation;

public sealed class SourceSyntaxValidationService(IEnumerable<ISourceSyntaxValidator> validators)
{
    public static SourceSyntaxValidationService Default { get; } = new([new TypeScriptSyntaxValidator()]);

    public async Task<SourceValidationResult> ValidateAsync(string path, string content, string projectPath, CancellationToken cancellationToken)
    {
        var validator = validators.FirstOrDefault(item => item.Supports(path));
        return validator is null
            ? new(false, null, [], null)
            : await validator.ValidateAsync(path, content, projectPath, cancellationToken);
    }

    public static IReadOnlyDictionary<string, object?> Metadata(SourceValidationResult validation) => new Dictionary<string, object?>
    {
        ["validationPerformed"] = validation.Performed,
        ["language"] = validation.Language,
        ["diagnosticCount"] = validation.Diagnostics.Count
    };

    public static ToolResult? Failure(SourceValidationResult validation)
    {
        if (!validation.Performed) return validation.Error is null
            ? null
            : ToolResult.Fail("syntax_validation_unavailable", validation.Error, Metadata(validation));
        if (validation.Success) return null;
        var diagnostics = validation.Diagnostics.Take(8).Select(item => item.Line is null ? item.Message : $"{item.Line}:{item.Column}: {item.Message}");
        return ToolResult.Fail("syntax_validation_failed", string.Join(Environment.NewLine, diagnostics), Metadata(validation));
    }
}
