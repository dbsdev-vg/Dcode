namespace DCode.Server.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (string.IsNullOrWhiteSpace(tool.Definition.Id))
        {
            throw new ArgumentException("A tool ID is required.", nameof(tool));
        }

        lock (_gate)
        {
            if (!_tools.TryAdd(tool.Definition.Id, tool))
            {
                throw new InvalidOperationException($"A tool with ID '{tool.Definition.Id}' is already registered.");
            }
        }
    }

    public bool TryGet(string id, out ITool? tool)
    {
        lock (_gate) return _tools.TryGetValue(id, out tool);
    }

    public ITool Get(string id) => TryGet(id, out var tool)
        ? tool!
        : throw new KeyNotFoundException($"Unknown tool: {id}");

    public IReadOnlyList<ToolDefinition> GetDefinitions()
    {
        lock (_gate)
        {
            return _tools.Values.Select(tool => tool.Definition).OrderBy(definition => definition.Id).ToArray();
        }
    }
}
