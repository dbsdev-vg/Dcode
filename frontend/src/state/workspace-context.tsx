"use client";

import {
  useCallback,
  createContext,
  useEffect,
  useContext,
  useState,
  type ReactNode,
} from "react";

import type { ChatProviderSession, Workspace } from "../types/dcode";

interface WorkspaceContextValue {
  activeWorkspace: Workspace;
  setActiveWorkspace: (workspace: Workspace) => void;
  chatProviderSession: ChatProviderSession | null;
  startChat: (session: ChatProviderSession) => void;
  closeChat: () => void;
}

const WorkspaceContext =
  createContext<WorkspaceContextValue | null>(null);

interface WorkspaceProviderProps {
  children: ReactNode;
}

const historyStateKey = "dcodeWorkspace";
const chatSessionHistoryKey = "dcodeChatProviderSession";

function isChatProviderSession(value: unknown): value is ChatProviderSession {
  if (!value || typeof value !== "object") return false;
  const session = value as Partial<ChatProviderSession>;
  return typeof session.conversationId === "string"
    && typeof session.profileId === "string"
    && session.providerId === "deepseek"
    && typeof session.providerName === "string"
    && typeof session.accountName === "string"
    && session.transport === "browser";
}

function isWorkspace(value: unknown): value is Workspace {
  return value === "home"
    || value === "projects"
    || value === "files"
    || value === "chat"
    || value === "agents"
    || value === "providers"
    || value === "settings";
}

export function WorkspaceProvider({
  children,
}: WorkspaceProviderProps): ReactNode {
  const [activeWorkspace, setActiveWorkspace] =
    useState<Workspace>("home");
  const [chatProviderSession, setChatProviderSession] =
    useState<ChatProviderSession | null>(null);

  useEffect(() => {
    window.history.replaceState(
      {
        ...window.history.state,
        [historyStateKey]: "home",
      },
      "",
    );

    function handlePopState(event: PopStateEvent) {
      const workspace = event.state?.[historyStateKey];

      setActiveWorkspace(
        isWorkspace(workspace) ? workspace : "home",
      );
      const chatSession = event.state?.[chatSessionHistoryKey];
      setChatProviderSession(
        isChatProviderSession(chatSession) ? chatSession : null,
      );
    }

    window.addEventListener("popstate", handlePopState);

    return () => {
      window.removeEventListener("popstate", handlePopState);
    };
  }, []);

  const startChat = useCallback((session: ChatProviderSession) => {
    window.history.pushState(
      {
        ...window.history.state,
        [historyStateKey]: "chat",
        [chatSessionHistoryKey]: session,
      },
      "",
    );
    setChatProviderSession(session);
    setActiveWorkspace("chat");
  }, []);

  const closeChat = useCallback(() => {
    const nextState = { ...window.history.state };
    delete nextState[chatSessionHistoryKey];
    window.history.pushState(
      { ...nextState, [historyStateKey]: "chat" },
      "",
    );
    setChatProviderSession(null);
    setActiveWorkspace("chat");
  }, []);

  const navigateToWorkspace = useCallback((workspace: Workspace) => {
    setActiveWorkspace((current) => {
      if (current === workspace) {
        return current;
      }

      window.history.pushState(
        {
          ...window.history.state,
          [historyStateKey]: workspace,
        },
        "",
      );

      return workspace;
    });
  }, []);

  return (
    <WorkspaceContext.Provider
      value={{
        activeWorkspace,
        setActiveWorkspace: navigateToWorkspace,
        chatProviderSession,
        startChat,
        closeChat,
      }}
    >
      {children}
    </WorkspaceContext.Provider>
  );
}

export function useWorkspace(): WorkspaceContextValue {
  const context = useContext(WorkspaceContext);

  if (context === null) {
    throw new Error(
      "useWorkspace must be used inside WorkspaceProvider",
    );
  }

  return context;
}
