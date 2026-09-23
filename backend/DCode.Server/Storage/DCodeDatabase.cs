using Microsoft.Data.Sqlite;

namespace DCode.Server.Storage;

public sealed class DCodeDatabase
{
    public string DatabasePath { get; }

    private readonly string _connectionString;

    public DCodeDatabase(IConfiguration configuration)
    {
        var configuredPath = configuration["Storage:DatabasePath"];

        DatabasePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData
                ),
                "DCode",
                "dcode.db"
            )
            : Path.GetFullPath(configuredPath);

        var directory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException(
                "DCode database path has no parent directory."
            );

        Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS schema_info (
                version INTEGER NOT NULL
            );

            INSERT INTO schema_info (version)
            SELECT 1
            WHERE NOT EXISTS (SELECT 1 FROM schema_info);

            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                path TEXT NOT NULL,
                normalized_path TEXT NOT NULL UNIQUE,
                created_utc TEXT NOT NULL,
                last_opened_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS project_sessions (
                project_id TEXT PRIMARY KEY,
                active_file_path TEXT,
                open_tabs_json TEXT NOT NULL DEFAULT '[]',
                explorer_state_json TEXT NOT NULL DEFAULT '{}',
                layout_json TEXT NOT NULL DEFAULT '{}',
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects(id)
                    ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS provider_configs (
                id TEXT PRIMARY KEY,
                provider_type TEXT NOT NULL,
                transport TEXT NOT NULL,
                display_name TEXT NOT NULL,
                settings_json TEXT NOT NULL DEFAULT '{}',
                credential_reference TEXT,
                status TEXT NOT NULL DEFAULT 'disconnected',
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS browser_profiles (
                id TEXT PRIMARY KEY,
                provider_config_id TEXT NOT NULL,
                profile_name TEXT NOT NULL,
                user_data_directory TEXT NOT NULL,
                browser_channel TEXT NOT NULL,
                headless INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (provider_config_id) REFERENCES provider_configs(id)
                    ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS browser_profile_connections (
                browser_profile_id TEXT PRIMARY KEY,
                connection_status TEXT NOT NULL DEFAULT 'disconnected',
                verified_utc TEXT,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (browser_profile_id) REFERENCES browser_profiles(id)
                    ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS chats (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                provider_type TEXT NOT NULL,
                transport TEXT NOT NULL,
                browser_profile_id TEXT NOT NULL,
                provider_conversation_id TEXT,
                provider_conversation_url TEXT,
                status TEXT NOT NULL DEFAULT 'active',
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (browser_profile_id) REFERENCES browser_profiles(id)
                    ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS chat_messages (
                id TEXT PRIMARY KEY,
                chat_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                provider_message_id TEXT,
                created_utc TEXT NOT NULL,
                FOREIGN KEY (chat_id) REFERENCES chats(id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_chats_updated_utc
                ON chats(updated_utc DESC);

            UPDATE schema_info SET version = 2 WHERE version < 2;

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value_json TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tool_permission_policies (
                tool_id TEXT PRIMARY KEY,
                policy TEXT NOT NULL CHECK (policy IN ('allow', 'ask', 'deny')),
                updated_utc TEXT NOT NULL
            );

            INSERT OR IGNORE INTO tool_permission_policies (tool_id, policy, updated_utc) VALUES
                ('filesystem.read', 'allow', CURRENT_TIMESTAMP),
                ('filesystem.list', 'allow', CURRENT_TIMESTAMP),
                ('filesystem.search', 'allow', CURRENT_TIMESTAMP),
                ('filesystem.write', 'ask', CURRENT_TIMESTAMP),
                ('filesystem.edit', 'ask', CURRENT_TIMESTAMP),
                ('filesystem.insert', 'ask', CURRENT_TIMESTAMP),
                ('filesystem.patch', 'ask', CURRENT_TIMESTAMP),
                ('process.run', 'ask', CURRENT_TIMESTAMP);

            UPDATE schema_info SET version = 3 WHERE version < 3;

            CREATE TABLE IF NOT EXISTS project_chat_sessions (
                project_id TEXT PRIMARY KEY,
                chat_id TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (chat_id) REFERENCES chats(id) ON DELETE CASCADE
            );

            UPDATE schema_info SET version = 4 WHERE version < 4;

            CREATE TABLE IF NOT EXISTS project_conversations (
                project_id TEXT NOT NULL,
                chat_id TEXT NOT NULL UNIQUE,
                kind TEXT NOT NULL DEFAULT 'user',
                archived INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY (project_id, chat_id),
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (chat_id) REFERENCES chats(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_project_conversations_project
                ON project_conversations(project_id, archived, updated_utc DESC);

            INSERT OR IGNORE INTO project_conversations (
                project_id, chat_id, kind, archived, created_utc, updated_utc
            )
            SELECT project_id, chat_id, 'user', 0, updated_utc, updated_utc
            FROM project_chat_sessions;

            CREATE TABLE IF NOT EXISTS project_chat_execution_state (
                chat_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                error TEXT,
                progress_json TEXT NOT NULL DEFAULT '[]',
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (chat_id) REFERENCES chats(id) ON DELETE CASCADE
            );

            UPDATE schema_info SET version = 5 WHERE version < 5;

            CREATE TABLE IF NOT EXISTS project_chat_execution_turns (
                id TEXT PRIMARY KEY,
                chat_id TEXT NOT NULL,
                user_message_id TEXT NOT NULL,
                status TEXT NOT NULL,
                error TEXT,
                events_json TEXT NOT NULL DEFAULT '[]',
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (chat_id) REFERENCES chats(id) ON DELETE CASCADE,
                FOREIGN KEY (user_message_id) REFERENCES chat_messages(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_project_chat_execution_turns_chat
                ON project_chat_execution_turns(chat_id, created_utc);

            INSERT INTO project_chat_execution_turns (
                id, chat_id, user_message_id, status, error, events_json, created_utc, updated_utc
            )
            SELECT lower(hex(randomblob(16))), state.chat_id,
                (SELECT message.id FROM chat_messages message
                 WHERE message.chat_id = state.chat_id AND message.role = 'user'
                 ORDER BY message.created_utc DESC, message.rowid DESC LIMIT 1),
                state.status, state.error, state.progress_json, state.updated_utc, state.updated_utc
            FROM project_chat_execution_state state
            WHERE EXISTS (
                SELECT 1 FROM chat_messages message
                WHERE message.chat_id = state.chat_id AND message.role = 'user'
            )
            AND NOT EXISTS (
                SELECT 1 FROM project_chat_execution_turns turn_state
                WHERE turn_state.chat_id = state.chat_id
            );

            UPDATE schema_info SET version = 6 WHERE version < 6;

            CREATE TABLE IF NOT EXISTS project_chat_pending_tool_calls (
                run_id TEXT NOT NULL,
                tool_call_id TEXT NOT NULL,
                event_id TEXT NOT NULL,
                chat_id TEXT NOT NULL,
                user_message_id TEXT NOT NULL,
                project_id TEXT NOT NULL,
                tool_id TEXT NOT NULL,
                arguments_json TEXT NOT NULL,
                required_permission TEXT NOT NULL,
                conversation_reference TEXT NOT NULL,
                next_iteration INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                created_utc TEXT NOT NULL,
                PRIMARY KEY (run_id, tool_call_id),
                FOREIGN KEY (chat_id) REFERENCES chats(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_pending_tool_calls_chat ON project_chat_pending_tool_calls(chat_id, status);
            UPDATE schema_info SET version = 7 WHERE version < 7;
            UPDATE schema_info SET version = 9 WHERE version < 9;

            CREATE TABLE IF NOT EXISTS project_agents (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                name TEXT NOT NULL,
                role TEXT NOT NULL,
                instructions TEXT NOT NULL DEFAULT '',
                browser_profile_id TEXT,
                model TEXT,
                permissions_json TEXT NOT NULL DEFAULT '{}',
                enabled INTEGER NOT NULL DEFAULT 1,
                is_lead INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (browser_profile_id) REFERENCES browser_profiles(id) ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS ix_project_agents_project ON project_agents(project_id, is_lead DESC, created_utc);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_project_agents_lead ON project_agents(project_id) WHERE is_lead = 1;
            UPDATE schema_info SET version = 10 WHERE version < 10;

            CREATE TABLE IF NOT EXISTS agent_runs (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                agent_id TEXT NOT NULL,
                conversation_id TEXT,
                task_id TEXT,
                parent_run_id TEXT,
                trigger TEXT NOT NULL,
                objective TEXT NOT NULL,
                status TEXT NOT NULL,
                provider_type TEXT,
                transport TEXT,
                provider_account_id TEXT,
                model TEXT,
                user_message_id TEXT,
                error TEXT,
                events_json TEXT NOT NULL DEFAULT '[]',
                created_utc TEXT NOT NULL,
                started_utc TEXT,
                completed_utc TEXT,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (agent_id) REFERENCES project_agents(id) ON DELETE RESTRICT,
                FOREIGN KEY (conversation_id) REFERENCES chats(id) ON DELETE SET NULL,
                FOREIGN KEY (parent_run_id) REFERENCES agent_runs(id) ON DELETE SET NULL,
                FOREIGN KEY (provider_account_id) REFERENCES browser_profiles(id) ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS ix_agent_runs_project ON agent_runs(project_id, created_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_agent_runs_conversation ON agent_runs(conversation_id, created_utc);
            UPDATE schema_info SET version = 12 WHERE version < 12;

            CREATE TABLE IF NOT EXISTS agent_sessions (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                agent_id TEXT NOT NULL,
                conversation_id TEXT NOT NULL UNIQUE,
                status TEXT NOT NULL DEFAULT 'active',
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(project_id, agent_id),
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY(agent_id) REFERENCES project_agents(id) ON DELETE CASCADE,
                FOREIGN KEY(conversation_id) REFERENCES chats(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_agent_sessions_project ON agent_sessions(project_id, agent_id);
            UPDATE schema_info SET version = 13 WHERE version < 13;

            CREATE TABLE IF NOT EXISTS run_configurations (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                name TEXT NOT NULL,
                command TEXT NOT NULL,
                arguments_json TEXT NOT NULL DEFAULT '[]',
                working_directory TEXT NOT NULL DEFAULT '.',
                is_detected INTEGER NOT NULL DEFAULT 0,
                archived INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_run_configurations_project ON run_configurations(project_id, name);

            CREATE TABLE IF NOT EXISTS run_executions (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                configuration_id TEXT NOT NULL,
                status TEXT NOT NULL,
                command TEXT NOT NULL,
                arguments_json TEXT NOT NULL,
                working_directory TEXT NOT NULL,
                stdout TEXT NOT NULL DEFAULT '',
                stderr TEXT NOT NULL DEFAULT '',
                exit_code INTEGER,
                error TEXT,
                created_utc TEXT NOT NULL,
                started_utc TEXT NOT NULL,
                completed_utc TEXT,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY(configuration_id) REFERENCES run_configurations(id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS ix_run_executions_project ON run_executions(project_id, created_utc DESC);
            UPDATE schema_info SET version = 14 WHERE version < 14;
            """;

        command.ExecuteNonQuery();
        using(var abandoned=connection.CreateCommand())
        {
            abandoned.CommandText="""
                UPDATE agent_runs
                SET status='interrupted',
                    error='DCode stopped before this Agent run completed.',
                    events_json=replace(events_json, '"status":"running"', '"status":"cancelled"'),
                    completed_utc=CURRENT_TIMESTAMP,
                    updated_utc=CURRENT_TIMESTAMP
                WHERE status='running';
                """;
            abandoned.ExecuteNonQuery();
        }
        EnsurePendingApprovalEventId(connection);
        EnsureProjectConversationAgentId(connection);
        MigrateExecutionTurnsToAgentRuns(connection);
    }

    private static void MigrateExecutionTurnsToAgentRuns(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO agent_runs (
                id, project_id, agent_id, conversation_id, trigger, objective, status,
                provider_type, transport, provider_account_id, user_message_id, error,
                events_json, created_utc, started_utc, completed_utc, updated_utc
            )
            SELECT turns.id, membership.project_id, membership.agent_id, turns.chat_id,
                'project_chat', COALESCE(message.content, ''), turns.status,
                chats.provider_type, chats.transport, chats.browser_profile_id,
                turns.user_message_id, turns.error, turns.events_json, turns.created_utc,
                turns.created_utc,
                CASE WHEN turns.status IN ('completed','failed','cancelled','provider_error') THEN turns.updated_utc ELSE NULL END,
                turns.updated_utc
            FROM project_chat_execution_turns turns
            JOIN project_conversations membership ON membership.chat_id=turns.chat_id
            JOIN chats ON chats.id=turns.chat_id
            LEFT JOIN chat_messages message ON message.id=turns.user_message_id
            WHERE membership.agent_id IS NOT NULL;
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsurePendingApprovalEventId(SqliteConnection connection)
    {
        using var check = connection.CreateCommand(); check.CommandText = "PRAGMA table_info(project_chat_pending_tool_calls);";
        using var reader = check.ExecuteReader(); var exists = false;
        while (reader.Read()) if (string.Equals(reader.GetString(1), "event_id", StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
        reader.Close();
        if (!exists)
        {
            using var alter = connection.CreateCommand(); alter.CommandText = "ALTER TABLE project_chat_pending_tool_calls ADD COLUMN event_id TEXT NOT NULL DEFAULT '';"; alter.ExecuteNonQuery();
        }
        using var migrate = connection.CreateCommand();
        migrate.CommandText = "UPDATE project_chat_pending_tool_calls SET event_id = replace(tool_call_id, ':call:', ':permission-required:') WHERE event_id = ''; UPDATE schema_info SET version = 8 WHERE version < 8;";
        migrate.ExecuteNonQuery();
    }

    private static void EnsureProjectConversationAgentId(SqliteConnection connection)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(project_conversations);";
        using var reader = check.ExecuteReader(); var exists = false;
        while (reader.Read()) if (string.Equals(reader.GetString(1), "agent_id", StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
        reader.Close();
        if (!exists)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE project_conversations ADD COLUMN agent_id TEXT;";
            alter.ExecuteNonQuery();
        }
        using var version = connection.CreateCommand();
        version.CommandText = "UPDATE schema_info SET version = 11 WHERE version < 11;";
        version.ExecuteNonQuery();
    }
}
