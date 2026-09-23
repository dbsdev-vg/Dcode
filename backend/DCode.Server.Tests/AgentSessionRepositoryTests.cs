using DCode.Server.Agents;
using DCode.Server.Chats;
using DCode.Server.Providers;
using DCode.Server.Storage;
using Microsoft.Extensions.Configuration;

namespace DCode.Server.Tests;

public sealed class AgentSessionRepositoryTests:IDisposable
{
 private readonly string directory=Path.Combine(Path.GetTempPath(),"dcode-agent-sessions",Guid.NewGuid().ToString("N"));
 [Fact] public void CoderSessionIsPersistentIsolatedAndHiddenFromUserConversations(){var(database,agents,profiles,chats,sessions)=Create();InsertProject(database,"project");var profile=profiles.Create(new("Coder account"));profiles.MarkConnected(profile.Id);var coder=agents.GetForProject("project").Single(agent=>agent.Role=="coder");coder=agents.Update("project",coder.Id,new(coder.Name,"Code carefully",profile.Id,"coder-model",true));var first=sessions.GetOrCreate(coder);var restored=new AgentSessionRepository(new DCodeDatabase(Configuration()),new ChatRepository(new DCodeDatabase(Configuration())),new BrowserProfileRepository(new DCodeDatabase(Configuration()))).GetOrCreate(coder);Assert.Equal(first.Id,restored.Id);Assert.Equal(coder.Id,first.AgentId);Assert.Empty(chats.GetProjectChats("project").Conversations);Assert.Equal("Coder internal session",chats.Get(first.ConversationId).Title);}
 [Fact] public void SessionRequiresEnabledAgentAndProviderAssignment(){var(database,agents,_,_,sessions)=Create();InsertProject(database,"project");var coder=agents.GetForProject("project").Single(agent=>agent.Role=="coder");Assert.Throws<InvalidOperationException>(()=>sessions.GetOrCreate(coder));coder=agents.Update("project",coder.Id,new(coder.Name,"",null,null,false));Assert.Throws<InvalidOperationException>(()=>sessions.GetOrCreate(coder));}
 [Fact] public void ResetRemovesOnlyInternalSessionAndAllowsFreshConversation(){var(database,agents,profiles,_,sessions)=Create();InsertProject(database,"project");var profile=profiles.Create(new("Coder account"));profiles.MarkConnected(profile.Id);var coder=agents.GetForProject("project").Single(agent=>agent.Role=="coder");coder=agents.Update("project",coder.Id,new(coder.Name,"",profile.Id,null,true));var first=sessions.GetOrCreate(coder);sessions.Reset("project",coder.Id);Assert.Null(sessions.Find("project",coder.Id));var next=sessions.GetOrCreate(coder);Assert.NotEqual(first.Id,next.Id);Assert.NotEqual(first.ConversationId,next.ConversationId);}
 private(DCodeDatabase,ProjectAgentRepository,BrowserProfileRepository,ChatRepository,AgentSessionRepository)Create(){Directory.CreateDirectory(directory);var database=new DCodeDatabase(Configuration());database.Initialize();var agents=new ProjectAgentRepository(database);var profiles=new BrowserProfileRepository(database);var chats=new ChatRepository(database);return(database,agents,profiles,chats,new AgentSessionRepository(database,chats,profiles));}
 private IConfiguration Configuration()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:DatabasePath",Path.Combine(directory,"dcode.db")}}).Build();
 private static void InsertProject(DCodeDatabase database,string id){using var connection=database.OpenConnection();using var command=connection.CreateCommand();var path=Path.Combine(Path.GetTempPath(),id);command.CommandText="INSERT INTO projects(id,name,path,normalized_path,created_utc,last_opened_utc) VALUES($id,$id,$path,$path,$now,$now);";command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$path",path);command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));command.ExecuteNonQuery();}
 public void Dispose(){try{if(Directory.Exists(directory))Directory.Delete(directory,true);}catch{}}
}
