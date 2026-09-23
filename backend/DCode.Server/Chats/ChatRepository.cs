using DCode.Server.Providers;
using DCode.Server.Storage;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace DCode.Server.Chats;

public sealed class ChatRepository(DCodeDatabase database)
{
    public IReadOnlyList<ChatConversation> GetAll()
    {
        using var connection = database.OpenConnection();
        using var command = CreateSelectCommand(connection);
        command.CommandText += " ORDER BY c.updated_utc DESC;";
        using var reader = command.ExecuteReader();
        var chats = new List<ChatConversation>();
        while (reader.Read()) chats.Add(Read(reader));
        return chats;
    }

    public ChatConversation Get(string id)
    {
        using var connection = database.OpenConnection();
        using var command = CreateSelectCommand(connection);
        command.CommandText += " WHERE c.id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException($"Chat '{id}' was not found.");
        return Read(reader);
    }

    public ChatConversation Create(BrowserProfile profile)
    {
        if (!profile.Connected)
            throw new InvalidOperationException("Connect the browser account before starting a chat.");

        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chats (
                id, title, provider_type, transport, browser_profile_id,
                status, created_utc, updated_utc
            ) VALUES (
                $id, $title, $providerType, 'browser', $profileId,
                'active', $now, $now
            );
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", $"New {profile.ProviderType} chat");
        command.Parameters.AddWithValue("$providerType", profile.ProviderType);
        command.Parameters.AddWithValue("$profileId", profile.Id);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
        return Get(id);
    }

    public IReadOnlyList<ChatMessage> GetMessages(string chatId)
    {
        Get(chatId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, chat_id, role, content, provider_message_id, created_utc
            FROM chat_messages WHERE chat_id = $chatId ORDER BY created_utc, rowid;
            """;
        command.Parameters.AddWithValue("$chatId", chatId);
        using var reader = command.ExecuteReader();
        var messages = new List<ChatMessage>();
        while (reader.Read())
            messages.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5))));
        return messages;
    }

    public ChatConversation? GetProjectChat(string projectId)
    {
        using var connection = database.OpenConnection();
        using var command = CreateSelectCommand(connection);
        command.CommandText += " JOIN project_chat_sessions pcs ON pcs.chat_id = c.id WHERE pcs.project_id = $projectId;";
        command.Parameters.AddWithValue("$projectId", projectId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public ProjectConversationCollection GetProjectChats(string projectId, bool includeArchived = false)
    {
        using var connection = database.OpenConnection();
        string? activeId;
        using (var active = connection.CreateCommand())
        {
            active.CommandText = "SELECT chat_id FROM project_chat_sessions WHERE project_id = $projectId;";
            active.Parameters.AddWithValue("$projectId", projectId);
            activeId = active.ExecuteScalar() as string;
        }
        using var command = CreateSelectCommand(connection);
        command.CommandText += " JOIN project_conversations pc ON pc.chat_id = c.id WHERE pc.project_id = $projectId";
        command.CommandText += " AND pc.kind = 'user'";
        if (!includeArchived) command.CommandText += " AND pc.archived = 0";
        command.CommandText += " ORDER BY c.updated_utc DESC;";
        command.Parameters.AddWithValue("$projectId", projectId);
        using var reader = command.ExecuteReader();
        var conversations = new List<ChatConversation>();
        while (reader.Read()) conversations.Add(Read(reader));
        if (activeId is not null && conversations.All(chat => chat.Id != activeId)) activeId = null;
        return new(activeId, conversations);
    }

    public ChatConversation CreateProjectChat(string projectId, BrowserProfile profile)
    {
        var chat = Create(profile);
        using var connection = database.OpenConnection();
        var title = NextProjectConversationTitle(connection, projectId);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_conversations (project_id, chat_id, kind, archived, created_utc, updated_utc, agent_id)
            VALUES ($projectId, $chatId, 'user', 0, $now, $now,
                (SELECT id FROM project_agents WHERE project_id=$projectId AND is_lead=1));
            UPDATE chats SET title=$title WHERE id=$chatId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$chatId", chat.Id);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$title", title);
        try { command.ExecuteNonQuery(); }
        catch { Delete(chat.Id); throw; }
        SetProjectChat(projectId, chat.Id);
        return Get(chat.Id);
    }

    public ChatConversation CreateAgentChat(string projectId, string agentId, string agentName, BrowserProfile profile)
    {
        var chat = Create(profile); var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = database.OpenConnection(); using var transaction = connection.BeginTransaction();
        using (var membership = connection.CreateCommand())
        {
            membership.Transaction = transaction;
            membership.CommandText = "INSERT INTO project_conversations(project_id,chat_id,kind,archived,created_utc,updated_utc,agent_id) VALUES($project,$chat,'agent',0,$now,$now,$agent); UPDATE chats SET title=$title WHERE id=$chat;";
            membership.Parameters.AddWithValue("$project",projectId);membership.Parameters.AddWithValue("$chat",chat.Id);membership.Parameters.AddWithValue("$agent",agentId);membership.Parameters.AddWithValue("$title",$"{agentName} internal session");membership.Parameters.AddWithValue("$now",now);membership.ExecuteNonQuery();
        }
        transaction.Commit(); return Get(chat.Id);
    }

    public void SetProjectChat(string projectId, string chatId)
    {
        Get(chatId);
        using var connection = database.OpenConnection();
        using (var membership = connection.CreateCommand())
        {
            membership.CommandText = "SELECT COUNT(*) FROM project_conversations WHERE project_id = $projectId AND chat_id = $chatId AND archived = 0;";
            membership.Parameters.AddWithValue("$projectId", projectId);
            membership.Parameters.AddWithValue("$chatId", chatId);
            if (Convert.ToInt32(membership.ExecuteScalar()) == 0)
                throw new InvalidOperationException("This conversation does not belong to the requested project.");
        }
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_chat_sessions (project_id, chat_id, updated_utc)
            VALUES ($projectId, $chatId, $now)
            ON CONFLICT(project_id) DO UPDATE SET chat_id = excluded.chat_id, updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$chatId", chatId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public ChatConversation RenameProjectChat(string projectId, string chatId, string title)
    {
        var normalized = NormalizeTitle(title);
        EnsureProjectMembership(projectId, chatId);
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE chats SET title=$title, updated_utc=$now WHERE id=$chatId; UPDATE project_conversations SET updated_utc=$now WHERE project_id=$projectId AND chat_id=$chatId;";
        command.Parameters.AddWithValue("$title", normalized); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$projectId", projectId); command.Parameters.AddWithValue("$chatId", chatId); command.ExecuteNonQuery();
        return Get(chatId);
    }

    public static string NormalizeTitle(string title)
    {
        var normalized = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("Conversation title is required.");
        if (normalized.Length > 80) throw new ArgumentException("Conversation title cannot exceed 80 characters.");
        return normalized;
    }

    private static string NextProjectConversationTitle(Microsoft.Data.Sqlite.SqliteConnection connection, string projectId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT p.name, COUNT(pc.chat_id) FROM projects p LEFT JOIN project_conversations pc ON pc.project_id=p.id AND pc.kind='user' WHERE p.id=$project GROUP BY p.name;";
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException("Project was not found.");
        return $"{reader.GetString(0)} Lead #{reader.GetInt64(1) + 1}";
    }

    public void ArchiveProjectChat(string projectId, string chatId)
    {
        EnsureProjectMembership(projectId, chatId);
        using var connection = database.OpenConnection(); using var transaction = connection.BeginTransaction();
        using (var archive = connection.CreateCommand()) { archive.Transaction = transaction; archive.CommandText = "UPDATE project_conversations SET archived=1, updated_utc=$now WHERE project_id=$projectId AND chat_id=$chatId; UPDATE chats SET status='archived', updated_utc=$now WHERE id=$chatId;"; archive.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); archive.Parameters.AddWithValue("$projectId", projectId); archive.Parameters.AddWithValue("$chatId", chatId); archive.ExecuteNonQuery(); }
        using (var active = connection.CreateCommand()) { active.Transaction = transaction; active.CommandText = "DELETE FROM project_chat_sessions WHERE project_id=$projectId AND chat_id=$chatId;"; active.Parameters.AddWithValue("$projectId", projectId); active.Parameters.AddWithValue("$chatId", chatId); active.ExecuteNonQuery(); }
        transaction.Commit();
    }

    public void DeleteProjectChat(string projectId, string chatId) { EnsureProjectMembership(projectId, chatId); Delete(chatId); }

    private void EnsureProjectMembership(string projectId, string chatId)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM project_conversations WHERE project_id=$projectId AND chat_id=$chatId;"; command.Parameters.AddWithValue("$projectId", projectId); command.Parameters.AddWithValue("$chatId", chatId);
        if (Convert.ToInt32(command.ExecuteScalar()) == 0) throw new KeyNotFoundException("The project conversation was not found.");
    }

    private void Delete(string chatId)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM chats WHERE id=$chatId;"; command.Parameters.AddWithValue("$chatId", chatId); command.ExecuteNonQuery();
    }

    public ProjectChatExecutionState? GetExecutionState(string chatId)
    {
        Get(chatId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, error, progress_json, updated_utc FROM project_chat_execution_state WHERE chat_id = $chatId;";
        command.Parameters.AddWithValue("$chatId", chatId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var progress = JsonSerializer.Deserialize<List<AgentLoop.ConversationProgress>>(reader.GetString(2), JsonOptions) ?? [];
        return new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), progress, DateTimeOffset.Parse(reader.GetString(3)));
    }

    public void SaveExecutionState(string chatId, string status, string? error, IReadOnlyList<AgentLoop.ConversationProgress> progress)
    {
        Get(chatId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_chat_execution_state (chat_id, status, error, progress_json, updated_utc)
            VALUES ($chatId, $status, $error, $progress, $now)
            ON CONFLICT(chat_id) DO UPDATE SET
                status = excluded.status,
                error = excluded.error,
                progress_json = excluded.progress_json,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$chatId", chatId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$progress", JsonSerializer.Serialize(progress, JsonOptions));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ProjectChatExecutionTurn> GetExecutionTurns(string chatId)
    {
        Get(chatId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, chat_id, user_message_id, status, error, events_json, created_utc, updated_utc
            FROM project_chat_execution_turns WHERE chat_id = $chatId ORDER BY created_utc;
            """;
        command.Parameters.AddWithValue("$chatId", chatId);
        using var reader = command.ExecuteReader();
        var turns = new List<ProjectChatExecutionTurn>();
        while (reader.Read())
        {
            var events = JsonSerializer.Deserialize<List<AgentLoop.ConversationProgress>>(reader.GetString(5), JsonOptions) ?? [];
            turns.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), events,
                DateTimeOffset.Parse(reader.GetString(6)), DateTimeOffset.Parse(reader.GetString(7))));
        }
        return turns;
    }

    public void SaveExecutionTurn(string turnId, string chatId, string userMessageId, string status, string? error, IReadOnlyList<AgentLoop.ConversationProgress> events)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_chat_execution_turns (id, chat_id, user_message_id, status, error, events_json, created_utc, updated_utc)
            VALUES ($id, $chatId, $userMessageId, $status, $error, $events, $now, $now)
            ON CONFLICT(id) DO UPDATE SET status = excluded.status, error = excluded.error,
                events_json = excluded.events_json, updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", turnId);
        command.Parameters.AddWithValue("$chatId", chatId);
        command.Parameters.AddWithValue("$userMessageId", userMessageId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$events", JsonSerializer.Serialize(events, JsonOptions));
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ChatExchangeResponse SaveUserMessage(string chatId, string content)
    {
        Get(chatId);
        var now = DateTimeOffset.UtcNow;
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_messages (id, chat_id, role, content, created_utc)
            VALUES ($id, $chatId, 'user', $content, $now);

            UPDATE chats SET
                title = CASE WHEN title LIKE 'New % chat' THEN $title ELSE title END,
                updated_utc = $now
            WHERE id = $chatId;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$chatId", chatId);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$title", CreateTitle(content));
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
        return new(Get(chatId), GetMessages(chatId));
    }

    public ChatExchangeResponse SaveAssistantMessage(string chatId, string content, string conversationUrl)
    {
        Get(chatId);
        var now = DateTimeOffset.UtcNow;
        var providerConversationId = conversationUrl.TrimEnd('/').Split('/').LastOrDefault();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_messages (id, chat_id, role, content, created_utc)
            VALUES ($id, $chatId, 'assistant', $content, $now);

            UPDATE chats SET
                provider_conversation_id = $providerConversationId,
                provider_conversation_url = $providerConversationUrl,
                updated_utc = $now
            WHERE id = $chatId;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$chatId", chatId);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$providerConversationId", (object?)providerConversationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$providerConversationUrl", conversationUrl);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
        return new(Get(chatId), GetMessages(chatId));
    }

    public ChatConversation BindRemoteConversation(string chatId, string conversationUrl)
    {
        var chat = Get(chatId);
        if (string.IsNullOrWhiteSpace(conversationUrl))
            throw new ArgumentException("A remote conversation URL is required.", nameof(conversationUrl));
        if (!string.IsNullOrWhiteSpace(chat.ProviderConversationUrl)
            && !string.Equals(chat.ProviderConversationUrl, conversationUrl, StringComparison.Ordinal))
            throw new InvalidOperationException("This DCode chat is already bound to a different remote conversation.");

        var providerConversationId = conversationUrl.TrimEnd('/').Split('/').LastOrDefault();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE chats SET
                provider_conversation_id = $providerConversationId,
                provider_conversation_url = $providerConversationUrl,
                updated_utc = $now
            WHERE id = $chatId;
            """;
        command.Parameters.AddWithValue("$chatId", chatId);
        command.Parameters.AddWithValue("$providerConversationId", (object?)providerConversationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$providerConversationUrl", conversationUrl);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return Get(chatId);
    }

    public ChatExchangeResponse SaveExchange(string chatId, string userContent, string assistantContent, string conversationUrl)
    {
        var chat = Get(chatId);
        var now = DateTimeOffset.UtcNow;
        var providerConversationId = conversationUrl.TrimEnd('/').Split('/').LastOrDefault();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO chat_messages (id, chat_id, role, content, created_utc)
                VALUES ($userId, $chatId, 'user', $userContent, $now),
                       ($assistantId, $chatId, 'assistant', $assistantContent, $assistantNow);
                """;
            command.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$assistantId", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$userContent", userContent);
            command.Parameters.AddWithValue("$assistantContent", assistantContent);
            command.Parameters.AddWithValue("$now", now.ToString("O"));
            command.Parameters.AddWithValue("$assistantNow", now.AddTicks(1).ToString("O"));
            command.ExecuteNonQuery();
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE chats SET
                    title = CASE WHEN title LIKE 'New % chat' THEN $title ELSE title END,
                    provider_conversation_id = $providerConversationId,
                    provider_conversation_url = $providerConversationUrl,
                    updated_utc = $now
                WHERE id = $chatId;
                """;
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$title", CreateTitle(userContent));
            command.Parameters.AddWithValue("$providerConversationId", (object?)providerConversationId ?? DBNull.Value);
            command.Parameters.AddWithValue("$providerConversationUrl", conversationUrl);
            command.Parameters.AddWithValue("$now", now.ToString("O"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return new(Get(chatId), GetMessages(chatId));
    }

    private static string CreateTitle(string content)
    {
        var singleLine = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= 60 ? singleLine : $"{singleLine[..57]}...";
    }

    private static SqliteCommand CreateSelectCommand(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.title, c.provider_type, c.transport,
                   c.browser_profile_id, bp.profile_name,
                   c.provider_conversation_id, c.provider_conversation_url,
                   c.status, c.created_utc, c.updated_utc
            FROM chats c
            JOIN browser_profiles bp ON bp.id = c.browser_profile_id
            """;
        return command;
    }

    private static ChatConversation Read(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8),
        DateTimeOffset.Parse(reader.GetString(9)), DateTimeOffset.Parse(reader.GetString(10))
    );
}
