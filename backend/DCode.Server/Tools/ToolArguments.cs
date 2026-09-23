using System.Text.Json;

namespace DCode.Server.Tools;

internal static class ToolArguments
{
    public static string RequiredString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException($"Argument '{name}' must be a non-empty string.");
        }
        return value.GetString()!;
    }

    public static string? OptionalString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException($"Argument '{name}' must be a string.");
        return value.GetString();
    }

    public static bool OptionalBoolean(JsonElement arguments, string name, bool defaultValue = false)
    {
        if (!arguments.TryGetProperty(name, out var value)) return defaultValue;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException($"Argument '{name}' must be a boolean.");
        return value.GetBoolean();
    }

    public static int OptionalInteger(JsonElement arguments, string name, int defaultValue)
    {
        if (!arguments.TryGetProperty(name, out var value)) return defaultValue;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result)) throw new ArgumentException($"Argument '{name}' must be an integer.");
        return result;
    }

    public static IReadOnlyList<string> OptionalStringArray(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array) throw new ArgumentException($"Argument '{name}' must be an array of strings.");
        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw new ArgumentException($"Argument '{name}' must be an array of strings.");
            result.Add(item.GetString()!);
        }
        return result;
    }
}
