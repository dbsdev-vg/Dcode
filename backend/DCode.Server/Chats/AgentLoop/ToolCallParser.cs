using System.Text.Json;
using DCode.Server.Tools;

namespace DCode.Server.Chats.AgentLoop;

public abstract record ProviderTurnSegment;
public sealed record ProviderText(string Content) : ProviderTurnSegment;
public sealed record ProviderToolCall(ToolCall Call) : ProviderTurnSegment;

public sealed record ProviderTurnParseResult(bool Success, IReadOnlyList<ProviderTurnSegment> Segments, string? ErrorCode = null, string? Error = null, string? ToolIdHint = null)
{
    public ProviderToolCall? ToolCall => Segments.OfType<ProviderToolCall>().SingleOrDefault();
    public IReadOnlyList<ProviderText> Text => Segments.OfType<ProviderText>().ToArray();
}

public sealed class ToolCallParser
{
    public const int MaximumPayloadCharacters = 131_072;
    private const string OpeningTag = "<tool>";
    private const string ClosingTag = "</tool>";

    public ProviderTurnParseResult Parse(string response)
    {
        var content = response.Trim();
        var openingIndex = content.IndexOf(OpeningTag, StringComparison.Ordinal);
        var closingIndex = content.IndexOf(ClosingTag, StringComparison.Ordinal);
        var containsToolMarkup = content.Contains("<tool", StringComparison.OrdinalIgnoreCase) || content.Contains(ClosingTag, StringComparison.OrdinalIgnoreCase);

        if (openingIndex < 0 && closingIndex < 0)
            return containsToolMarkup ? Malformed("tool_call_malformed", "The provider returned malformed tool-call markup.") : new(true, string.IsNullOrWhiteSpace(content) ? [] : [new ProviderText(content)]);

        var secondOpening = openingIndex < 0 ? -1 : content.IndexOf(OpeningTag, openingIndex + OpeningTag.Length, StringComparison.Ordinal);
        var secondClosing = closingIndex < 0 ? -1 : content.IndexOf(ClosingTag, closingIndex + ClosingTag.Length, StringComparison.Ordinal);
        if (openingIndex >= 0 && closingIndex < openingIndex)
            return Malformed("tool_call_truncated", "The provider tool block is missing its closing </tool> tag and appears truncated.", TryReadToolIdHint(content[(openingIndex + OpeningTag.Length)..]));
        if (openingIndex < 0 || secondOpening >= 0 || secondClosing >= 0)
            return Malformed("tool_call_malformed", "The provider response must contain exactly one complete tool block.");

        var jsonContent = content[(openingIndex + OpeningTag.Length)..closingIndex].Trim();
        if (jsonContent.Length > MaximumPayloadCharacters) return Malformed("tool_call_too_large", $"The tool payload exceeds {MaximumPayloadCharacters:N0} characters.", TryReadToolIdHint(jsonContent));
        if (string.IsNullOrWhiteSpace(jsonContent)) return Malformed("tool_call_malformed", "The tool block is empty.");

        ToolCall call;
        try
        {
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || !root.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object)
                return Malformed("tool_call_malformed", "A tool call requires a non-empty name and an arguments object.");
            call = new(name.GetString()!, arguments.Clone());
        }
        catch (JsonException exception) { return Malformed("tool_call_malformed", $"The tool-call JSON is invalid: {exception.Message}", TryReadToolIdHint(jsonContent)); }

        var segments = new List<ProviderTurnSegment>();
        AddText(segments, content[..openingIndex]);
        segments.Add(new ProviderToolCall(call));
        AddText(segments, content[(closingIndex + ClosingTag.Length)..]);
        return new(true, segments);
    }

    private static void AddText(List<ProviderTurnSegment> segments, string value)
    {
        var text = value.Trim();
        if (!string.IsNullOrWhiteSpace(text)) segments.Add(new ProviderText(text));
    }

    private static string? TryReadToolIdHint(string content)
    {
        const string property = "\"name\"";
        var nameIndex = content.IndexOf(property, StringComparison.Ordinal);
        if (nameIndex < 0) return null;
        var colonIndex = content.IndexOf(':', nameIndex + property.Length);
        var quoteIndex = colonIndex < 0 ? -1 : content.IndexOf('"', colonIndex + 1);
        var endQuoteIndex = quoteIndex < 0 ? -1 : content.IndexOf('"', quoteIndex + 1);
        return quoteIndex >= 0 && endQuoteIndex > quoteIndex ? content[(quoteIndex + 1)..endQuoteIndex] : null;
    }

    private static ProviderTurnParseResult Malformed(string code, string error, string? toolIdHint = null) => new(false, [], code, error, toolIdHint);
}
