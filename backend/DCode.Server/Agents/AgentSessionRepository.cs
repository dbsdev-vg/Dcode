using DCode.Server.Chats;
using DCode.Server.Providers;
using DCode.Server.Storage;

namespace DCode.Server.Agents;

public sealed class AgentSessionRepository(DCodeDatabase database, ChatRepository chats, BrowserProfileRepository profiles)
{
    public AgentSession GetOrCreate(ProjectAgent agent)
    {
        if (!agent.Enabled) throw new InvalidOperationException($"Agent '{agent.Name}' is disabled.");
        if (string.IsNullOrWhiteSpace(agent.BrowserProfileId)) throw new InvalidOperationException($"Assign a provider account to Agent '{agent.Name}' before delegating work.");
        var existing=Find(agent.ProjectId,agent.Id);
        if(existing is not null)
        {
            if(!string.Equals(existing.ProviderAccountId,agent.BrowserProfileId,StringComparison.Ordinal)) throw new InvalidOperationException("The Agent provider assignment changed. Internal-session rebinding is not supported yet.");
            return existing;
        }
        var chat=chats.CreateAgentChat(agent.ProjectId,agent.Id,agent.Name,profiles.Get(agent.BrowserProfileId));var id=Guid.NewGuid().ToString("N");var now=DateTimeOffset.UtcNow.ToString("O");
        using var connection=database.OpenConnection();using var command=connection.CreateCommand();command.CommandText="INSERT INTO agent_sessions(id,project_id,agent_id,conversation_id,status,created_utc,updated_utc) VALUES($id,$project,$agent,$chat,'active',$now,$now);";command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$project",agent.ProjectId);command.Parameters.AddWithValue("$agent",agent.Id);command.Parameters.AddWithValue("$chat",chat.Id);command.Parameters.AddWithValue("$now",now);command.ExecuteNonQuery();return Get(id);
    }
    public AgentSession Get(string id)=>Query("s.id=$value",id)??throw new KeyNotFoundException($"Agent session '{id}' was not found.");
    public AgentSession? Find(string projectId,string agentId)=>Query("s.project_id=$project AND s.agent_id=$value",agentId,projectId);
    public IReadOnlyList<AgentSession> ListForProject(string projectId){using var connection=database.OpenConnection();using var command=connection.CreateCommand();command.CommandText=Select+" WHERE s.project_id=$project ORDER BY s.created_utc;";command.Parameters.AddWithValue("$project",projectId);using var reader=command.ExecuteReader();var result=new List<AgentSession>();while(reader.Read())result.Add(Read(reader));return result;}
    public void Reset(string projectId,string agentId)
    {
        var session=Find(projectId,agentId)??throw new KeyNotFoundException("The Agent session was not found.");
        using var connection=database.OpenConnection();
        using(var active=connection.CreateCommand()){active.CommandText="SELECT COUNT(*) FROM agent_runs WHERE conversation_id=$chat AND status IN ('running','waiting_permission','waiting_delegation');";active.Parameters.AddWithValue("$chat",session.ConversationId);if((long)(active.ExecuteScalar()??0L)>0)throw new InvalidOperationException("Stop or resolve the Agent's active run before resetting its session.");}
        using var transaction=connection.BeginTransaction();
        using(var remove=connection.CreateCommand()){remove.Transaction=transaction;remove.CommandText="DELETE FROM agent_sessions WHERE id=$id;";remove.Parameters.AddWithValue("$id",session.Id);remove.ExecuteNonQuery();}
        using(var chat=connection.CreateCommand()){chat.Transaction=transaction;chat.CommandText="DELETE FROM chats WHERE id=$chat;";chat.Parameters.AddWithValue("$chat",session.ConversationId);chat.ExecuteNonQuery();}
        transaction.Commit();
    }
    private AgentSession? Query(string predicate,string value,string? project=null){using var connection=database.OpenConnection();using var command=connection.CreateCommand();command.CommandText=$"{Select} WHERE {predicate};";command.Parameters.AddWithValue("$value",value);if(project is not null)command.Parameters.AddWithValue("$project",project);using var reader=command.ExecuteReader();return reader.Read()?Read(reader):null;}
    private static AgentSession Read(Microsoft.Data.Sqlite.SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetString(9),r.GetString(10),DateTimeOffset.Parse(r.GetString(11)),DateTimeOffset.Parse(r.GetString(12)));
    private const string Select="SELECT s.id,s.project_id,s.agent_id,a.name,a.role,s.conversation_id,c.provider_type,c.transport,c.browser_profile_id,c.provider_conversation_url,s.status,s.created_utc,s.updated_utc FROM agent_sessions s JOIN project_agents a ON a.id=s.agent_id JOIN chats c ON c.id=s.conversation_id";
}
