using DCode.Server.Chats;
using DCode.Server.Chats.AgentLoop;
using DCode.Server.Projects;

namespace DCode.Server.Agents;

public sealed class DelegationContinuationService(
    AgentRunRepository runs, ChatRepository chats, ProjectService projects,
    ConversationRunner runner, ProviderToolProtocol protocol,
    AgentDelegationService delegation, PendingToolApprovalRepository approvals)
{
    public async Task<ProjectChatRunResponse?> ResumeParentAsync(string childRunId, string? childResult, Func<ConversationProgress,CancellationToken,ValueTask>? progress, CancellationToken cancellationToken)
    {
        var child=runs.Get(childRunId);
        if(string.IsNullOrWhiteSpace(child.ParentRunId))return null;
        var parent=runs.Get(child.ParentRunId);
        if(parent.Status!="waiting_delegation")return null;
        if(string.IsNullOrWhiteSpace(parent.ConversationId))throw new InvalidOperationException("The parent Lead run has no conversation.");
        var chat=chats.Get(parent.ConversationId);var project=projects.GetProject(parent.ProjectId);var events=parent.Events.ToList();
        var startIteration=events.Count(item=>item.Stage=="provider_started");
        var continuation=protocol.FormatDelegationResult(new(child.Status,child.Id,childResult,child.Error));
        runs.Update(parent.Id,"running",null,events);chats.SaveExecutionState(chat.Id,"running",null,events);
        var result=await runner.RunAsync(new ConversationRunRequest(
            new(chat.ProviderType,chat.Transport,chat.BrowserProfileId,chat.ProviderConversationUrl),project,parent.Objective,chat.Id,
            reference=>chats.BindRemoteConversation(chat.Id,reference),
            async(item,token)=>{var index=events.FindIndex(existing=>existing.Id==item.Id);if(index<0)events.Add(item);else events[index]=item;chats.SaveExecutionState(chat.Id,"running",null,events);runs.Update(parent.Id,"running",null,events);if(progress is not null)await progress(item,token);},
            ContinuationContent:continuation,StartIteration:startIteration,RunId:parent.Id,
            Delegation:async(call,childProgress,token)=>{var delegated=await delegation.DelegateAsync(parent.Id,new(call.AgentRole,call.Objective),token,childProgress);return new(delegated.Run.Status,delegated.Run.Id,delegated.Result,delegated.Run.Error);},
            FinalizingDelegation: child.Status == "completed" ? new(child.Status, child.Id, childResult, child.Error) : null,
            DelegationAttemptCount: 1),cancellationToken);
        if(result.Status=="waiting_permission"&&result.PendingToolCall is not null&&!string.IsNullOrWhiteSpace(result.ConversationReference))
            approvals.Save(new(parent.Id,result.PendingToolCall.ToolCallId,chat.Id,parent.UserMessageId!,project.Id,result.PendingToolCall.Call.ToolId,result.PendingToolCall.Call.Arguments,result.PendingToolCall.RequiredPermission,result.ConversationReference,result.PendingToolCall.NextIteration,"pending",DateTimeOffset.UtcNow,result.PendingToolCall.EventId));
        ChatExchangeResponse exchange=new(chat,chats.GetMessages(chat.Id));
        if(result.Status=="completed"&&!string.IsNullOrWhiteSpace(result.FinalContent)&&!string.IsNullOrWhiteSpace(result.ConversationReference))exchange=chats.SaveAssistantMessage(chat.Id,result.FinalContent,result.ConversationReference);
        chats.SaveExecutionState(chat.Id,result.Status,result.Error,events);runs.Update(parent.Id,result.Status,result.Error,events);
        return new(result.Status,result.Error,exchange,result.Activities);
    }
}
