namespace DCode.Server.Tools;

internal static class ProjectPathSandbox
{
    public static string Resolve(ToolContext context, string? relativePath, bool allowProjectRoot = true)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(context.ProjectPath));
        var path = string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath;
        if (Path.IsPathRooted(path))
        {
            throw new UnauthorizedAccessException("Absolute paths are not allowed.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The path is outside the active project.");
        }
        if (!allowProjectRoot && relative == ".")
        {
            throw new ArgumentException("A file path is required.");
        }

        RejectReparsePoints(root, fullPath);
        return fullPath;
    }

    private static void RejectReparsePoints(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".") return;

        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) break;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("Paths through symbolic links or junctions are not allowed.");
            }
        }
    }
}
