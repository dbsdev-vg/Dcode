"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Badge, Button } from "@dbs-studio/ui";
import {
  ArrowRight,
  FolderGit2,
  MessageSquare,
  PlugZap,
} from "lucide-react";

import { useProject } from "../../state/project-context";
import { useWorkspace } from "../../state/workspace-context";
import type { ChatConversation, Project } from "../../types/dcode";
import { DCodeBrand } from "../brand/dcode-brand";

interface BrowserProfile {
  id: string;
  connected: boolean;
}

const serverUrl = "http://localhost:5283";

export function HomeView() {
  const { projects, activeProject, restoreProjects, setActiveProject } = useProject();
  const { setActiveWorkspace, startChat } = useWorkspace();
  const [conversations, setConversations] = useState<ChatConversation[]>([]);
  const [connectedProviders, setConnectedProviders] = useState(0);
  const [backendConnected, setBackendConnected] = useState<boolean | null>(null);

  useEffect(() => {
    let active = true;

    async function restoreHome() {
      const [projectsResult, activeProjectResult, chatsResult, profilesResult] = await Promise.allSettled([
        fetch(`${serverUrl}/api/projects`),
        fetch(`${serverUrl}/api/projects/active`),
        fetch(`${serverUrl}/api/chats`),
        fetch(`${serverUrl}/api/providers/browser/deepseek/profiles`),
      ]);

      if (!active) return;

      const serverAvailable = projectsResult.status === "fulfilled" && projectsResult.value.ok;
      setBackendConnected(serverAvailable);

      if (serverAvailable) {
        restoreProjects(await projectsResult.value.json() as Project[]);
      }
      if (activeProjectResult.status === "fulfilled" && activeProjectResult.value.ok) {
        setActiveProject(await activeProjectResult.value.json() as Project | null);
      }
      if (chatsResult.status === "fulfilled" && chatsResult.value.ok) {
        setConversations(await chatsResult.value.json() as ChatConversation[]);
      }
      if (profilesResult.status === "fulfilled" && profilesResult.value.ok) {
        const profiles = await profilesResult.value.json() as BrowserProfile[];
        setConnectedProviders(profiles.filter((profile) => profile.connected).length);
      }
    }

    void restoreHome();
    return () => { active = false; };
  }, [restoreProjects, setActiveProject]);

  function continueProject(project: Project) {
    setActiveProject(project);
    setActiveWorkspace("files");
  }

  function openConversation(chat: ChatConversation) {
    startChat({
      conversationId: chat.id,
      profileId: chat.browserProfileId,
      providerId: "deepseek",
      providerName: "DeepSeek",
      accountName: chat.accountName,
      transport: "browser",
    });
  }

  const recentProjects = projects.slice(0, 3);
  const recentConversations = conversations.slice(0, 3);

  return (
    <section className="dcode-home">
      <header className="dcode-home__hero">
        <DCodeBrand size={76} showTagline showSupportingLine />
        <div className="dcode-home__hero-actions">
          {activeProject ? (
            <Button trailingIcon={<ArrowRight size={16} />} onClick={() => continueProject(activeProject)}>
              Continue {activeProject.name}
            </Button>
          ) : (
            <Button trailingIcon={<ArrowRight size={16} />} onClick={() => setActiveWorkspace("projects")}>
              Open a project
            </Button>
          )}
          <Button variant="outline" onClick={() => setActiveWorkspace("chat")}>Open Chat</Button>
        </div>
      </header>

      <div className="dcode-home__status-grid">
        <div><span>Backend</span><strong className={backendConnected ? "is-ready" : ""}>{backendConnected === null ? "Checking" : backendConnected ? "Connected" : "Offline"}</strong></div>
        <div><span>Projects</span><strong>{projects.length}</strong></div>
        <div><span>Provider accounts</span><strong>{connectedProviders} connected</strong></div>
        <div><span>Conversations</span><strong>{conversations.length}</strong></div>
      </div>

      <div className="dcode-home__content-grid">
        <section className="dcode-home-panel">
          <div className="dcode-home-panel__header">
            <div><span className="dcode-page-eyebrow">Workspace</span><h2>Recent projects</h2></div>
            <Button variant="ghost" size="sm" onClick={() => setActiveWorkspace("projects")}>View all</Button>
          </div>
          <div className="dcode-home-list">
            {recentProjects.length ? recentProjects.map((project) => (
              <button type="button" key={project.id} onClick={() => continueProject(project)}>
                <span className="dcode-home-list__icon"><FolderGit2 size={18} /></span>
                <span className="dcode-home-list__copy"><strong>{project.name}</strong><small>{project.path}</small></span>
                <ArrowRight size={15} />
              </button>
            )) : <EmptyHomeItem icon={<FolderGit2 size={20} />} title="No projects yet" copy="Open a local repository to begin." />}
          </div>
        </section>

        <section className="dcode-home-panel">
          <div className="dcode-home-panel__header">
            <div><span className="dcode-page-eyebrow">AI workspace</span><h2>Recent conversations</h2></div>
            <Button variant="ghost" size="sm" onClick={() => setActiveWorkspace("chat")}>Open Chat</Button>
          </div>
          <div className="dcode-home-list">
            {recentConversations.length ? recentConversations.map((chat) => (
              <button type="button" key={chat.id} onClick={() => openConversation(chat)}>
                <span className="dcode-home-list__icon"><MessageSquare size={18} /></span>
                <span className="dcode-home-list__copy"><strong>{chat.title}</strong><small>{chat.accountName} · DeepSeek</small></span>
                <Badge variant="outline">{chat.status}</Badge>
              </button>
            )) : <EmptyHomeItem icon={<MessageSquare size={20} />} title="No conversations yet" copy="Start a provider-backed chat when you're ready." />}
          </div>
        </section>
      </div>

      <button type="button" className="dcode-home__provider-callout" onClick={() => setActiveWorkspace("providers")}>
        <span className="dcode-home-list__icon"><PlugZap size={19} /></span>
        <span><strong>Provider connections</strong><small>{connectedProviders ? `${connectedProviders} accounts ready to use` : "Connect an API or browser account"}</small></span>
        <ArrowRight size={16} />
      </button>
    </section>
  );
}

function EmptyHomeItem({ icon, title, copy }: { icon: ReactNode; title: string; copy: string }) {
  return (
    <div className="dcode-home-list__empty">
      <span className="dcode-home-list__icon">{icon}</span>
      <span><strong>{title}</strong><small>{copy}</small></span>
    </div>
  );
}
