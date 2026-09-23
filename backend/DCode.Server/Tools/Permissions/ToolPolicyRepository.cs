using DCode.Server.Storage;

namespace DCode.Server.Tools.Permissions;

public sealed class ToolPolicyRepository(DCodeDatabase database)
{
    public ToolPermissionPolicy? GetGlobal(string toolId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT policy FROM tool_permission_policies WHERE tool_id = $toolId;";
        command.Parameters.AddWithValue("$toolId", toolId);
        var value = command.ExecuteScalar() as string;
        return ToolPermissionPolicyValues.TryParse(value, out var policy) ? policy : null;
    }

    public IReadOnlyDictionary<string, ToolPermissionPolicy> GetGlobals()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tool_id, policy FROM tool_permission_policies ORDER BY tool_id;";
        using var reader = command.ExecuteReader();
        var policies = new Dictionary<string, ToolPermissionPolicy>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (ToolPermissionPolicyValues.TryParse(reader.GetString(1), out var policy)) policies[reader.GetString(0)] = policy;
        }
        return policies;
    }

    public void SetGlobal(string toolId, ToolPermissionPolicy policy)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tool_permission_policies (tool_id, policy, updated_utc)
            VALUES ($toolId, $policy, $updatedUtc)
            ON CONFLICT(tool_id) DO UPDATE SET
                policy = excluded.policy,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$toolId", toolId);
        command.Parameters.AddWithValue("$policy", ToolPermissionPolicyValues.Serialize(policy));
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
}
