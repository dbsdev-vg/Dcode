using DCode.Server.Storage;

namespace DCode.Server.Providers;

public sealed class BrowserProfileRepository(DCodeDatabase database)
{
    private const string ProviderId = "deepseek-web";

    public IReadOnlyList<BrowserProfile> GetDeepSeekProfiles()
    {
        EnsureProvider();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bp.id, pc.provider_type, bp.profile_name, bp.user_data_directory,
                   bp.browser_channel, bp.headless,
                   COALESCE(bpc.connection_status = 'connected', 0)
            FROM browser_profiles bp JOIN provider_configs pc ON pc.id = bp.provider_config_id
            LEFT JOIN browser_profile_connections bpc ON bpc.browser_profile_id = bp.id
            WHERE bp.provider_config_id = $providerId ORDER BY bp.created_utc;
            """;
        command.Parameters.AddWithValue("$providerId", ProviderId);
        using var reader = command.ExecuteReader();
        var profiles = new List<BrowserProfile>();
        while (reader.Read()) profiles.Add(Read(reader));
        return profiles;
    }

    public BrowserProfile Get(string id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bp.id, pc.provider_type, bp.profile_name, bp.user_data_directory,
                   bp.browser_channel, bp.headless,
                   COALESCE(bpc.connection_status = 'connected', 0)
            FROM browser_profiles bp JOIN provider_configs pc ON pc.id = bp.provider_config_id
            LEFT JOIN browser_profile_connections bpc ON bpc.browser_profile_id = bp.id
            WHERE bp.id = $id AND bp.provider_config_id = $providerId;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$providerId", ProviderId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException($"Browser profile '{id}' was not found.");
        return Read(reader);
    }

    public BrowserProfile Create(CreateBrowserProfileRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A profile name is required.");
        if (!string.Equals(request.BrowserChannel, "msedge", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DeepSeek currently supports Microsoft Edge only.");

        EnsureProvider();
        var id = Guid.NewGuid().ToString("N");
        var root = Path.GetDirectoryName(database.DatabasePath)
            ?? throw new InvalidOperationException("DCode storage path has no parent directory.");
        var directory = Path.Combine(root, "browser-profiles", id);
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO browser_profiles (id, provider_config_id, profile_name,
                user_data_directory, browser_channel, headless, created_utc, updated_utc)
            VALUES ($id, $providerId, $name, $directory, 'msedge', $headless, $now, $now);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$providerId", ProviderId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$directory", directory);
        command.Parameters.AddWithValue("$headless", request.Headless);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
        return new(id, "deepseek", name, directory, "msedge", request.Headless, false);
    }

    public void MarkConnected(string id)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO browser_profile_connections (
                browser_profile_id, connection_status, verified_utc, updated_utc
            ) VALUES ($id, 'connected', $now, $now)
            ON CONFLICT(browser_profile_id) DO UPDATE SET
                connection_status = 'connected', verified_utc = $now, updated_utc = $now;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    public BrowserProfile UpdateExecutionMode(string id, bool headless)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE browser_profiles SET headless = $headless, updated_utc = $now WHERE id = $id AND provider_config_id = $providerId;";
        command.Parameters.AddWithValue("$headless", headless);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$providerId", ProviderId);
        if (command.ExecuteNonQuery() == 0) throw new KeyNotFoundException($"Browser profile '{id}' was not found.");
        return Get(id);
    }

    public void Delete(BrowserProfile profile)
    {
        using (var dependencyConnection = database.OpenConnection())
        using (var dependencyCheck = dependencyConnection.CreateCommand())
        {
            dependencyCheck.CommandText = "SELECT COUNT(*) FROM chats WHERE browser_profile_id = $id;";
            dependencyCheck.Parameters.AddWithValue("$id", profile.Id);
            if ((long)(dependencyCheck.ExecuteScalar() ?? 0L) > 0)
                throw new InvalidOperationException("Delete or move this account's chats before deleting the browser account.");
        }

        var storageRoot = Path.GetDirectoryName(database.DatabasePath)
            ?? throw new InvalidOperationException("DCode storage path has no parent directory.");
        var profilesRoot = Path.GetFullPath(Path.Combine(storageRoot, "browser-profiles"));
        var expectedDirectory = Path.GetFullPath(Path.Combine(profilesRoot, profile.Id));
        var actualDirectory = Path.GetFullPath(profile.UserDataDirectory);
        if (!string.Equals(expectedDirectory, actualDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The browser profile directory is outside DCode storage.");

        if (Directory.Exists(actualDirectory))
        {
            var attributes = File.GetAttributes(actualDirectory);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("The browser profile directory is a reparse point and cannot be deleted safely.");
            Directory.Delete(actualDirectory, recursive: true);
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM browser_profiles WHERE id = $id;";
        command.Parameters.AddWithValue("$id", profile.Id);
        command.ExecuteNonQuery();
    }

    private static BrowserProfile Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2),
        reader.GetString(3), reader.GetString(4), reader.GetBoolean(5), reader.GetBoolean(6));

    private void EnsureProvider()
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO provider_configs (id, provider_type, transport, display_name,
                settings_json, status, created_utc, updated_utc)
            VALUES ($id, 'deepseek', 'browser', 'DeepSeek Web', '{}',
                'disconnected', $now, $now) ON CONFLICT(id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", ProviderId);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }
}
