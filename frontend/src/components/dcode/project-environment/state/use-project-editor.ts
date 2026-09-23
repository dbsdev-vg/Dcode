"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useMonaco } from "@monaco-editor/react";
import type { Project, ProjectTreeNode } from "../../../../types/dcode";
import type { EditorDocument, ProjectSession, RegisteredProject } from "../domain";
import { configureDCodeMonaco, editorLanguage, modelPath } from "../editor/monaco-themes";

const serverUrl = "http://localhost:5283";
const diagnosticExtensions = new Set(["js", "jsx", "json", "ts", "tsx"]);

async function register(project: Project): Promise<RegisteredProject> {
  const response = await fetch(`${serverUrl}/api/projects/open`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ path: project.path }) });
  if (!response.ok) throw new Error(`Unable to open project (HTTP ${response.status})`);
  return response.json() as Promise<RegisteredProject>;
}

async function readFile(projectId: string, relativePath: string) {
  const response = await fetch(`${serverUrl}/api/projects/${projectId}/file?${new URLSearchParams({ path: relativePath })}`);
  if (!response.ok) throw new Error(`Unable to open file (HTTP ${response.status})`);
  return response.json() as Promise<{ relativePath: string; content: string }>;
}

export function useProjectEditor(project: Project) {
  const monaco = useMonaco();
  const [registered, setRegistered] = useState<RegisteredProject | null>(null);
  const [tree, setTree] = useState<ProjectTreeNode[]>([]);
  const [documents, setDocuments] = useState<EditorDocument[]>([]);
  const [activePath, setActivePath] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [fileErrors, setFileErrors] = useState<Record<string, number>>({});
  const [loading, setLoading] = useState(true);
  const [savingPath, setSavingPath] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [theme, setTheme] = useState("dcode-dark");
  const ready = useRef(false);
  const documentsRef = useRef(documents);
  documentsRef.current = documents;

  useEffect(() => {
    const root = document.documentElement;
    const sync = () => setTheme(root.classList.contains("dark") ? "dcode-dark" : "dcode-light");
    sync(); const observer = new MutationObserver(sync); observer.observe(root, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    let alive = true; ready.current = false; setLoading(true); setError(null);
    void (async () => {
      try {
        const opened = await register(project);
        const [treeResponse, sessionResponse] = await Promise.all([fetch(`${serverUrl}/api/projects/${opened.id}/tree`), fetch(`${serverUrl}/api/projects/${opened.id}/session`)]);
        if (!treeResponse.ok) throw new Error(`Unable to load project files (HTTP ${treeResponse.status})`);
        const nextTree = await treeResponse.json() as ProjectTreeNode[];
        let nextDocuments: EditorDocument[] = [];
        let nextActive: string | null = null;
        if (sessionResponse.ok) {
          const session = await sessionResponse.json() as ProjectSession;
          const restored = await Promise.all(session.openTabs.map(async (path): Promise<EditorDocument | null> => { try { const file = await readFile(opened.id, path); return { relativePath: path, name: fileName(path), content: file.content, savedContent: file.content, loading: false, error: null }; } catch { return null; } }));
          nextDocuments = restored.filter((item): item is EditorDocument => item !== null);
          nextActive = nextDocuments.some((item) => item.relativePath === session.activeFilePath) ? session.activeFilePath : nextDocuments[0]?.relativePath ?? null;
          if (alive) setExpanded(new Set(session.expandedFolders));
        }
        if (!alive) return;
        setRegistered(opened); setTree(nextTree); setDocuments(nextDocuments); setActivePath(nextActive); ready.current = true;
      } catch (reason) { if (alive) setError(reason instanceof Error ? reason.message : "Unable to open project"); }
      finally { if (alive) setLoading(false); }
    })();
    return () => { alive = false; };
  }, [project]);

  useEffect(() => {
    if (!registered || !ready.current) return;
    const timer = window.setTimeout(() => void fetch(`${serverUrl}/api/projects/${registered.id}/session`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ activeFilePath: activePath, openTabs: documents.map((item) => item.relativePath), expandedFolders: [...expanded], layout: { explorerOpen: true, chatOpen: true, terminalOpen: false } }) }), 350);
    return () => window.clearTimeout(timer);
  }, [activePath, documents, expanded, registered]);

  useEffect(() => {
    if (!registered) return;
    const timer = window.setInterval(() => void (async () => {
      const response = await fetch(`${serverUrl}/api/projects/${registered.id}/tree`).catch(() => null);
      if (response?.ok) { const next = await response.json() as ProjectTreeNode[]; setTree((current) => JSON.stringify(current) === JSON.stringify(next) ? current : next); }
      await Promise.all(documentsRef.current.map(async (tab) => {
        try { const file = await readFile(registered.id, tab.relativePath); setDocuments((current) => current.map((item) => item.relativePath !== tab.relativePath || file.content === item.savedContent ? item : item.content === item.savedContent ? { ...item, content: file.content, savedContent: file.content } : { ...item, error: "Changed outside DCode. Reopen before saving." })); } catch { /* transient */ }
      }));
    })(), 2500);
    return () => window.clearInterval(timer);
  }, [registered]);

  useEffect(() => {
    if (!monaco || !registered || !tree.length) return;
    configureDCodeMonaco(monaco); let cancelled = false; const created: { dispose(): void }[] = []; const paths = new Map<string, string>();
    const subscription = monaco.editor.onDidChangeMarkers((resources) => setFileErrors((current) => { const next = { ...current }; resources.forEach((resource) => { const path = paths.get(resource.toString()); if (!path) return; const count = monaco.editor.getModelMarkers({ resource }).filter((marker) => marker.severity === monaco.MarkerSeverity.Error).length; if (count) next[path] = count; else delete next[path]; }); return next; }));
    void (async () => { for (const node of diagnosticFiles(tree)) { if (cancelled) break; const uri = monaco.Uri.parse(modelPath(node.relativePath)); paths.set(uri.toString(), node.relativePath); if (!monaco.editor.getModel(uri)) { try { const file = await readFile(registered.id, node.relativePath); if (!cancelled) created.push(monaco.editor.createModel(file.content, editorLanguage(node.name), uri)); } catch { /* diagnostic indexing is best effort */ } } } })();
    return () => { cancelled = true; subscription.dispose(); created.forEach((model) => model.dispose()); };
  }, [monaco, registered, tree]);

  const openFile = useCallback(async (node: ProjectTreeNode) => {
    if (!registered || node.isDirectory) return;
    setActivePath(node.relativePath);
    if (documentsRef.current.some((item) => item.relativePath === node.relativePath)) return;
    setDocuments((current) => [...current, { relativePath: node.relativePath, name: node.name, content: "", savedContent: "", loading: true, error: null }]);
    try { const file = await readFile(registered.id, node.relativePath); setDocuments((current) => current.map((item) => item.relativePath === node.relativePath ? { ...item, content: file.content, savedContent: file.content, loading: false } : item)); }
    catch (reason) { setDocuments((current) => current.map((item) => item.relativePath === node.relativePath ? { ...item, loading: false, error: reason instanceof Error ? reason.message : "Unable to open file" } : item)); }
  }, [registered]);

  const save = useCallback(async (document: EditorDocument) => {
    if (!registered || document.loading || document.error) return; setSavingPath(document.relativePath);
    try { const response = await fetch(`${serverUrl}/api/projects/${registered.id}/file`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ relativePath: document.relativePath, content: document.content }) }); if (!response.ok) throw new Error(`Unable to save file (HTTP ${response.status})`); setDocuments((current) => current.map((item) => item.relativePath === document.relativePath ? { ...item, savedContent: document.content } : item)); }
    catch (reason) { setDocuments((current) => current.map((item) => item.relativePath === document.relativePath ? { ...item, error: reason instanceof Error ? reason.message : "Unable to save file" } : item)); }
    finally { setSavingPath(null); }
  }, [registered]);

  const close = useCallback((path: string) => { const current = documentsRef.current; const tab = current.find((item) => item.relativePath === path); if (tab && tab.content !== tab.savedContent && !window.confirm(`Discard unsaved changes to ${tab.name}?`)) return; const index = current.findIndex((item) => item.relativePath === path); const remaining = current.filter((item) => item.relativePath !== path); setDocuments(remaining); setActivePath((value) => value === path ? remaining[Math.max(0, index - 1)]?.relativePath ?? null : value); }, []);
  const activeDocument = useMemo(() => documents.find((item) => item.relativePath === activePath) ?? null, [activePath, documents]);
  const toggleFolder = (path: string) => setExpanded((current) => { const next = new Set(current); if (next.has(path)) next.delete(path); else next.add(path); return next; });
  const updateContent = (path: string, content: string) => setDocuments((current) => current.map((item) => item.relativePath === path ? { ...item, content, error: null } : item));
  const updateErrors = (path: string, count: number) => setFileErrors((current) => { const next = { ...current }; if (count) next[path] = count; else delete next[path]; return next; });
  return { registered, tree, documents, activeDocument, activePath, setActivePath, expanded, toggleFolder, fileErrors, loading, error, theme, savingPath, openFile, save, close, updateContent, updateErrors };
}

const fileName = (path: string) => path.split(/[\\/]/).pop() ?? path;
function diagnosticFiles(nodes: ProjectTreeNode[]): ProjectTreeNode[] { return nodes.flatMap((node) => node.isDirectory ? diagnosticFiles(node.children ?? []) : diagnosticExtensions.has(node.name.split(".").pop()?.toLowerCase() ?? "") ? [node] : []); }
