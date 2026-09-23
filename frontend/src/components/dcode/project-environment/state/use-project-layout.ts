"use client";

import { useState } from "react";
import type { ControlMode, DockMode, NavigatorMode } from "../domain";

export function useProjectLayout() {
  const [controlMode, setControlMode] = useState<ControlMode>("agent");
  const [navigatorMode, setNavigatorMode] = useState<NavigatorMode>("files");
  const [dockMode, setDockMode] = useState<DockMode>("terminal");
  const [navigatorOpen, setNavigatorOpen] = useState(true);
  const [workstreamOpen, setWorkstreamOpen] = useState(true);
  const [dockOpen, setDockOpen] = useState(false);

  return { controlMode, setControlMode, navigatorMode, setNavigatorMode, dockMode, setDockMode, navigatorOpen, setNavigatorOpen, workstreamOpen, setWorkstreamOpen, dockOpen, setDockOpen };
}
