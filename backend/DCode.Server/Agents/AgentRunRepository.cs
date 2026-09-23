using System.Text.Json;
using DCode.Server.Chats.AgentLoop;
using DCode.Server.Storage;

namespace DCode.Server.Agents;

public sealed class AgentRunRepository(DCodeDatabase database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AgentRun Start(StartAgentRun run)
    {
        var now=DateTimeOffset.UtcNow.ToString("O"); using var connection=database.OpenConnection();using var command=connection.CreateCommand();
        command.CommandText="""INSERT INTO agent_runs(id,project_id,agent_id,conversation_id,task_id,parent_run_id,trigger,objective,status,provider_type,transport,provider_account_id,model,user_message_id,events_json,created_utc,started_utc,updated_utc) VALUES($id,$project,$agent,$conversation,$task,$parent,$trigger,$objective,'running',$provider,$transport,$account,$model,$message,'[]',$now,$now,$now);""";
        Add(command,"$id",run.Id);Add(command,"$project",run.ProjectId);Add(command,"$agent",run.AgentId);Add(command,"$conversation",run.ConversationId);Add(command,"$task",run.TaskId);Add(command,"$parent",run.ParentRunId);Add(command,"$trigger",run.Trigger);Add(command,"$objective",run.Objective);Add(command,"$provider",run.ProviderType);Add(command,"$transport",run.Transport);Add(command,"$account",run.ProviderAccountId);Add(command,"$model",run.Model);Add(command,"$message",run.UserMessageId);Add(command,"$now",now);command.ExecuteNonQuery();return Get(run.Id);
    }

    public void Update(string runId,string status,string? error,IReadOnlyList<ConversationProgress> events)
    {
        var now=DateTimeOffset.UtcNow.ToString("O");var terminal=status is "completed" or "failed" or "cancelled" or "provider_error";using var connection=database.OpenConnection();using var command=connection.CreateCommand();
        command.CommandText="UPDATE agent_runs SET status=$status,error=$error,events_json=$events,completed_utc=CASE WHEN $terminal=1 THEN COALESCE(completed_utc,$now) ELSE NULL END,updated_utc=$now WHERE id=$id;";Add(command,"$status",status);Add(command,"$error",error);Add(command,"$events",JsonSerializer.Serialize(events,JsonOptions));Add(command,"$terminal",terminal);Add(command,"$now",now);Add(command,"$id",runId);if(command.ExecuteNonQuery()==0)throw new KeyNotFoundException($"Agent run '{runId}' was not found.");
    }

    public AgentRun Get(string id){using var connection=database.OpenConnection();using var command=connection.CreateCommand();command.CommandText=Select+" WHERE r.id=$id;";Add(command,"$id",id);using var reader=command.ExecuteReader();if(!reader.Read())throw new KeyNotFoundException($"Agent run '{id}' was not found.");return Read(reader);}
    public IReadOnlyList<AgentRun> ListForProject(string projectId,int limit=100)=>List("r.project_id=$value",projectId,limit);
    public IReadOnlyList<AgentRun> ListForConversation(string chatId,int limit=100)=>List("r.conversation_id=$value",chatId,limit,ascending:true);
    private IReadOnlyList<AgentRun> List(string predicate,string value,int limit,bool ascending=false){using var connection=database.OpenConnection();using var command=connection.CreateCommand();command.CommandText=$"{Select} WHERE {predicate} ORDER BY r.created_utc {(ascending?"ASC":"DESC")} LIMIT $limit;";Add(command,"$value",value);Add(command,"$limit",Math.Clamp(limit,1,500));using var reader=command.ExecuteReader();var result=new List<AgentRun>();while(reader.Read())result.Add(Read(reader));return result;}
    private static AgentRun Read(Microsoft.Data.Sqlite.SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),Null(r,5),Null(r,6),Null(r,7),r.GetString(8),r.GetString(9),r.GetString(10),Null(r,11),Null(r,12),Null(r,13),Null(r,14),Null(r,15),Null(r,16),JsonSerializer.Deserialize<List<ConversationProgress>>(r.GetString(17),JsonOptions)??[],DateTimeOffset.Parse(r.GetString(18)),Date(r,19),Date(r,20),DateTimeOffset.Parse(r.GetString(21)));
    private static string? Null(Microsoft.Data.Sqlite.SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);private static DateTimeOffset? Date(Microsoft.Data.Sqlite.SqliteDataReader r,int i)=>r.IsDBNull(i)?null:DateTimeOffset.Parse(r.GetString(i));private static void Add(Microsoft.Data.Sqlite.SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
    private const string Select="SELECT r.id,r.project_id,r.agent_id,a.name,a.role,r.conversation_id,r.task_id,r.parent_run_id,r.trigger,r.objective,r.status,r.provider_type,r.transport,r.provider_account_id,r.model,r.user_message_id,r.error,r.events_json,r.created_utc,r.started_utc,r.completed_utc,r.updated_utc FROM agent_runs r JOIN project_agents a ON a.id=r.agent_id";
}
