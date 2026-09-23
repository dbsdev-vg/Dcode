"use client";

import type { ReactNode } from "react";
import { useProject } from "../../state/project-context";
import { useWorkspace } from "../../state/workspace-context";
import { Sidebar } from "./sidebar";
import { Topbar } from "./topbar";

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const { activeProject } = useProject();
  const { activeWorkspace } = useWorkspace();
  const isProjectFocused = Boolean(
    activeProject && activeWorkspace === "files",
  );

  return (
    <div className={`dcode-shell ${
      isProjectFocused ? "is-project-focused" : ""
    }`}>
      {!isProjectFocused && <Sidebar />}

      <div className="dcode-main">
        {!isProjectFocused && <Topbar />}

        <main className="dcode-content">
          {children}
        </main>
      </div>
    </div>
  );
}
