"use client";

import { useEffect, useState } from "react";

type PanelState = { explorer: number; agent: number };

export function useWorkspacePanels(projectId: string) {
  const storageKey = `dcode:project:${projectId}:develop-panels`;
  const [panels, setPanels] = useState<PanelState>(() => {
    if (typeof window === "undefined") return { explorer: 260, agent: 440 };
    try {
      const stored = JSON.parse(window.localStorage.getItem(storageKey) ?? "null") as Partial<PanelState> | null;
      return { explorer: clamp(stored?.explorer ?? 260, 210, 420), agent: clamp(stored?.agent ?? 440, 360, 840) };
    } catch { return { explorer: 260, agent: 440 }; }
  });
  useEffect(() => { window.localStorage.setItem(storageKey, JSON.stringify(panels)); }, [panels, storageKey]);
  return { ...panels, setExplorer: (width: number) => setPanels((value) => ({ ...value, explorer: clamp(width, 210, 420) })), setAgent: (width: number) => setPanels((value) => ({ ...value, agent: clamp(width, 360, 840) })) };
}

function clamp(value: number, minimum: number, maximum: number) { return Math.min(maximum, Math.max(minimum, value)); }
