namespace DCode.Server.Tools.Permissions;

public readonly record struct ToolPermission(string Id)
{
    public static readonly ToolPermission FileSystemRead = new("filesystem.read");
    public static readonly ToolPermission FileSystemWrite = new("filesystem.write");
    public static readonly ToolPermission ProcessExecute = new("process.execute");

    public static bool TryParse(string value, out ToolPermission permission)
    {
        permission = value switch
        {
            "filesystem.read" => FileSystemRead,
            "filesystem.write" => FileSystemWrite,
            "process.execute" => ProcessExecute,
            _ => default
        };
        return permission != default;
    }

    public override string ToString() => Id;
}
