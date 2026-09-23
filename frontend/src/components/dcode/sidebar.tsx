"use client";

import {
  Folder,
  FolderOpen,
  House,
  MessageSquare,
  PlugZap,
  Settings,
  Users,
} from "lucide-react";

import { useWorkspace } from "../../state/workspace-context";
import type { Workspace } from "../../types/dcode";
import { DCodeBrand } from "../brand/dcode-brand";

const navigation: {
  label: string;
  items: {
    label: string;
    workspace: Workspace;
    icon: React.ComponentType<{ size?: number }>;
  }[];
}[] = [
  {
    label: "WORKSPACE",
    items: [
      { label: "Home", workspace: "home", icon: House },
      { label: "Projects", workspace: "projects", icon: Folder },
      { label: "Files", workspace: "files", icon: FolderOpen },
    ],
  },
  {
    label: "AI",
    items: [
      { label: "Chat", workspace: "chat", icon: MessageSquare },
      { label: "Agents", workspace: "agents", icon: Users },
      { label: "Providers", workspace: "providers", icon: PlugZap },
    ],
  },
];

export function Sidebar() {
  const { activeWorkspace, setActiveWorkspace } = useWorkspace();

  return (
    <aside className="dcode-sidebar">
      <button
        type="button"
        className="dcode-brand-button"
        aria-label="Go to DCode Home"
        onClick={() => setActiveWorkspace("home")}
      >
        <DCodeBrand className="dcode-brand" size={34} showTagline />
      </button>

      <nav className="dcode-nav">
        {navigation.map((section) => (
          <div className="dcode-nav__section" key={section.label}>
            <div className="dcode-nav__label">{section.label}</div>

            {section.items.map((item) => {
              const Icon = item.icon;
              const isActive = activeWorkspace === item.workspace;

              return (
                <button
                  type="button"
                  key={item.workspace}
                  className={`dcode-nav__item ${
                    isActive ? "is-active" : ""
                  }`}
                  onClick={() => setActiveWorkspace(item.workspace)}
                >
                  <span className="dcode-nav__icon"><Icon size={16} /></span>
                  <span>{item.label}</span>
                </button>
              );
            })}
          </div>
        ))}

      </nav>

      <div className="dcode-sidebar__footer">
        <button
          type="button"
          className={`dcode-nav__item dcode-settings ${
            activeWorkspace === "settings" ? "is-active" : ""
          }`}
          onClick={() => setActiveWorkspace("settings")}
        >
          <span className="dcode-nav__icon"><Settings size={16} /></span>
          <span>Settings</span>
        </button>
      </div>
    </aside>
  );
}
