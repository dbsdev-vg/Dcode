using DCode.Server.Agents;
using DCode.Server.Chats.AgentLoop;
using DCode.Server.Storage;
using Microsoft.Extensions.Configuration;

namespace DCode.Server.Tests;

public sealed class AgentRunRepositoryTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"dcode-agent-runs",Guid.NewGuid().ToString("N"));
    [Fact]
    public void RunPersistsAgentOwnershipLifecycleAndImmutableSnapshots()
    {
        var(database,agents,runs)=Create();InsertProject(database,"project");var lead=agents.GetLead("project");
        var started=runs.Start(new("run","project",lead.Id,null,"project_chat","Inspect package.json","deepseek","browser",null,"model-a",null));
        var events=new[]{new ConversationProgress("event","tool_completed","Read package.json","completed",12)};runs.Update("run","completed",null,events);
        var restored=new AgentRunRepository(new DCodeDatabase(Configuration())).Get("run");
        Assert.Equal("running",started.Status);Assert.Equal(lead.Id,restored.AgentId);Assert.Equal("Lead",restored.AgentName);Assert.Equal("model-a",restored.Model);Assert.Equal("completed",restored.Status);Assert.NotNull(restored.CompletedUtc);Assert.Single(restored.Events);
    }
    [Fact]
    public void RunsCanRepresentFutureTaskAndDelegatedAttemptsWithoutCreatingTasks()
    {
        var(database,agents,runs)=Create();InsertProject(database,"project");var identities=agents.GetForProject("project");var coder=identities.Single(agent=>agent.Role=="coder");
        runs.Start(new("parent","project",identities.Single(agent=>agent.IsLead).Id,null,"project_chat","Plan parser",null,null,null,null,null));
        var run=runs.Start(new("child","project",coder.Id,null,"delegated","Implement parser",null,null,null,null,null,"future-task","parent"));
        Assert.Equal("future-task",run.TaskId);Assert.Equal("parent",run.ParentRunId);Assert.Equal("coder",run.AgentRole);
    }
    private(DCodeDatabase,ProjectAgentRepository,AgentRunRepository)Create(){Directory.CreateDirectory(directory);var database=new DCodeDatabase(Configuration());database.Initialize();return(database,new(database),new(database));}
    private IConfiguration Configuration()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:DatabasePath",Path.Combine(directory,"dcode.db")}}).Build();
    private static void InsertProject(DCodeDatabase database,string id){using var connection=database.OpenConnection();using var command=connection.CreateCommand();var path=Path.Combine(Path.GetTempPath(),id);command.CommandText="INSERT INTO projects(id,name,path,normalized_path,created_utc,last_opened_utc) VALUES($id,$id,$path,$path,$now,$now);";command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$path",path);command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));command.ExecuteNonQuery();}
    public void Dispose(){try{if(Directory.Exists(directory))Directory.Delete(directory,true);}catch{}}
}
