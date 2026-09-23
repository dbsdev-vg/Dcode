using System.Text.Json;
using DCode.Server.Storage;

namespace DCode.Server.Projects;

public sealed class ProjectSessionRepository(DCodeDatabase database)
{
    private static readonly ProjectSessionLayout DefaultLayout = new(
        ExplorerOpen: true,
        ChatOpen: false,
        TerminalOpen: false
    );

    public ProjectSession Get(string projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT active_file_path, open_tabs_json,
                   explorer_state_json, layout_json
            FROM project_sessions
            WHERE project_id = $projectId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return new ProjectSession(
                ProjectId: projectId,
                ActiveFilePath: null,
                OpenTabs: [],
                ExpandedFolders: [],
                Layout: DefaultLayout
            );
        }

        return new ProjectSession(
            ProjectId: projectId,
            ActiveFilePath: reader.IsDBNull(0) ? null : reader.GetString(0),
            OpenTabs: DeserializeList(reader.GetString(1)),
            ExpandedFolders: DeserializeList(reader.GetString(2)),
            Layout: JsonSerializer.Deserialize<ProjectSessionLayout>(
                reader.GetString(3)
            ) ?? DefaultLayout
        );
    }

    public ProjectSession Save(
        string projectId,
        SaveProjectSessionRequest request
    )
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_sessions (
                project_id,
                active_file_path,
                open_tabs_json,
                explorer_state_json,
                layout_json,
                updated_utc
            ) VALUES (
                $projectId,
                $activeFilePath,
                $openTabs,
                $expandedFolders,
                $layout,
                $updatedUtc
            )
            ON CONFLICT(project_id) DO UPDATE SET
                active_file_path = excluded.active_file_path,
                open_tabs_json = excluded.open_tabs_json,
                explorer_state_json = excluded.explorer_state_json,
                layout_json = excluded.layout_json,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue(
            "$activeFilePath",
            (object?)request.ActiveFilePath ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$openTabs",
            JsonSerializer.Serialize(request.OpenTabs)
        );
        command.Parameters.AddWithValue(
            "$expandedFolders",
            JsonSerializer.Serialize(request.ExpandedFolders)
        );
        command.Parameters.AddWithValue(
            "$layout",
            JsonSerializer.Serialize(request.Layout)
        );
        command.Parameters.AddWithValue(
            "$updatedUtc",
            DateTimeOffset.UtcNow.ToString("O")
        );
        command.ExecuteNonQuery();

        return new ProjectSession(
            ProjectId: projectId,
            ActiveFilePath: request.ActiveFilePath,
            OpenTabs: request.OpenTabs,
            ExpandedFolders: request.ExpandedFolders,
            Layout: request.Layout
        );
    }

    private static IReadOnlyList<string> DeserializeList(string json)
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }
}
