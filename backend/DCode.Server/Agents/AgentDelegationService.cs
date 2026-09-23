using DCode.Server.Chats;
using DCode.Server.Chats.AgentLoop;
using DCode.Server.Projects;
using DCode.Server.Providers;

namespace DCode.Server.Agents;

public sealed class AgentDelegationService(ProjectAgentRepository agents, AgentSessionRepository sessions, AgentRunRepository runs, ChatRepository chats, ProjectService projects, ConversationRunner runner, PendingToolApprovalRepository approvals)
{
    public async Task<DelegationResponse> DelegateAsync(string parentRunId, DelegateAgentRequest request, CancellationToken cancellationToken, Func<ConversationProgress,CancellationToken,ValueTask>? parentProgress = null)
    {
        var parent=runs.Get(parentRunId);var parentAgent=agents.GetByRole(parent.ProjectId,"lead");
        if(parent.AgentId!=parentAgent.Id)throw new InvalidOperationException("Only a Lead Agent run can delegate project work.");
        var role=request.AgentRole.Trim().ToLowerInvariant();if(role.Length==0)throw new ArgumentException("A target Agent role is required.");if(role=="lead")throw new ArgumentException("A Lead Agent cannot delegate work back to itself.");
        var objective=request.Objective.Trim();if(objective.Length==0)throw new ArgumentException("A delegation objective is required.");if(objective.Length>4000)throw new ArgumentException("The delegation objective is too long.");
        var agent=agents.GetByRole(parent.ProjectId,role);var session=sessions.GetOrCreate(agent);var chat=chats.Get(session.ConversationId);var project=projects.GetProject(parent.ProjectId);
        var exchange=chats.SaveUserMessage(chat.Id,objective);var userMessageId=exchange.Messages.Last(message=>message.Role=="user").Id;var runId=Guid.NewGuid().ToString("N");var events=new List<ConversationProgress>();
        runs.Start(new(runId,project.Id,agent.Id,chat.Id,"delegated",objective,chat.ProviderType,chat.Transport,chat.BrowserProfileId,agent.Model,userMessageId,ParentRunId:parentRunId));
        var prompt=$"You are the project's {agent.Name} ({agent.Role}) Agent.\nAgent instructions:\n{(string.IsNullOrWhiteSpace(agent.Instructions)?"Implement the delegated objective carefully and report the result.":agent.Instructions)}\n\nDelegated objective:\n{objective}";
        ConversationRunResult result;
        try{result=await runner.RunAsync(new ConversationRunRequest(new ProviderConversationSession(chat.ProviderType,chat.Transport,chat.BrowserProfileId,chat.ProviderConversationUrl),project,prompt,chat.Id,reference=>chats.BindRemoteConversation(chat.Id,reference),async(progress,token)=>{var index=events.FindIndex(item=>item.Id==progress.Id);if(index<0)events.Add(progress);else events[index]=progress;runs.Update(runId,"running",null,events);if(parentProgress is not null)await parentProgress(progress with{Id=$"{runId}:child:{progress.Id}"},token);},RunId:runId,IterationLimit:20),cancellationToken);}
        catch(OperationCanceledException){MarkTerminal(events,"cancelled");runs.Update(runId,"cancelled","Delegated run cancelled.",events);throw;}
        catch(Exception exception){MarkTerminal(events,"failed");runs.Update(runId,"provider_error",exception.Message,events);throw;}
        if(result.Status=="waiting_permission"&&result.PendingToolCall is not null&&!string.IsNullOrWhiteSpace(result.ConversationReference))approvals.Save(new(runId,result.PendingToolCall.ToolCallId,chat.Id,userMessageId,project.Id,result.PendingToolCall.Call.ToolId,result.PendingToolCall.Call.Arguments,result.PendingToolCall.RequiredPermission,result.ConversationReference,result.PendingToolCall.NextIteration,"pending",DateTimeOffset.UtcNow,result.PendingToolCall.EventId));
        string? final=null;if(result.Status=="completed"&&!string.IsNullOrWhiteSpace(result.FinalContent)&&!string.IsNullOrWhiteSpace(result.ConversationReference)){final=result.FinalContent;chats.SaveAssistantMessage(chat.Id,final,result.ConversationReference);}
        runs.Update(runId,result.Status,result.Error,events);return new(runs.Get(runId),sessions.Get(session.Id),final);
    }
    private static void MarkTerminal(List<ConversationProgress> events,string status){for(var index=0;index<events.Count;index++)if(events[index].Status=="running")events[index]=events[index] with{Status=status};}
}
