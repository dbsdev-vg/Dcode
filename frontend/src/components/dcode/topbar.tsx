"use client";

import { useEffect, useState } from "react";
import { Avatar, IconButton } from "@dbs-studio/ui";
import { Bell, Server } from "lucide-react";
import { ThemeToggle } from "./theme-toggle";
import { useWorkspace } from "../../state/workspace-context";

const workspaceLabels = {
  home: "Home",
  projects: "Projects",
  files: "Project workspace",
  chat: "Chat",
  agents: "Agents",
  providers: "Providers",
  settings: "Settings",
};

export function Topbar() {
  const { activeWorkspace } = useWorkspace();
  const [backendStatus, setBackendStatus] = useState<"checking" | "connected" | "offline">("checking");
  const [showNotifications, setShowNotifications] = useState(false);

  useEffect(() => {
    let active = true;

    async function checkBackend() {
      try {
        const response = await fetch("http://localhost:5283/api/health");
        if (active) setBackendStatus(response.ok ? "connected" : "offline");
      } catch {
        if (active) setBackendStatus("offline");
      }
    }

    void checkBackend();
    const timer = window.setInterval(() => void checkBackend(), 15_000);

    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, []);

  const backendLabel = backendStatus === "connected"
    ? "Backend connected"
    : backendStatus === "offline"
      ? "Backend offline"
      : "Checking backend";

  return (
    <header className="dcode-topbar">
      <div className="dcode-topbar__location">
        <span>DCode</span><span aria-hidden="true">/</span><strong>{workspaceLabels[activeWorkspace]}</strong>
      </div>

      <div className="dcode-topbar__controls">
        <div
          className={`dcode-topbar__status is-${backendStatus}`}
          title={backendLabel}
          aria-label={backendLabel}
        >
          <Server size={14} />
          <span className="dcode-topbar__status-dot" aria-hidden="true" />
          <span>{backendStatus === "connected" ? "Connected" : backendStatus === "offline" ? "Offline" : "Checking"}</span>
        </div>

        <div className="dcode-topbar__notifications">
          <IconButton
            variant="ghost"
            label="Notifications"
            icon={<Bell size={18} />}
            aria-expanded={showNotifications}
            onClick={() => setShowNotifications((visible) => !visible)}
          />
          {showNotifications ? (
            <div className="dcode-topbar__notification-popover" role="status">
              <strong>You&apos;re all caught up</strong>
              <span>New DCode activity will appear here.</span>
            </div>
          ) : null}
        </div>

        <ThemeToggle />

        <div className="dcode-topbar__profile" title="Local profile">
          <Avatar name="Local User" size="sm" />
          <span>Local</span>
        </div>
      </div>
    </header>
  );
}
