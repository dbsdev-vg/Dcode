using System.Text.Json;

namespace DCode.Server.Tools;

public sealed record ToolCall(string ToolId, JsonElement Arguments);
