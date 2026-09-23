export interface BrowserProfile { id: string; name: string; connected: boolean; }
export interface ChatConversation { id: string; title: string; providerType: string; transport: string; browserProfileId: string; accountName: string; providerConversationId: string | null; providerConversationUrl: string | null; status: string; }
export interface ProjectConversationCollection { activeConversationId: string | null; conversations: ChatConversation[]; }
export interface ChatMessage { id: string; role: "user" | "assistant" | "system"; content: string; }
export interface ToolActivity { toolId: string; label: string; success: boolean; }

export interface ProjectChatResponse {
  status: string;
  error: string | null;
  exchange: { conversation: ChatConversation; messages: ChatMessage[] } | null;
  activities: ToolActivity[];
}

export interface ConversationProgress {
  id: string;
  stage: "user_message" | "provider_started" | "provider_response" | "provider_text" | "tool_requested" | "tool_started" | "tool_completed" | "tool_reused" | "tool_failed" | "tool_call_rejected" | "provider_retry_started" | "provider_retry_completed" | "provider_retry_failed" | "recovery_started" | "recovery_completed" | "recovery_exhausted" | "permission_required" | "permission_granted" | "permission_denied" | "continuation_started" | "delegation_started" | "delegation_completed" | "delegation_waiting" | "delegation_failed" | "final_response" | "error";
  label: string;
  status: "running" | "waiting" | "completed" | "failed" | "cancelled";
  durationMilliseconds: number | null;
  detail: string | null;
  timestamp?: string | null;
}

export interface ProjectChatStreamEvent {
  type: "exchange" | "progress" | "result";
  progress: ConversationProgress | null;
  exchange: ProjectChatResponse["exchange"];
  result: ProjectChatResponse | null;
}

export interface ProjectChatExecutionState {
  status: string;
  error: string | null;
  progress: ConversationProgress[];
  updatedUtc: string;
}

export interface ProjectChatExecutionTurn {
  id: string;
  chatId: string;
  userMessageId: string;
  status: string;
  error: string | null;
  events: ConversationProgress[];
  createdUtc: string;
  updatedUtc: string;
}

export interface PendingToolApproval {
  runId: string;
  toolCallId: string;
  eventId: string;
  chatId: string;
  userMessageId: string;
  projectId: string;
  toolId: string;
  arguments: Record<string, unknown>;
  requiredPermission: string;
  conversationReference: string;
  nextIteration: number;
  status: "pending" | "approved" | "always_allowed" | "denied";
  createdAt: string;
}
