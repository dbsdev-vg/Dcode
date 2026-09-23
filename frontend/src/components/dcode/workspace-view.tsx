"use client";

import { Badge } from "@dbs-studio/ui";
import { Users } from "lucide-react";

import { useWorkspace } from "../../state/workspace-context";
import { ProjectsView } from "./projects-view";
import { FilesView } from "./files-view";
import { VendorProvidersView } from "./vendor-providers-view";
import { ConversationChatView } from "./conversation-chat-view";
import { HomeView } from "./home-view";
import { SettingsView } from "./settings-view";

export function WorkspaceView() {
  const { activeWorkspace } = useWorkspace();

  switch (activeWorkspace) {
    case "home":
      return <HomeView />;

    case "projects":
      return <ProjectsView />;

    case "files":
      return <FilesView />;

    case "chat":
      return <ConversationChatView />;

    case "agents":
      return (
        <section className="dcode-workspace-page">
          <header className="dcode-workspace-page__header"><div><span className="dcode-page-eyebrow">AI workspace</span><h1>Agents</h1><p>Configure specialized development agents and their responsibilities.</p></div><Badge variant="outline">Coming later</Badge></header>
          <div className="dcode-page-placeholder"><span><Users size={24} /></span><h2>Agent workspace</h2><p>Agent roles, sessions, and orchestration will appear here as the runtime evolves.</p></div>
        </section>
      );

    case "providers":
      return <VendorProvidersView />;

    case "settings":
      return <SettingsView />;
    
    default:
      return null;
  }
}
