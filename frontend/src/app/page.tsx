import { AppShell } from "@/components/dcode/app-shell";
import { WorkspaceView } from "../components/dcode/workspace-view";

export default function Home() {
  return (
    <AppShell>
      <WorkspaceView />
    </AppShell>
  );
}