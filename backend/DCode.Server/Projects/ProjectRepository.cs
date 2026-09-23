using System.Text.Json;
using DCode.Server.Storage;

namespace DCode.Server.Projects;

public sealed class ProjectRepository(DCodeDatabase database)
{
    private const string ActiveProjectKey = "active_project_id";

    public IReadOnlyList<ProjectInfo> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, path
            FROM projects
            ORDER BY last_opened_utc DESC;
            """;

        using var reader = command.ExecuteReader();
        var projects = new List<ProjectInfo>();

        while (reader.Read())
        {
            projects.Add(ReadProject(reader));
        }

        return projects;
    }

    public ProjectInfo? GetById(string id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, path
            FROM projects
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProject(reader) : null;
    }

    public ProjectInfo? GetByNormalizedPath(string normalizedPath)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, path
            FROM projects
            WHERE normalized_path = $normalizedPath;
            """;
        command.Parameters.AddWithValue("$normalizedPath", normalizedPath);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProject(reader) : null;
    }

    public void Save(ProjectInfo project, string normalizedPath)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var timestamp = DateTimeOffset.UtcNow.ToString("O");

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO projects (
                    id, name, path, normalized_path,
                    created_utc, last_opened_utc
                ) VALUES (
                    $id, $name, $path, $normalizedPath,
                    $timestamp, $timestamp
                )
                ON CONFLICT(normalized_path) DO UPDATE SET
                    name = excluded.name,
                    path = excluded.path,
                    last_opened_utc = excluded.last_opened_utc;
                """;
            command.Parameters.AddWithValue("$id", project.Id);
            command.Parameters.AddWithValue("$name", project.Name);
            command.Parameters.AddWithValue("$path", project.Path);
            command.Parameters.AddWithValue("$normalizedPath", normalizedPath);
            command.Parameters.AddWithValue("$timestamp", timestamp);
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO app_settings (key, value_json, updated_utc)
                VALUES ($key, $value, $timestamp)
                ON CONFLICT(key) DO UPDATE SET
                    value_json = excluded.value_json,
                    updated_utc = excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$key", ActiveProjectKey);
            command.Parameters.AddWithValue(
                "$value",
                JsonSerializer.Serialize(project.Id)
            );
            command.Parameters.AddWithValue("$timestamp", timestamp);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public ProjectInfo? GetActive()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.name, p.path
            FROM app_settings AS settings
            JOIN projects AS p
                ON p.id = json_extract(settings.value_json, '$')
            WHERE settings.key = $key;
            """;
        command.Parameters.AddWithValue("$key", ActiveProjectKey);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProject(reader) : null;
    }

    private static ProjectInfo ReadProject(
        Microsoft.Data.Sqlite.SqliteDataReader reader
    )
    {
        return new ProjectInfo(
            Id: reader.GetString(0),
            Name: reader.GetString(1),
            Path: reader.GetString(2)
        );
    }
}
