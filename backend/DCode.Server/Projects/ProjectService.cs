namespace DCode.Server.Projects;

public sealed class ProjectService(ProjectRepository projectRepository)
{
    private static readonly HashSet<string> IgnoredDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".next",
            "node_modules",
            "bin",
            "obj",
            "dist",
            "coverage"
        };

    public ProjectInfo CreateProject(string path)
    {
        var normalizedPath = Path.GetFullPath(path);

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException(
                $"Project directory not found: {normalizedPath}"
            );
        }

        var pathKey = normalizedPath.ToUpperInvariant();
        var existing = projectRepository.GetByNormalizedPath(pathKey);

        if (existing is not null)
        {
            projectRepository.Save(existing, pathKey);
            return existing;
        }

        var project = new ProjectInfo(
            Id: Guid.NewGuid().ToString("N"),
            Name: new DirectoryInfo(normalizedPath).Name,
            Path: normalizedPath
        );

        projectRepository.Save(project, pathKey);

        return project;
    }

    public IReadOnlyList<ProjectInfo> GetProjects()
    {
        return projectRepository.GetAll();
    }

    public ProjectInfo? GetActiveProject()
    {
        return projectRepository.GetActive();
    }

    public ProjectInfo GetProject(string id)
    {
        return projectRepository.GetById(id)
            ?? throw new KeyNotFoundException(
                $"Project not found: {id}"
            );
    }

    public IReadOnlyList<ProjectTreeNode> GetTree(
        string projectId
    )
    {
        var project = GetProject(projectId);

        return BuildTree(
            rootPath: project.Path,
            currentPath: project.Path
        );
    }

    public async Task<ProjectFile> ReadFileAsync(
        string projectId,
        string relativePath
    )
    {
        var fullPath = ResolveProjectPath(projectId, relativePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Project file not found: {relativePath}",
                fullPath
            );
        }

        var content = await File.ReadAllTextAsync(fullPath);

        return new ProjectFile(
            RelativePath: Path.GetRelativePath(
                GetProject(projectId).Path,
                fullPath
            ),
            Content: content
        );
    }

    public async Task<ProjectFile> WriteFileAsync(
        string projectId,
        WriteProjectFileRequest request
    )
    {
        var fullPath = ResolveProjectPath(
            projectId,
            request.RelativePath
        );

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Project file not found: {request.RelativePath}",
                fullPath
            );
        }

        await File.WriteAllTextAsync(fullPath, request.Content);

        return new ProjectFile(
            RelativePath: Path.GetRelativePath(
                GetProject(projectId).Path,
                fullPath
            ),
            Content: request.Content
        );
    }

    private string ResolveProjectPath(
        string projectId,
        string relativePath
    )
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A relative file path is required.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new UnauthorizedAccessException(
                "Absolute file paths are not allowed."
            );
        }

        var project = GetProject(projectId);
        var projectRoot = Path.GetFullPath(project.Path);
        var fullPath = Path.GetFullPath(
            Path.Combine(projectRoot, relativePath)
        );
        var rootPrefix = projectRoot.EndsWith(
            Path.DirectorySeparatorChar
        )
            ? projectRoot
            : projectRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
            rootPrefix,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            throw new UnauthorizedAccessException(
                "The file path is outside the project."
            );
        }

        return fullPath;
    }

    private static IReadOnlyList<ProjectTreeNode> BuildTree(
        string rootPath,
        string currentPath
    )
    {
        var nodes = new List<ProjectTreeNode>();

        foreach (
            var directoryPath in Directory
                .GetDirectories(currentPath)
                .OrderBy(path => path)
        )
        {
            var directoryName =
                Path.GetFileName(directoryPath);

            if (IgnoredDirectories.Contains(directoryName))
            {
                continue;
            }

            nodes.Add(
                new ProjectTreeNode(
                    Name: directoryName,
                    RelativePath: Path.GetRelativePath(
                        rootPath,
                        directoryPath
                    ),
                    IsDirectory: true,
                    Children: BuildTree(
                        rootPath,
                        directoryPath
                    )
                )
            );
        }

        foreach (
            var filePath in Directory
                .GetFiles(currentPath)
                .OrderBy(path => path)
        )
        {
            nodes.Add(
                new ProjectTreeNode(
                    Name: Path.GetFileName(filePath),
                    RelativePath: Path.GetRelativePath(
                        rootPath,
                        filePath
                    ),
                    IsDirectory: false,
                    Children: null
                )
            );
        }

        return nodes;
    }
}
