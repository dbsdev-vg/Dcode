import type {
  Project,
  ProjectState,
} from "@/types/dcode";

const state: ProjectState = {
  projects: [],
  activeProjectId: null,
};

export function getProjectState(): ProjectState {
  return state;
}

export function setActiveProject(projectId: string | null): void {
  state.activeProjectId = projectId;
}

export function addProject(project: Project): void {
  state.projects.push(project);
}

export function removeProject(projectId: string): void {
  state.projects = state.projects.filter(
    (project) => project.id !== projectId,
  );

  if (state.activeProjectId === projectId) {
    state.activeProjectId = null;
  }
}