namespace DCode.Server.Projects;

public sealed record OpenProjectRequest(
    string Path
);

public sealed record ProjectInfo(
    string Id,
    string Name,
    string Path
);
public sealed record ProjectTreeNode(
    string Name,
    string RelativePath,
    bool IsDirectory,
    IReadOnlyList<ProjectTreeNode>? Children
);

public sealed record ProjectFile(
    string RelativePath,
    string Content
);

public sealed record WriteProjectFileRequest(
    string RelativePath,
    string Content
);

public sealed record ProjectSessionLayout(
    bool ExplorerOpen,
    bool ChatOpen,
    bool TerminalOpen
);

public sealed record ProjectSession(
    string ProjectId,
    string? ActiveFilePath,
    IReadOnlyList<string> OpenTabs,
    IReadOnlyList<string> ExpandedFolders,
    ProjectSessionLayout Layout
);

public sealed record SaveProjectSessionRequest(
    string? ActiveFilePath,
    IReadOnlyList<string> OpenTabs,
    IReadOnlyList<string> ExpandedFolders,
    ProjectSessionLayout Layout
);
