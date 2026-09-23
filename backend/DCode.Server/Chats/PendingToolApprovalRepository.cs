using System.Text.Json;
using DCode.Server.Storage;
using DCode.Server.Tools;

namespace DCode.Server.Chats;

public sealed record PendingToolApproval(string RunId, string ToolCallId, string ChatId, string UserMessageId, string ProjectId, string ToolId, JsonElement Arguments, string RequiredPermission, string ConversationReference, int NextIteration, string Status, DateTimeOffset CreatedAt, string? EventId = null);

public sealed class PendingToolApprovalRepository(DCodeDatabase database)
{
    public void Save(PendingToolApproval pending)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_chat_pending_tool_calls (run_id, tool_call_id, event_id, chat_id, user_message_id, project_id, tool_id, arguments_json, required_permission, conversation_reference, next_iteration, status, created_utc)
            VALUES ($run,$call,$event,$chat,$message,$project,$tool,$arguments,$permission,$reference,$iteration,'pending',$created)
            ON CONFLICT(run_id, tool_call_id) DO UPDATE SET status='pending', event_id=excluded.event_id, arguments_json=excluded.arguments_json;
            """;
        command.Parameters.AddWithValue("$run", pending.RunId); command.Parameters.AddWithValue("$call", pending.ToolCallId); command.Parameters.AddWithValue("$event", pending.EventId ?? EventIdFor(pending.ToolCallId)); command.Parameters.AddWithValue("$chat", pending.ChatId); command.Parameters.AddWithValue("$message", pending.UserMessageId); command.Parameters.AddWithValue("$project", pending.ProjectId); command.Parameters.AddWithValue("$tool", pending.ToolId); command.Parameters.AddWithValue("$arguments", pending.Arguments.GetRawText()); command.Parameters.AddWithValue("$permission", pending.RequiredPermission); command.Parameters.AddWithValue("$reference", pending.ConversationReference); command.Parameters.AddWithValue("$iteration", pending.NextIteration); command.Parameters.AddWithValue("$created", pending.CreatedAt.ToString("O")); command.ExecuteNonQuery();
    }

    public PendingToolApproval? Get(string runId, string toolCallId)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM project_chat_pending_tool_calls WHERE run_id=$run AND tool_call_id=$call;";
        command.Parameters.AddWithValue("$run", runId); command.Parameters.AddWithValue("$call", toolCallId); using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public PendingToolApproval? GetForChat(string chatId)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM project_chat_pending_tool_calls WHERE chat_id=$chat AND status='pending' ORDER BY created_utc DESC LIMIT 1;";
        command.Parameters.AddWithValue("$chat", chatId); using var reader = command.ExecuteReader(); return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<PendingToolApproval> ListForChat(string chatId)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM project_chat_pending_tool_calls WHERE chat_id=$chat ORDER BY created_utc, rowid;";
        command.Parameters.AddWithValue("$chat", chatId); using var reader = command.ExecuteReader(); var approvals = new List<PendingToolApproval>();
        while (reader.Read()) approvals.Add(Read(reader)); return approvals;
    }

    public IReadOnlyList<PendingToolApproval> ListForProject(string projectId)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM project_chat_pending_tool_calls WHERE project_id=$project ORDER BY created_utc, rowid;";
        command.Parameters.AddWithValue("$project", projectId); using var reader = command.ExecuteReader(); var approvals = new List<PendingToolApproval>();
        while (reader.Read()) approvals.Add(Read(reader)); return approvals;
    }

    public bool TryResolve(string runId, string toolCallId, string resolution)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE project_chat_pending_tool_calls SET status=$status WHERE run_id=$run AND tool_call_id=$call AND status='pending';";
        command.Parameters.AddWithValue("$status", resolution); command.Parameters.AddWithValue("$run", runId); command.Parameters.AddWithValue("$call", toolCallId); return command.ExecuteNonQuery() == 1;
    }

    private const string Columns = "run_id,tool_call_id,chat_id,user_message_id,project_id,tool_id,arguments_json,required_permission,conversation_reference,next_iteration,status,created_utc,event_id";
    private static PendingToolApproval Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), JsonDocument.Parse(reader.GetString(6)).RootElement.Clone(), reader.GetString(7), reader.GetString(8), reader.GetInt32(9), reader.GetString(10), DateTimeOffset.Parse(reader.GetString(11)), reader.GetString(12));
    private static string EventIdFor(string toolCallId) => toolCallId.Replace(":call:", ":permission-required:", StringComparison.Ordinal);
}
