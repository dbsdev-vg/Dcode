import type { Project, ProjectTreeNode } from "../../../types/dcode";

export type ControlMode = "agent" | "team" | "runs" | "project";
export type NavigatorMode = "files" | "search" | "git";
export type DockMode = "terminal" | "problems" | "output" | "logs";

export interface EditorDocument {
  relativePath: string;
  name: string;
  content: string;
  savedContent: string;
  loading: boolean;
  error: string | null;
}

export interface RegisteredProject {
  id: string;
  name: string;
  path: string;
}

export interface ProjectSession {
  projectId: string;
  activeFilePath: string | null;
  openTabs: string[];
  expandedFolders: string[];
  layout: { explorerOpen: boolean; chatOpen: boolean; terminalOpen: boolean };
}

export interface ProjectEnvironmentModel {
  project: Project;
  registered: RegisteredProject | null;
  tree: ProjectTreeNode[];
}

export interface ProjectAgent {
  id: string;
  name: string;
  role: string;
  lead: boolean;
  status: "idle" | "working" | "disabled";
  providerId?: string;
  accountId?: string;
  modelId?: string;
}

export type ProjectTaskStatus = "queued" | "running" | "waiting_approval" | "blocked" | "completed" | "failed" | "cancelled";

export interface ProjectTask {
  id: string;
  projectId: string;
  title: string;
  objective: string;
  status: ProjectTaskStatus;
  assignedAgentId?: string;
  changedFiles: string[];
  result?: string;
}
