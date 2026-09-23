using DCode.Server.Agents;
using DCode.Server.Providers;
using DCode.Server.Storage;
using Microsoft.Extensions.Configuration;

namespace DCode.Server.Tests;

public sealed class ProjectAgentRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dcode-project-agents", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsAreStableProjectScopedLeadAndCoder()
    {
        var (database, agents, _) = Create(); InsertProject(database,"a"); InsertProject(database,"b");
        var first = agents.GetForProject("a"); var restored = new ProjectAgentRepository(new DCodeDatabase(Configuration())).GetForProject("a"); var other = agents.GetForProject("b");
        Assert.Equal(2, first.Count); Assert.Single(first, agent=>agent.IsLead); Assert.Contains(first, agent=>agent.Role=="coder");
        Assert.Equal(first.Select(agent=>agent.Id), restored.Select(agent=>agent.Id)); Assert.DoesNotContain(other, agent=>first.Any(existing=>existing.Id==agent.Id));
    }

    [Fact]
    public void AssignmentAndInstructionsPersistAndConversationsBackfillLead()
    {
        var (database, agents, profiles) = Create(); InsertProject(database,"a");
        var profile=profiles.Create(new("DBS Account")); var lead=agents.GetForProject("a").Single(agent=>agent.IsLead);
        using(var connection=database.OpenConnection()) using(var command=connection.CreateCommand()) { command.CommandText="INSERT INTO chats (id,title,provider_type,transport,browser_profile_id,status,created_utc,updated_utc) VALUES ('chat','New','deepseek','browser',$profile,'active',$now,$now); INSERT INTO project_conversations(project_id,chat_id,kind,archived,created_utc,updated_utc) VALUES ('a','chat','user',0,$now,$now);"; command.Parameters.AddWithValue("$profile",profile.Id);command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));command.ExecuteNonQuery(); }
        var updated=agents.Update("a",lead.Id,new("Architect","Plan work clearly",profile.Id,"deepseek-chat",true)); agents.EnsureDefaults("a");
        Assert.Equal("Architect",updated.Name); Assert.Equal(profile.Id,updated.BrowserProfileId); Assert.Equal("deepseek-chat",updated.Model);
        using var checkConnection=database.OpenConnection();using var check=checkConnection.CreateCommand();check.CommandText="SELECT agent_id FROM project_conversations WHERE chat_id='chat';";Assert.Equal(lead.Id,check.ExecuteScalar());
    }

    private (DCodeDatabase,ProjectAgentRepository,BrowserProfileRepository) Create(){Directory.CreateDirectory(directory);var database=new DCodeDatabase(Configuration());database.Initialize();return(database,new(database),new(database));}
    private IConfiguration Configuration()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:DatabasePath",Path.Combine(directory,"dcode.db")}}).Build();
    private static void InsertProject(DCodeDatabase database,string id){using var connection=database.OpenConnection();using var command=connection.CreateCommand();var path=Path.Combine(Path.GetTempPath(),id);command.CommandText="INSERT INTO projects(id,name,path,normalized_path,created_utc,last_opened_utc) VALUES($id,$id,$path,$path,$now,$now);";command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$path",path);command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));command.ExecuteNonQuery();}
    public void Dispose(){try{if(Directory.Exists(directory))Directory.Delete(directory,true);}catch{}}
}
