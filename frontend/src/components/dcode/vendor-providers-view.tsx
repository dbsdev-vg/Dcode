"use client";

import {
  Badge, Button, Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle, IconButton, Input,
} from "@dbs-studio/ui";
import {
  AppWindow, ArrowLeft, Bot, CheckCircle2, ChevronRight, CircleHelp, Eraser,
  ExternalLink, Eye, EyeOff, KeyRound, MessageSquare, MoreHorizontal, Pencil, Play, Plus,
  RefreshCw, RotateCw, ShieldCheck, Square, TestTube2, Trash2,
} from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";

import { useWorkspace } from "../../state/workspace-context";
import type { ChatConversation } from "../../types/dcode";
import { DCodeLogo } from "../brand/dcode-logo";

interface ProviderDefinition {
  id: string;
  name: string;
  description: string;
  endpoint: string;
  suggestedModel: string;
  browserAvailable: boolean;
}

interface BrowserProfile {
  id: string;
  name: string;
  headless: boolean;
  connected: boolean;
  status: "running" | "stopped";
}

interface BrowserConnectionTestResult {
  success: boolean;
  status: string;
  message: string;
}

const serverUrl = "http://localhost:5283";
const providerHistoryKey = "dcodeProvider";
const providers: ProviderDefinition[] = [
  { id: "deepseek", name: "DeepSeek", description: "DeepSeek models through an API key or independent web accounts.", endpoint: "api.deepseek.com", suggestedModel: "deepseek-chat", browserAvailable: true },
  { id: "openai", name: "OpenAI", description: "GPT models through the OpenAI API and, later, ChatGPT web sessions.", endpoint: "api.openai.com", suggestedModel: "gpt-5", browserAvailable: false },
  { id: "gemini", name: "Google Gemini", description: "Gemini models through Google AI Studio and future web sessions.", endpoint: "generativelanguage.googleapis.com", suggestedModel: "gemini-2.5-pro", browserAvailable: false },
];

export function VendorProvidersView() {
  const [selectedId, setSelectedId] = useState<string | null>(() => readSelectedProvider());
  const selectedProvider = providers.find(({ id }) => id === selectedId);

  useEffect(() => {
    function handlePopState(event: PopStateEvent) {
      const id = event.state?.[providerHistoryKey];
      setSelectedId(typeof id === "string" && providers.some((provider) => provider.id === id) ? id : null);
    }
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, []);

  function openProvider(id: string) {
    window.history.pushState({ ...window.history.state, [providerHistoryKey]: id }, "");
    setSelectedId(id);
  }

  function closeProvider() {
    if (window.history.state?.[providerHistoryKey]) window.history.back();
    else setSelectedId(null);
  }

  return selectedProvider
    ? <ProviderDetail provider={selectedProvider} onBack={closeProvider} />
    : <ProviderOverview onOpenProvider={openProvider} />;
}

function ProviderOverview({ onOpenProvider }: { onOpenProvider: (id: string) => void }) {
  const [profiles, setProfiles] = useState<BrowserProfile[]>([]);

  useEffect(() => {
    fetch(`${serverUrl}/api/providers/browser/deepseek/profiles`)
      .then((response) => response.ok ? response.json() as Promise<BrowserProfile[]> : [])
      .then(setProfiles)
      .catch(() => setProfiles([]));
  }, []);

  const running = profiles.filter((profile) => profile.status === "running").length;
  const connected = profiles.filter((profile) => profile.connected).length;
  return (
    <section className="dcode-providers-page">
      <header className="dcode-providers-header">
        <div className="dcode-providers-header__identity">
          <DCodeLogo size={40} />
          <div><span className="dcode-page-eyebrow">DCode infrastructure</span><h1>Providers</h1><p>Manage every way DCode can connect to each model provider.</p></div>
        </div>
      </header>
      <ProviderSecurityNotice />
      <div className="dcode-provider-section">
        <div className="dcode-provider-section__heading">
          <div><h2>Model providers</h2><p>API connections and browser accounts live together under their provider.</p></div>
          <Badge variant="soft">{providers.length} providers</Badge>
        </div>
        <div className="dcode-provider-grid">
          {providers.map((provider) => {
            const accountCount = provider.id === "deepseek" ? profiles.length : 0;
            const runningCount = provider.id === "deepseek" ? running : 0;
            const connectedCount = provider.id === "deepseek" ? connected : 0;
            return (
              <article className="dcode-provider-card dcode-provider-card--vendor" key={provider.id}>
                <div className="dcode-provider-card__topline">
                  <span className="dcode-provider-card__icon"><Bot size={21} /></span>
                  <Badge variant={connectedCount ? "success" : "outline"}>{connectedCount ? `${connectedCount} connected` : "Not connected"}</Badge>
                </div>
                <div className="dcode-provider-card__copy"><h3>{provider.name}</h3><p>{provider.description}</p></div>
                <div className="dcode-provider-capabilities">
                  <div><KeyRound size={15} /><span>API</span><Badge variant="outline">Disconnected</Badge></div>
                  <div><AppWindow size={15} /><span>Browser</span><Badge variant="outline">{provider.browserAvailable ? `${accountCount} accounts · ${runningCount} running` : "Coming later"}</Badge></div>
                </div>
                <div className="dcode-provider-card__actions">
                  <Button size="sm" variant="outline" trailingIcon={<ChevronRight size={14} />} onClick={() => onOpenProvider(provider.id)}>Open provider</Button>
                </div>
              </article>
            );
          })}
        </div>
      </div>
    </section>
  );
}

function ProviderDetail({ provider, onBack }: { provider: ProviderDefinition; onBack: () => void }) {
  return (
    <section className="dcode-providers-page">
      <header className="dcode-provider-detail-header">
        <IconButton label="Back to providers" icon={<ArrowLeft size={17} />} variant="ghost" onClick={onBack} />
        <span className="dcode-provider-card__icon"><Bot size={21} /></span>
        <div><span className="dcode-page-eyebrow">Provider</span><h1>{provider.name}</h1><p>{provider.description}</p></div>
      </header>
      <ProviderSecurityNotice />
      <ApiConnection provider={provider} />
      {provider.browserAvailable ? <BrowserAccounts /> : (
        <section className="dcode-provider-panel">
          <div className="dcode-provider-panel__icon"><AppWindow size={19} /></div>
          <div className="dcode-provider-panel__body"><h2>Browser accounts</h2><p>A managed browser adapter for {provider.name} is not available yet.</p></div>
          <Badge variant="outline">Coming later</Badge>
        </section>
      )}
    </section>
  );
}

function ProviderSecurityNotice() {
  return <div className="dcode-provider-summary"><div><ShieldCheck size={18} /><span>Credentials and browser profiles are stored by DCode.Server, never in the web interface.</span></div><Badge variant="outline">Local-first</Badge></div>;
}

function ApiConnection({ provider }: { provider: ProviderDefinition }) {
  return (
    <section className="dcode-provider-panel">
      <div className="dcode-provider-panel__icon"><KeyRound size={19} /></div>
      <div className="dcode-provider-panel__body">
        <div className="dcode-provider-panel__heading"><div><h2>API connection</h2><p>Direct model access using a provider-issued API key.</p></div><Badge variant="outline">Disconnected</Badge></div>
        <dl className="dcode-provider-card__details"><div><dt>Endpoint</dt><dd>{provider.endpoint}</dd></div><div><dt>Default model</dt><dd>{provider.suggestedModel}</dd></div></dl>
        <Button size="sm" variant="outline" disabled>Configure API</Button>
        <p className="dcode-provider-helper">Credential vault support is required before API keys can be saved.</p>
      </div>
    </section>
  );
}

function BrowserAccounts() {
  const { startChat } = useWorkspace();
  const [profiles, setProfiles] = useState<BrowserProfile[]>([]);
  const [name, setName] = useState("");
  const [setupOpen, setSetupOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<BrowserProfile | null>(null);
  const [openMenuId, setOpenMenuId] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [successTitle, setSuccessTitle] = useState("DeepSeek connected");
  const knownConnections = useRef<Map<string, boolean> | null>(null);

  const loadProfiles = useCallback(async (quiet = false) => {
    if (!quiet) setLoading(true);
    try {
      const response = await fetch(`${serverUrl}/api/providers/browser/deepseek/profiles`);
      if (!response.ok) throw new Error(`Unable to load profiles (HTTP ${response.status})`);
      const nextProfiles = (await response.json()) as BrowserProfile[];
      if (knownConnections.current !== null) {
        const newlyConnected = nextProfiles.find(
          (profile) => profile.connected && knownConnections.current?.get(profile.id) === false,
        );
        if (newlyConnected) {
          setSuccessTitle("DeepSeek connected");
          setSuccessMessage(`${newlyConnected.name} connected successfully.`);
        }
      }
      knownConnections.current = new Map(nextProfiles.map((profile) => [profile.id, profile.connected]));
      setProfiles(nextProfiles);
      setError(null);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Unable to load profiles"); }
    finally { if (!quiet) setLoading(false); }
  }, []);

  useEffect(() => {
    void loadProfiles();
    const timer = window.setInterval(() => void loadProfiles(true), 3000);
    return () => window.clearInterval(timer);
  }, [loadProfiles]);

  useEffect(() => {
    if (!successMessage) return;
    const timer = window.setTimeout(() => setSuccessMessage(null), 4500);
    return () => window.clearTimeout(timer);
  }, [successMessage]);

  async function createProfile() {
    if (!name.trim()) return;
    setBusy("new"); setError(null);
    try {
      const response = await fetch(`${serverUrl}/api/providers/browser/deepseek/profiles`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: name.trim(), headless: false, browserChannel: "msedge" }) });
      if (!response.ok) throw new Error(await readError(response));
      const profile = (await response.json()) as BrowserProfile;
      const startResponse = await fetch(`${serverUrl}/api/providers/browser/deepseek/profiles/${profile.id}/start`, { method: "POST" });
      if (!startResponse.ok) {
        await loadProfiles(true);
        throw new Error(`Account created, but the login browser could not start. ${await readError(startResponse)}`);
      }
      activateBrowserWindow();
      setName(""); setSetupOpen(false); await loadProfiles(true);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Unable to add account"); }
    finally { setBusy(null); }
  }

  async function setSession(profile: BrowserProfile, action: "start" | "stop") {
    setBusy(profile.id); setError(null);
    try {
      const response = await fetch(`${serverUrl}/api/providers/browser/deepseek/profiles/${profile.id}/${action}`, { method: "POST" });
      if (!response.ok) throw new Error(await readError(response));
      if (action === "start") activateBrowserWindow();
      await loadProfiles(true);
    } catch (caught) { setError(caught instanceof Error ? caught.message : `Unable to ${action} session`); }
    finally { setBusy(null); }
  }

  async function deleteProfile(profile: BrowserProfile) {
    setBusy(profile.id); setError(null);
    try {
      const response = await fetch(`${serverUrl}/api/providers/browser/deepseek/profiles/${profile.id}`, { method: "DELETE" });
      if (!response.ok && response.status !== 204) throw new Error(await readError(response));
      setDeleteTarget(null);
      await loadProfiles(true);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Unable to delete the account"); }
    finally { setBusy(null); }
  }

  async function testConnection(profile: BrowserProfile) {
    setBusy(profile.id); setError(null);
    try {
      const response = await fetch(`${serverUrl}/api/providers/browser/deepseek/profiles/${profile.id}/test`, { method: "POST" });
      if (!response.ok) throw new Error(await readError(response));
      const result = (await response.json()) as BrowserConnectionTestResult;
      if (!result.success) {
        setError(`${profile.name}: ${result.message}`);
        return;
      }
      setSuccessTitle("Connection verified");
      setSuccessMessage(`${profile.name}: ${result.message}`);
      await loadProfiles(true);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Unable to test the connection"); }
    finally { setBusy(null); }
  }

  async function setExecutionMode(profile: BrowserProfile, headless: boolean) {
    setBusy(profile.id); setError(null); setOpenMenuId(null);
    try {
      const response = await fetch(`${serverUrl}/api/providers/browser/deepseek/profiles/${profile.id}`, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ headless }) });
      if (!response.ok) throw new Error(await readError(response));
      await loadProfiles(true);
      setSuccessTitle("Browser mode updated");
      setSuccessMessage(`${profile.name} Agent sessions will now run ${headless ? "headless" : "in a visible browser"}.`);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Unable to update browser mode"); }
    finally { setBusy(null); }
  }

  async function startPersistedChat(profile: BrowserProfile) {
    setBusy(profile.id); setError(null);
    try {
      const response = await fetch(`${serverUrl}/api/chats`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ browserProfileId: profile.id }),
      });
      if (!response.ok) throw new Error(await readError(response));
      const conversation = (await response.json()) as ChatConversation;
      startChat({ conversationId: conversation.id, profileId: profile.id, providerId: "deepseek", providerName: "DeepSeek", accountName: profile.name, transport: "browser" });
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Unable to create the chat"); }
    finally { setBusy(null); }
  }

  const running = profiles.filter((profile) => profile.status === "running").length;
  return (
    <section className="dcode-provider-panel dcode-provider-panel--stacked">
      <div className="dcode-provider-panel__icon"><AppWindow size={19} /></div>
      <div className="dcode-provider-panel__body">
        <div className="dcode-provider-panel__heading">
          <div><h2>Browser accounts</h2><p>Independent persistent DeepSeek sessions managed through Playwright.</p></div>
          <div className="dcode-provider-panel__tools"><Badge variant={running ? "success" : "outline"}>{running} running</Badge><Button size="sm" variant="ghost" icon={<RefreshCw size={14} />} onClick={() => void loadProfiles()}>Refresh</Button><Button size="sm" icon={<Plus size={14} />} onClick={() => setSetupOpen(true)}>Add browser account</Button></div>
        </div>
        <div className="dcode-browser-notice"><CircleHelp size={17} /><span>After you sign in, DCode detects the DeepSeek chat screen and saves the connection automatically. You can then close the browser while the account stays connected.</span><Button size="sm" variant="link" trailingIcon={<ExternalLink size={13} />} disabled>Adapter design</Button></div>
        {error ? <div className="dcode-provider-error" role="alert">{error}</div> : null}
        {loading ? <p className="dcode-provider-empty">Loading browser accounts...</p> : null}
        {!loading && !profiles.length ? <p className="dcode-provider-empty">No browser accounts yet.</p> : null}
        <div className="dcode-provider-grid dcode-provider-grid--profiles">
          {profiles.map((profile) => (
            <article className="dcode-provider-card dcode-provider-card--profile" key={profile.id}>
              <div className="dcode-provider-card__topline"><span className="dcode-provider-card__icon"><AppWindow size={21} /></span><div className="dcode-provider-card__statuses"><Badge variant={profile.connected ? "success" : "warning"}>{profile.connected ? "Connected" : "Login required"}</Badge><Badge variant="outline">{profile.status === "running" ? "Browser open" : "Browser closed"}</Badge></div></div>
              <div className="dcode-provider-card__copy"><h3>{profile.name}</h3><p>Independent DeepSeek web account with persistent login data.</p></div>
              <dl className="dcode-provider-card__details"><div><dt>Browser</dt><dd>Microsoft Edge</dd></div><div><dt>Mode</dt><dd>{profile.headless ? "Headless" : "Visible"}</dd></div></dl>
              <div className="dcode-provider-card__actions dcode-provider-card__actions--account">
                <Button size="sm" icon={<MessageSquare size={14} />} disabled={!profile.connected || busy === profile.id} title={profile.connected ? "Start a chat with this account" : "Sign in before starting chat"} onClick={() => void startPersistedChat(profile)}>{busy === profile.id ? "Starting..." : "Start chat"}</Button>
                <Button size="sm" variant="outline" icon={<TestTube2 size={14} />} disabled={!profile.connected || busy === profile.id} title={profile.connected ? "Verify the saved DeepSeek login" : "Sign in before testing"} onClick={() => void testConnection(profile)}>{busy === profile.id ? "Testing..." : "Test"}</Button>
                <div className="dcode-provider-overflow">
                  <IconButton label={`More actions for ${profile.name}`} icon={<MoreHorizontal size={16} />} variant="ghost" disabled={busy === profile.id} aria-expanded={openMenuId === profile.id} onClick={() => setOpenMenuId((current) => current === profile.id ? null : profile.id)} />
                  {openMenuId === profile.id ? (
                    <div className="dcode-provider-overflow__menu" role="menu">
                      <button type="button" role="menuitem" onClick={() => { setOpenMenuId(null); void setSession(profile, profile.status === "running" ? "stop" : "start"); }}>{profile.status === "running" ? <Square size={14} /> : <Play size={14} />}<span>{profile.status === "running" ? "Close browser" : "Open browser"}</span></button>
                      <button type="button" role="menuitem" onClick={() => void setExecutionMode(profile, !profile.headless)}>{profile.headless ? <Eye size={14} /> : <EyeOff size={14} />}<span>{profile.headless ? "Show Agent browser" : "Run Agents headless"}</span></button>
                      <button type="button" role="menuitem" disabled title="Reconnect backend is not implemented yet"><RotateCw size={14} /><span>Reconnect</span></button>
                      <button type="button" role="menuitem" disabled title="Rename backend is not implemented yet"><Pencil size={14} /><span>Rename</span></button>
                      <button type="button" role="menuitem" disabled title="Clear-data backend is not implemented yet"><Eraser size={14} /><span>Clear data</span></button>
                      <div className="dcode-provider-overflow__separator" />
                      <button type="button" role="menuitem" className="is-destructive" onClick={() => { setOpenMenuId(null); setDeleteTarget(profile); }}><Trash2 size={14} /><span>Delete</span></button>
                    </div>
                  ) : null}
                </div>
              </div>
            </article>
          ))}
        </div>
      </div>

      <Dialog open={setupOpen} onOpenChange={(open) => { setSetupOpen(open); if (!open && busy !== "new") setName(""); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Add DeepSeek browser account</DialogTitle>
            <DialogDescription>Create an isolated local Edge profile. Its browser window will open immediately so you can sign in to DeepSeek.</DialogDescription>
          </DialogHeader>

          <div className="dcode-browser-setup-form">
            <label htmlFor="deepseek-profile-name">Account name</label>
            <Input id="deepseek-profile-name" value={name} onChange={(event) => setName(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter" && name.trim()) void createProfile(); }} placeholder="Work, Personal, Client..." autoFocus />

            <div className="dcode-browser-setup-row">
              <div><span>Browser</span><strong>Microsoft Edge</strong></div>
              <Badge variant="outline">Persistent profile</Badge>
            </div>

            <div className="dcode-browser-setup-note"><ShieldCheck size={16} /><span>DCode stores browser cookies in this isolated profile, but never asks for or stores your DeepSeek password.</span></div>
          </div>

          <DialogFooter>
            <Button variant="ghost" disabled={busy === "new"} onClick={() => setSetupOpen(false)}>Cancel</Button>
            <Button disabled={!name.trim() || busy === "new"} onClick={() => void createProfile()}>{busy === "new" ? "Creating and opening..." : "Create and sign in"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={deleteTarget !== null} onOpenChange={(open) => { if (!open && busy === null) setDeleteTarget(null); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete browser account?</DialogTitle>
            <DialogDescription>This will stop {deleteTarget?.name}, remove its saved DeepSeek login, cookies, and isolated browser profile from this device. This cannot be undone.</DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" disabled={busy !== null} onClick={() => setDeleteTarget(null)}>Cancel</Button>
            <Button variant="destructive" disabled={!deleteTarget || busy !== null} onClick={() => { if (deleteTarget) void deleteProfile(deleteTarget); }}>{busy ? "Deleting..." : "Delete account"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {successMessage ? (
        <div className="dcode-provider-toast" role="status" aria-live="polite">
          <CheckCircle2 size={18} />
          <div><strong>{successTitle}</strong><span>{successMessage}</span></div>
        </div>
      ) : null}
    </section>
  );
}

function readSelectedProvider() {
  if (typeof window === "undefined") return null;
  const id = window.history.state?.[providerHistoryKey];
  return typeof id === "string" ? id : null;
}

async function readError(response: Response) {
  try { const body = (await response.json()) as { error?: string; detail?: string }; return body.error ?? body.detail ?? `Request failed (HTTP ${response.status})`; }
  catch { return `Request failed (HTTP ${response.status})`; }
}

function activateBrowserWindow() {
  const hostWindow = window as Window & {
    chrome?: { webview?: { postMessage: (message: unknown) => void } };
  };
  hostWindow.chrome?.webview?.postMessage({ type: "activate-browser-window" });
}
