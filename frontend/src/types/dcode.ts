export type Workspace =
  | "home"
  | "projects"
  | "files"
  | "chat"
  | "agents"
  | "providers"
  | "settings";

export type AIProvider =
  | "deepseek"
  | "gemini"
  | "chatgpt";

export type ProviderStatus =
  | "connected"
  | "disconnected"
  | "connecting"
  | "error";

export interface ChatProviderSession {
  conversationId: string;
  profileId: string;
  providerId: "deepseek";
  providerName: string;
  accountName: string;
  transport: "browser";
}

export interface ChatConversation {
  id: string;
  title: string;
  providerType: "deepseek";
  transport: "browser";
  browserProfileId: string;
  accountName: string;
  providerConversationId: string | null;
  providerConversationUrl: string | null;
  status: "active" | "archived" | "unavailable" | "authentication-required";
  createdUtc: string;
  updatedUtc: string;
}

export interface ChatMessage {
  id: string;
  chatId: string;
  role: "user" | "assistant" | "system";
  content: string;
  providerMessageId: string | null;
  createdUtc: string;
}

export interface Provider {
  id: AIProvider;
  name: string;
  status: ProviderStatus;
  model?: string;
}

export interface Project {
  id: string;
  name: string;
  path: string;
}

export interface WorkspaceState {
  activeWorkspace: Workspace;
}

export interface ProviderState {
  providers: Provider[];
  activeProvider: AIProvider | null;
}

export interface ProjectState {
  projects: Project[];
  activeProjectId: string | null;
}

export interface ProjectTreeNode {
  name: string;
  relativePath: string;
  isDirectory: boolean;
  children: ProjectTreeNode[] | null;
}
