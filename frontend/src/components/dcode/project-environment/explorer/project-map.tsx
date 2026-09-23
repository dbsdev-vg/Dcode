import { Braces, ChevronDown, ChevronRight, File, FileCode2, FileJson, FileText, Folder, FolderOpen, GitBranch, Search, TriangleAlert } from "lucide-react";
import type { ReactNode } from "react";
import type { ProjectTreeNode } from "../../../../types/dcode";
import type { NavigatorMode } from "../domain";

export function ProjectMap({ mode, onMode, tree, activePath, expanded, errors, loading, error, onOpen, onToggle }: { mode: NavigatorMode; onMode(mode: NavigatorMode): void; tree: ProjectTreeNode[]; activePath: string | null; expanded: Set<string>; errors: Record<string, number>; loading: boolean; error: string | null; onOpen(node: ProjectTreeNode): void; onToggle(path: string): void }) {
  return <aside className="pe-map">
    <header className="pe-map__header"><strong>Project</strong><nav aria-label="Project navigation">{(["files", "search", "git"] as NavigatorMode[]).map((item) => <button key={item} title={item === "files" ? "Files" : item === "search" ? "Search" : "Changes"} className={mode === item ? "is-active" : ""} onClick={() => onMode(item)}>{item === "files" ? <Folder size={14} /> : item === "search" ? <Search size={14} /> : <GitBranch size={14} />}<span>{item === "files" ? "Files" : item === "search" ? "Search" : "Changes"}</span></button>)}</nav></header>
    {mode === "files" ? <div className="pe-map__body">
      <div className="pe-section-label"><span>Workspace files</span><span>{tree.length}</span></div>
      {loading ? <p className="pe-muted">Mapping workspace…</p> : null}{error ? <p className="pe-error">{error}</p> : null}
      {tree.map((node) => <TreeNode key={node.relativePath} node={node} depth={0} activePath={activePath} expanded={expanded} errors={errors} onOpen={onOpen} onToggle={onToggle} />)}
    </div> : <Unavailable icon={<Search size={18} />} title={mode === "search" ? "Project search" : "Source changes"} text={mode === "search" ? "Search UI will connect to the sandboxed project search API." : "Git inspection is not connected yet."} />}
  </aside>;
}

function TreeNode({ node, depth, activePath, expanded, errors, onOpen, onToggle }: { node: ProjectTreeNode; depth: number; activePath: string | null; expanded: Set<string>; errors: Record<string, number>; onOpen(node: ProjectTreeNode): void; onToggle(path: string): void }) {
  const open = expanded.has(node.relativePath); const count = treeErrors(node, errors);
  return <div className="pe-tree-node"><button className={`pe-tree-row ${activePath === node.relativePath ? "is-active" : ""} ${count ? "has-errors" : ""}`} style={{ paddingLeft: 12 + depth * 14 }} onClick={() => node.isDirectory ? onToggle(node.relativePath) : onOpen(node)} title={node.relativePath}>
    <span className="pe-tree-row__toggle">{node.isDirectory ? open ? <ChevronDown size={13} /> : <ChevronRight size={13} /> : null}</span>
    {node.isDirectory ? open ? <FolderOpen className="pe-icon--folder" size={14} /> : <Folder className="pe-icon--folder" size={14} /> : <FileIcon name={node.name} />}
    <span>{node.name}</span>{count ? <small><TriangleAlert size={11} />{count}</small> : null}
  </button>{node.isDirectory && open ? node.children?.map((child) => <TreeNode key={child.relativePath} node={child} depth={depth + 1} activePath={activePath} expanded={expanded} errors={errors} onOpen={onOpen} onToggle={onToggle} />) : null}</div>;
}
function treeErrors(node: ProjectTreeNode, errors: Record<string, number>): number { return node.isDirectory ? node.children?.reduce((sum, child) => sum + treeErrors(child, errors), 0) ?? 0 : errors[node.relativePath] ?? 0; }
function FileIcon({ name }: { name: string }) { const ext = name.split(".").pop()?.toLowerCase(); if (ext === "json") return <FileJson className="pe-icon--json" size={14} />; if (["ts", "tsx", "js", "jsx"].includes(ext ?? "")) return <FileCode2 className="pe-icon--typescript" size={14} />; if (ext === "cs") return <FileCode2 className="pe-icon--csharp" size={14} />; if (["md", "txt"].includes(ext ?? "")) return <FileText className="pe-icon--text" size={14} />; if (["css", "scss", "html"].includes(ext ?? "")) return <Braces className="pe-icon--style" size={14} />; return <File className="pe-icon--file" size={14} />; }
function Unavailable({ icon, title, text }: { icon: ReactNode; title: string; text: string }) { return <div className="pe-unavailable">{icon}<strong>{title}</strong><p>{text}</p><span>Not available in this build</span></div>; }
