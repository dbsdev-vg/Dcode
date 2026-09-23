using DCode.Server.Chats;
using DCode.Server.Providers;
using DCode.Server.Storage;
using Microsoft.Extensions.Configuration;

namespace DCode.Server.Tests;

public sealed class ProjectConversationRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dcode-project-conversations", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ConversationsAreProjectScopedAndActiveSelectionRestores()
    {
        var (database, profiles, chats) = CreateRepositories();
        InsertProject(database, "project-a", "Project A"); InsertProject(database, "project-b", "Project B");
        var profile = profiles.Create(new("Account")); profiles.MarkConnected(profile.Id); profile = profiles.Get(profile.Id);

        var first = chats.CreateProjectChat("project-a", profile);
        var second = chats.CreateProjectChat("project-a", profile);
        var other = chats.CreateProjectChat("project-b", profile);
        Assert.Equal("Project A Lead #1", first.Title);
        Assert.Equal("Project A Lead #2", second.Title);
        Assert.Equal("Project B Lead #1", other.Title);
        chats.BindRemoteConversation(first.Id, "https://chat.deepseek.com/a/chat/s/first");
        chats.BindRemoteConversation(second.Id, "https://chat.deepseek.com/a/chat/s/second");
        chats.SetProjectChat("project-a", first.Id);

        var restored = new ChatRepository(new DCodeDatabase(CreateConfiguration())).GetProjectChats("project-a");
        var otherProject = chats.GetProjectChats("project-b");

        Assert.Equal(2, restored.Conversations.Count);
        Assert.Equal(first.Id, restored.ActiveConversationId);
        Assert.Contains(restored.Conversations, item => item.ProviderConversationId == "first");
        Assert.Contains(restored.Conversations, item => item.ProviderConversationId == "second");
        Assert.Single(otherProject.Conversations);
        Assert.Equal(other.Id, otherProject.Conversations[0].Id);
        Assert.Throws<InvalidOperationException>(() => chats.SetProjectChat("project-b", first.Id));
    }

    [Fact]
    public void RenameArchiveAndDeletePreserveOtherProjectConversations()
    {
        var (database, profiles, chats) = CreateRepositories();
        InsertProject(database, "project-a", "Project A");
        var profile = profiles.Create(new("Account")); profiles.MarkConnected(profile.Id); profile = profiles.Get(profile.Id);
        var first = chats.CreateProjectChat("project-a", profile); var second = chats.CreateProjectChat("project-a", profile);

        Assert.Equal("Supplier work", chats.RenameProjectChat("project-a", first.Id, "  Supplier   work ").Title);
        chats.ArchiveProjectChat("project-a", first.Id);
        Assert.DoesNotContain(chats.GetProjectChats("project-a").Conversations, item => item.Id == first.Id);
        Assert.Contains(chats.GetProjectChats("project-a", true).Conversations, item => item.Id == first.Id && item.Status == "archived");
        chats.DeleteProjectChat("project-a", first.Id);
        Assert.Single(chats.GetProjectChats("project-a", true).Conversations);
        Assert.Equal(second.Id, chats.GetProjectChats("project-a", true).Conversations[0].Id);
    }

    private (DCodeDatabase Database, BrowserProfileRepository Profiles, ChatRepository Chats) CreateRepositories()
    {
        Directory.CreateDirectory(_directory); var database = new DCodeDatabase(CreateConfiguration()); database.Initialize();
        return (database, new BrowserProfileRepository(database), new ChatRepository(database));
    }

    private IConfiguration CreateConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DatabasePath"] = Path.Combine(_directory, "dcode.db") }).Build();

    private static void InsertProject(DCodeDatabase database, string id, string name)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand(); var path = Path.Combine(Path.GetTempPath(), id);
        command.CommandText = "INSERT INTO projects (id,name,path,normalized_path,created_utc,last_opened_utc) VALUES ($id,$name,$path,$path,$now,$now);";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$name", name); command.Parameters.AddWithValue("$path", path); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
    }

    public void Dispose() { try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { } }
}
