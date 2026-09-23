"use client";

import { useEffect, type CSSProperties, type PointerEvent as ReactPointerEvent, type ReactNode } from "react";
import { ArrowLeft, Bot, FolderTree, Gauge, PanelBottom, PanelRightClose, PanelRightOpen, Save, Settings2, TerminalSquare, Users } from "lucide-react";
import { DCodeLogo } from "../../../brand/dcode-logo";
import { useProject } from "../../../../state/project-context";
import { useWorkspace } from "../../../../state/workspace-context";
import type { Project, ProjectTreeNode } from "../../../../types/dcode";
import { ProjectConversation } from "../chat/project-conversation";
import { ProjectCoordinate } from "../coordinate/project-coordinate";
import type { ControlMode, DockMode } from "../domain";
import { EditorStage } from "../editor/editor-stage";
import { ProjectMap } from "../explorer/project-map";
import { ProjectManagement } from "../management/project-management";
import { RunConfigurations } from "../runtime/run-configurations";
import { ProjectRunControl } from "../runtime/project-run-control";
import { useProjectEditor } from "../state/use-project-editor";
import { useProjectLayout } from "../state/use-project-layout";
import { useWorkspacePanels } from "../state/use-workspace-panels";
import styles from "../project-environment.module.css";

export function ProjectEnvironment() {
  const { activeProject } = useProject(); const { setActiveWorkspace } = useWorkspace();
  if (!activeProject) return <div className={styles.missing}>No project selected.</div>;
  return <ProjectEnvironmentReady project={activeProject} onBack={() => setActiveWorkspace("projects")} />;
}

function ProjectEnvironmentReady({ project, onBack }: { project: Project; onBack(): void }) {
  const editor = useProjectEditor(project); const layout = useProjectLayout(); const panels = useWorkspacePanels(editor.registered?.id ?? project.id);
  const projectId = editor.registered?.id ?? project.id; const problems = Object.values(editor.fileErrors).reduce((sum, count) => sum + count, 0);
  useEffect(() => { const handler = (event: KeyboardEvent) => { if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s" && editor.activeDocument) { event.preventDefault(); void editor.save(editor.activeDocument); } }; window.addEventListener("keydown", handler); return () => window.removeEventListener("keydown", handler); }, [editor]);
  const workspaceStyle = { "--explorer-width": `${panels.explorer}px`, "--agent-width": `${panels.agent}px` } as CSSProperties;
  const openControl = (mode: ControlMode) => { layout.setControlMode(mode); layout.setWorkstreamOpen(true); };
  return <section className={styles.environment}>
    <WorkspaceToolbar project={project} projectId={projectId} onBack={onBack} onSave={() => editor.activeDocument && void editor.save(editor.activeDocument)} saveDisabled={!editor.activeDocument || editor.savingPath === editor.activePath} onRun={() => { layout.setDockMode("terminal"); layout.setDockOpen(true); }} onProblems={() => { layout.setDockMode("problems"); layout.setDockOpen(true); }} problems={problems} navigatorOpen={layout.navigatorOpen} onNavigator={() => layout.setNavigatorOpen(!layout.navigatorOpen)} controlMode={layout.controlMode} controlOpen={layout.workstreamOpen} onControl={openControl} onCloseControl={() => layout.setWorkstreamOpen(false)} />
    <main className={styles.develop} style={workspaceStyle}><section className={`${styles.developGrid} ${!layout.navigatorOpen ? styles.withoutExplorer : ""} ${!layout.workstreamOpen ? styles.withoutAgent : ""}`}>
      {layout.navigatorOpen ? <><ProjectMap mode={layout.navigatorMode} onMode={layout.setNavigatorMode} tree={editor.tree} activePath={editor.activePath} expanded={editor.expanded} errors={editor.fileErrors} loading={editor.loading} error={editor.error} onOpen={editor.openFile} onToggle={editor.toggleFolder} /><Splitter side="explorer" onPointerDown={(event) => beginResize(event, panels.setExplorer)} /></> : null}
      <EditorStage documents={editor.documents} active={editor.activeDocument} activePath={editor.activePath} theme={editor.theme} saving={editor.savingPath === editor.activePath} onSelect={editor.setActivePath} onClose={editor.close} onChange={editor.updateContent} onSave={(document) => void editor.save(document)} onErrors={editor.updateErrors} />
      {layout.workstreamOpen ? <><Splitter side="agent" onPointerDown={(event) => beginResize(event, (x) => panels.setAgent(window.innerWidth - x))} /><ControlWorkbench mode={layout.controlMode} project={{ ...project, id: projectId }} projectId={editor.registered?.id ?? null} tree={editor.tree} /></> : null}
    </section>{layout.dockOpen ? <RuntimePanel projectId={projectId} active={layout.dockMode} onChange={layout.setDockMode} problems={problems} onClose={() => layout.setDockOpen(false)} /> : null}</main>
    <WorkspaceStatus activePath={editor.activeDocument?.relativePath ?? project.path} problems={problems} dockOpen={layout.dockOpen} onDock={() => layout.setDockOpen(!layout.dockOpen)} />
  </section>;
}

function WorkspaceToolbar({ project, projectId, onBack, onSave, saveDisabled, onRun, onProblems, problems, navigatorOpen, onNavigator, controlMode, controlOpen, onControl, onCloseControl }: { project: Project; projectId:string; onBack(): void; onSave(): void; saveDisabled: boolean; onRun(): void; onProblems():void; problems: number; navigatorOpen: boolean; onNavigator(): void; controlMode: ControlMode; controlOpen: boolean; onControl(mode: ControlMode): void; onCloseControl(): void }) {
  const controls: { mode: ControlMode; label: string; icon: ReactNode }[] = [{ mode: "agent", label: "Agent", icon: <Bot /> }, { mode: "team", label: "Team", icon: <Users /> }, { mode: "runs", label: "Runs", icon: <Gauge /> }, { mode: "project", label: "Project", icon: <Settings2 /> }];
  return <header className={styles.projectBar}><button className={styles.iconButton} onClick={onBack} aria-label="Back to projects"><ArrowLeft /></button><div className={styles.projectIdentity}><DCodeLogo size={24} /><div><strong>{project.name}</strong><span>{compactPath(project.path)}</span></div></div><div className={styles.workspaceTools}><button className={navigatorOpen ? styles.active : ""} onClick={onNavigator}><FolderTree />Files</button><span /><button disabled={saveDisabled} onClick={onSave}><Save />Save</button><ProjectRunControl projectId={projectId} onOpenRuntime={onRun}/>{problems ? <button className={styles.problemButton} onClick={onProblems}>{problems} problem{problems === 1 ? "" : "s"}</button> : null}</div><nav className={styles.controlNav}>{controls.map(item => <button key={item.mode} className={controlOpen && controlMode === item.mode ? styles.active : ""} onClick={() => onControl(item.mode)}>{item.icon}<span>{item.label}</span></button>)}<button className={styles.iconButton} onClick={controlOpen ? onCloseControl : () => onControl("agent")} title={controlOpen ? "Close workbench" : "Open workbench"}>{controlOpen ? <PanelRightClose /> : <PanelRightOpen />}</button></nav></header>;
}

function ControlWorkbench({ mode, project, projectId, tree }: { mode: ControlMode; project: Project; projectId: string | null; tree: ProjectTreeNode[] }) {
  return <aside className={styles.controlPane}><div className={styles.controlContent}>{mode === "agent" ? projectId ? <ProjectConversation projectId={projectId} /> : <Empty icon={<Bot />} title="Preparing DCode" text="Registering this project with the local runtime." /> : mode === "team" ? <ProjectManagement project={project} tree={tree} initialSection="agents" embedded /> : mode === "runs" ? <ProjectCoordinate projectId={projectId} /> : <ProjectManagement project={project} tree={tree} initialSection="overview" embedded />}</div></aside>;
}

function WorkspaceStatus({ activePath, problems, dockOpen, onDock }: { activePath: string; problems: number; dockOpen: boolean; onDock(): void }) { return <footer className={styles.workspaceStatus}><span className={styles.localState}><i />Local workspace</span><span className={styles.activePath}>{activePath}</span><button className={problems ? styles.hasProblems : ""} onClick={onDock}>{problems} problems</button><button className={dockOpen ? styles.active : ""} onClick={onDock}><PanelBottom />Runtime</button><span>UTF-8</span></footer>; }
function Splitter({ side, onPointerDown }: { side: "explorer" | "agent"; onPointerDown(event: ReactPointerEvent): void }) { return <div className={`${styles.splitter} ${styles[side]}`} role="separator" aria-orientation="vertical" onPointerDown={onPointerDown} />; }
function beginResize(event: ReactPointerEvent, update: (clientX: number) => void) { event.preventDefault(); const move = (pointer: PointerEvent) => update(pointer.clientX); const stop = () => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", stop); document.body.style.cursor = ""; }; document.body.style.cursor = "col-resize"; window.addEventListener("pointermove", move); window.addEventListener("pointerup", stop); }
function RuntimePanel({ projectId, active, onChange, problems, onClose }: { projectId: string; active: DockMode; onChange(mode: DockMode): void; problems: number; onClose(): void }) { return <section className={styles.runtimePanel}><header>{(["terminal", "problems", "output", "logs"] as DockMode[]).map(mode => <button key={mode} className={active === mode ? styles.active : ""} onClick={() => onChange(mode)}>{mode === "terminal" ? "Run" : mode}{mode === "problems" ? ` ${problems}` : ""}</button>)}<button onClick={onClose}>Close</button></header>{active === "terminal" ? <RunConfigurations projectId={projectId} /> : <Empty icon={<TerminalSquare />} title={active === "problems" ? `${problems} diagnostics` : `${active} unavailable`} text={active === "problems" ? "Diagnostics remain marked in project navigation and the editor." : "This runtime surface is not connected yet."} />}</section>; }
function Empty({ icon, title, text }: { icon: ReactNode; title: string; text: string }) { return <div className={styles.empty}>{icon}<strong>{title}</strong><p>{text}</p></div>; }
const compactPath = (path: string) => path.length > 64 ? `…${path.slice(-63)}` : path;
