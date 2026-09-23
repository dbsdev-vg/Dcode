using System.Text.Json;
using DCode.Server.Storage;

namespace DCode.Server.Agents;

public sealed class ProjectAgentRepository(DCodeDatabase database)
{
    public IReadOnlyList<ProjectAgent> GetForProject(string projectId)
    {
        EnsureDefaults(projectId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE a.project_id=$projectId ORDER BY a.is_lead DESC, a.created_utc;";
        command.Parameters.AddWithValue("$projectId", projectId);
        using var reader = command.ExecuteReader(); var result = new List<ProjectAgent>();
        while (reader.Read()) result.Add(Read(reader));
        return result;
    }

    public ProjectAgent GetLead(string projectId)
    {
        EnsureDefaults(projectId);
        return GetForProject(projectId).Single(agent => agent.IsLead);
    }

    public ProjectAgent GetConversationOwner(string projectId, string chatId)
    {
        EnsureDefaults(projectId);
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT agent_id FROM project_conversations WHERE project_id=$project AND chat_id=$chat;";
        command.Parameters.AddWithValue("$project", projectId); command.Parameters.AddWithValue("$chat", chatId);
        var id = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("This project conversation has no Agent owner.");
        return Get(projectId, id);
    }

    public ProjectAgent GetByRole(string projectId, string role)
    {
        EnsureDefaults(projectId);
        var agent = GetForProject(projectId).FirstOrDefault(item => string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase));
        return agent ?? throw new KeyNotFoundException($"No '{role}' Agent is assigned to this project.");
    }

    public ProjectAgent Create(string projectId, CreateProjectAgentRequest request)
    {
        var name = Required(request.Name, "Agent name"); var role = Required(request.Role, "Agent role").ToLowerInvariant();
        ValidateProfile(request.BrowserProfileId);
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO project_agents (id,project_id,name,role,instructions,browser_profile_id,model,enabled,is_lead,created_utc,updated_utc) VALUES ($id,$project,$name,$role,$instructions,$profile,$model,$enabled,0,$now,$now);";
        Bind(command, id, projectId, name, role, request.Instructions, request.BrowserProfileId, request.Model, request.Enabled, now); command.ExecuteNonQuery();
        return Get(projectId, id);
    }

    public ProjectAgent Update(string projectId, string agentId, UpdateProjectAgentRequest request)
    {
        var name = Required(request.Name, "Agent name"); ValidateProfile(request.BrowserProfileId); var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE project_agents SET name=$name,instructions=$instructions,browser_profile_id=$profile,model=$model,enabled=$enabled,updated_utc=$now WHERE id=$id AND project_id=$project;";
        Bind(command, agentId, projectId, name, "", request.Instructions, request.BrowserProfileId, request.Model, request.Enabled, now);
        if (command.ExecuteNonQuery() == 0) throw new KeyNotFoundException($"Agent '{agentId}' was not found in this project.");
        return Get(projectId, agentId);
    }

    public void EnsureDefaults(string projectId)
    {
        using var connection = database.OpenConnection(); using var transaction = connection.BeginTransaction(); var now = DateTimeOffset.UtcNow.ToString("O");
        InsertDefault(connection, transaction, projectId, "Lead", "lead", true, now);
        InsertDefault(connection, transaction, projectId, "Coder", "coder", false, now);
        using var backfill = connection.CreateCommand(); backfill.Transaction = transaction;
        backfill.CommandText = "UPDATE project_conversations SET agent_id=(SELECT id FROM project_agents WHERE project_id=$project AND is_lead=1) WHERE project_id=$project AND agent_id IS NULL;";
        backfill.Parameters.AddWithValue("$project", projectId); backfill.ExecuteNonQuery();
        using var migrateRuns = connection.CreateCommand(); migrateRuns.Transaction = transaction;
        migrateRuns.CommandText = """
            INSERT OR IGNORE INTO agent_runs (
                id,project_id,agent_id,conversation_id,trigger,objective,status,
                provider_type,transport,provider_account_id,user_message_id,error,
                events_json,created_utc,started_utc,completed_utc,updated_utc
            )
            SELECT turns.id,membership.project_id,membership.agent_id,turns.chat_id,
                'project_chat',COALESCE(message.content,''),turns.status,chats.provider_type,
                chats.transport,chats.browser_profile_id,turns.user_message_id,turns.error,
                turns.events_json,turns.created_utc,turns.created_utc,
                CASE WHEN turns.status IN ('completed','failed','cancelled','provider_error') THEN turns.updated_utc ELSE NULL END,
                turns.updated_utc
            FROM project_chat_execution_turns turns
            JOIN project_conversations membership ON membership.chat_id=turns.chat_id
            JOIN chats ON chats.id=turns.chat_id
            LEFT JOIN chat_messages message ON message.id=turns.user_message_id
            WHERE membership.project_id=$project AND membership.agent_id IS NOT NULL;
            """;
        migrateRuns.Parameters.AddWithValue("$project", projectId); migrateRuns.ExecuteNonQuery(); transaction.Commit();
    }

    private ProjectAgent Get(string projectId, string id)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE a.project_id=$project AND a.id=$id;"; command.Parameters.AddWithValue("$project", projectId); command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader(); if (!reader.Read()) throw new KeyNotFoundException($"Agent '{id}' was not found in this project."); return Read(reader);
    }

    private void ValidateProfile(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM browser_profiles WHERE id=$id;"; command.Parameters.AddWithValue("$id", id);
        if ((long)(command.ExecuteScalar() ?? 0L) == 0) throw new ArgumentException("The selected provider account does not exist.");
    }

    private static void InsertDefault(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction, string projectId, string name, string role, bool lead, string now)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO project_agents (id,project_id,name,role,instructions,enabled,is_lead,created_utc,updated_utc) SELECT $id,$project,$name,$role,'',1,$lead,$now,$now WHERE NOT EXISTS (SELECT 1 FROM project_agents WHERE project_id=$project AND role=$role);";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N")); command.Parameters.AddWithValue("$project", projectId); command.Parameters.AddWithValue("$name", name); command.Parameters.AddWithValue("$role", role); command.Parameters.AddWithValue("$lead", lead); command.Parameters.AddWithValue("$now", now); command.ExecuteNonQuery();
    }

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, string id, string project, string name, string role, string? instructions, string? profile, string? model, bool enabled, string now)
    { command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$project", project); command.Parameters.AddWithValue("$name", name); command.Parameters.AddWithValue("$role", role); command.Parameters.AddWithValue("$instructions", (instructions ?? "").Trim()); command.Parameters.AddWithValue("$profile", (object?)profile ?? DBNull.Value); command.Parameters.AddWithValue("$model", string.IsNullOrWhiteSpace(model) ? DBNull.Value : model.Trim()); command.Parameters.AddWithValue("$enabled", enabled); command.Parameters.AddWithValue("$now", now); }
    private static string Required(string value, string label) { var result = value.Trim(); if (result.Length == 0) throw new ArgumentException($"{label} is required."); if (result.Length > 80) throw new ArgumentException($"{label} must be 80 characters or fewer."); return result; }
    private static ProjectAgent Read(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.IsDBNull(8)?null:r.GetString(8),JsonDocument.Parse(r.GetString(9)).RootElement.Clone(),r.GetBoolean(10),r.GetBoolean(11),r.GetString(12),r.GetString(13));
    private const string SelectSql = "SELECT a.id,a.project_id,a.name,a.role,a.instructions,a.browser_profile_id,pc.provider_type,bp.profile_name,a.model,a.permissions_json,a.enabled,a.is_lead,a.created_utc,a.updated_utc FROM project_agents a LEFT JOIN browser_profiles bp ON bp.id=a.browser_profile_id LEFT JOIN provider_configs pc ON pc.id=bp.provider_config_id";
}
