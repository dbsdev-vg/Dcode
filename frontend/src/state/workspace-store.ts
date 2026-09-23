import type {
  Workspace,
  WorkspaceState,
} from "../types/dcode";

const state: WorkspaceState = {
  activeWorkspace: "projects",
};

export function getWorkspaceState(): WorkspaceState {
  return state;
}

export function setActiveWorkspace(workspace: Workspace): void {
  state.activeWorkspace = workspace;
}