"use client";

import {
  createContext,
  useContext,
  useCallback,
  useState,
  type ReactNode,
} from "react";

import type { Project } from "../types/dcode";

interface ProjectContextValue {
  projects: Project[];
  activeProject: Project | null;

  addProject: (project: Project) => void;
  restoreProjects: (projects: Project[]) => void;
  setActiveProject: (project: Project | null) => void;
}

const ProjectContext =
  createContext<ProjectContextValue | null>(null);

export function ProjectProvider({
  children,
}: {
  children: ReactNode;
}) {
  const [projects, setProjects] =
    useState<Project[]>([]);

  const [activeProject, setActiveProject] =
    useState<Project | null>(null);

  const addProject = useCallback((project: Project) => {
    setProjects((current) => {
      const existingIndex = current.findIndex(
        (item) => item.path === project.path,
      );

      if (existingIndex >= 0) {
        const next = [...current];
        next[existingIndex] = project;
        return next;
      }

      return [...current, project];
    });
  }, []);

  const restoreProjects = useCallback((persistedProjects: Project[]) => {
    setProjects(persistedProjects);
  }, []);

  return (
    <ProjectContext.Provider
      value={{
        projects,
        activeProject,
        addProject,
        restoreProjects,
        setActiveProject,
      }}
    >
      {children}
    </ProjectContext.Provider>
  );
}

export function useProject() {
  const context = useContext(ProjectContext);

  if (!context) {
    throw new Error(
      "useProject must be used inside ProjectProvider",
    );
  }

  return context;
}
