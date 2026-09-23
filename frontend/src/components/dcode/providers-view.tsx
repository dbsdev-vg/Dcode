"use client";

import { Badge, Button, IconButton, Switch } from "@dbs-studio/ui";
import {
  Bot,
  AppWindow,
  CircleHelp,
  ExternalLink,
  Globe2,
  KeyRound,
  MoreHorizontal,
  Play,
  Plus,
  RefreshCw,
  ShieldCheck,
  Square,
} from "lucide-react";
import { useCallback, useEffect, useState } from "react";

type ProviderMode = "api" | "browser";

interface ApiProviderDefinition {
  id: string;
  name: string;
  description: string;
  endpoint: string;
  suggestedModel: string;
}

interface BrowserProfile {
  id: string;
  providerType: string;
  name: string;
  browserChannel: string;
  headless: boolean;
  status: "running" | "stopped";
  error: string | null;
}

const serverUrl = "http://localhost:5283";

const apiProviders: ApiProviderDefinition[] = [
  {
    id: "openai",
    name: "OpenAI",
    description: "GPT models through the OpenAI API.",
    endpoint: "api.openai.com",
    suggestedModel: "gpt-5",
  },
  {
    id: "deepseek",
    name: "DeepSeek",
    description: "DeepSeek chat and reasoning models.",
    endpoint: "api.deepseek.com",
    suggestedModel: "deepseek-chat",
  },
  {
    id: "gemini",
    name: "Google Gemini",
    description: "Gemini models through Google AI Studio.",
    endpoint: "generativelanguage.googleapis.com",
    suggestedModel: "gemini-2.5-pro",
  },
];

export function ProvidersView() {
  const [mode, setMode] = useState<ProviderMode>("api");
  const [headless, setHeadless] = useState(false);

  return (
    <section className="dcode-providers-page">
      <header className="dcode-providers-header">
        <div>
          <span className="dcode-page-eyebrow">AI infrastructure</span>
          <h1>Providers</h1>
          <p>
            Connect model APIs or managed Playwright browser sessions.
          </p>
        </div>

        <Button
          icon={<Plus size={16} />}
          disabled={mode !== "browser"}
          onClick={() => document.getElementById("deepseek-profile-name")?.focus()}
        >
          Add account
        </Button>
      </header>

      <div className="dcode-provider-summary">
        <div>
          <ShieldCheck size={18} />
          <span>
            Credentials and browser profiles will be stored by DCode.Server,
            never in the web interface.
          </span>
        </div>
        <Badge variant="outline">Local-first</Badge>
      </div>

      <div className="dcode-provider-mode" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={mode === "api"}
          className={mode === "api" ? "is-active" : ""}
          onClick={() => setMode("api")}
        >
          <KeyRound size={16} />
          <span>API providers</span>
          <Badge variant="soft">{apiProviders.length}</Badge>
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={mode === "browser"}
          className={mode === "browser" ? "is-active" : ""}
          onClick={() => setMode("browser")}
        >
          <Globe2 size={16} />
          <span>Browser providers</span>
          <Badge variant="soft">Playwright</Badge>
        </button>
      </div>

      {mode === "api" ? (
        <ApiProviders />
      ) : (
        <BrowserProviders
          headless={headless}
          onHeadlessChange={setHeadless}
        />
      )}
    </section>
  );
}

function ApiProviders() {
  return (
    <div className="dcode-provider-section">
      <div className="dcode-provider-section__heading">
        <div>
          <h2>Model APIs</h2>
          <p>Direct API access with explicit model selection and usage.</p>
        </div>
        <Badge variant="warning">Credential vault required</Badge>
      </div>

      <div className="dcode-provider-grid">
        {apiProviders.map((provider) => (
          <article className="dcode-provider-card" key={provider.id}>
            <div className="dcode-provider-card__topline">
              <span className="dcode-provider-card__icon">
                <Bot size={21} />
              </span>
              <Badge variant="outline">Disconnected</Badge>
            </div>

            <div className="dcode-provider-card__copy">
              <h3>{provider.name}</h3>
              <p>{provider.description}</p>
            </div>

            <dl className="dcode-provider-card__details">
              <div>
                <dt>Endpoint</dt>
                <dd>{provider.endpoint}</dd>
              </div>
              <div>
                <dt>Default model</dt>
                <dd>{provider.suggestedModel}</dd>
              </div>
            </dl>

            <div className="dcode-provider-card__actions">
              <Button size="sm" variant="outline" disabled>
                Configure API
              </Button>
              <IconButton
                label={`More ${provider.name} actions`}
                icon={<MoreHorizontal size={16} />}
                variant="ghost"
                disabled
              />
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}

function BrowserProviders({
  headless,
  onHeadlessChange,
}: {
  headless: boolean;
  onHeadlessChange: (checked: boolean) => void;
}) {
  const [profiles, setProfiles] = useState<BrowserProfile[]>([]);
  const [name, setName] = useState("");
  const [busyProfile, setBusyProfile] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadProfiles = useCallback(async (quiet = false) => {
    if (!quiet) setLoading(true);
    try {
      const response = await fetch(
        `${serverUrl}/api/providers/browser/deepseek/profiles`,
      );
      if (!response.ok) throw new Error(`Unable to load profiles (HTTP ${response.status})`);
      setProfiles((await response.json()) as BrowserProfile[]);
      setError(null);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to load profiles");
    } finally {
      if (!quiet) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadProfiles();
    const timer = window.setInterval(() => void loadProfiles(true), 3000);
    return () => window.clearInterval(timer);
  }, [loadProfiles]);

  async function createProfile() {
    const profileName = name.trim();
    if (!profileName) return;
    setBusyProfile("new");
    setError(null);
    try {
      const response = await fetch(
        `${serverUrl}/api/providers/browser/deepseek/profiles`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ name: profileName, headless, browserChannel: "msedge" }),
        },
      );
      if (!response.ok) throw new Error(await readError(response));
      setName("");
      await loadProfiles(true);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to add account");
    } finally {
      setBusyProfile(null);
    }
  }

  async function setSession(profile: BrowserProfile, action: "start" | "stop") {
    setBusyProfile(profile.id);
    setError(null);
    try {
      const response = await fetch(
        `${serverUrl}/api/providers/browser/deepseek/profiles/${profile.id}/${action}`,
        { method: "POST" },
      );
      if (!response.ok) throw new Error(await readError(response));
      await loadProfiles(true);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : `Unable to ${action} session`);
    } finally {
      setBusyProfile(null);
    }
  }

  return (
    <div className="dcode-provider-section">
      <div className="dcode-provider-section__heading">
        <div>
          <h2>Managed browser sessions</h2>
          <p>
            Provider adapters drive isolated Playwright profiles through
            DCode.Server.
          </p>
        </div>
        <Button size="sm" variant="ghost" icon={<RefreshCw size={14} />} onClick={() => void loadProfiles()}>
          Refresh
        </Button>
      </div>

      <div className="dcode-browser-notice">
        <CircleHelp size={17} />
        <span>
          Every DeepSeek account gets an isolated persistent Edge profile. Sign
          in in the launched browser; DCode never stores your password.
        </span>
        <Button
          size="sm"
          variant="link"
          trailingIcon={<ExternalLink size={13} />}
          disabled
        >
          Adapter design
        </Button>
      </div>

      <div className="dcode-browser-profile-form">
        <label htmlFor="deepseek-profile-name">New DeepSeek account</label>
        <div>
          <input
            id="deepseek-profile-name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            onKeyDown={(event) => { if (event.key === "Enter") void createProfile(); }}
            placeholder="Work, Personal, Client..."
          />
          <Switch id="provider-headless" checked={headless} onCheckedChange={onHeadlessChange} label="Headless" />
          <Button size="sm" icon={<Plus size={14} />} disabled={!name.trim() || busyProfile === "new"} onClick={() => void createProfile()}>
            Add account
          </Button>
        </div>
        <p>Use visible mode for the first launch so you can sign in.</p>
      </div>

      {error ? <div className="dcode-provider-error" role="alert">{error}</div> : null}

      {loading ? <p className="dcode-provider-empty">Loading browser accounts…</p> : null}
      {!loading && profiles.length === 0 ? (
        <p className="dcode-provider-empty">No DeepSeek accounts yet. Create one above to get started.</p>
      ) : null}

      <div className="dcode-provider-grid">
        {profiles.map((profile) => (
          <article className="dcode-provider-card" key={profile.id}>
            <div className="dcode-provider-card__topline">
              <span className="dcode-provider-card__icon">
                <AppWindow size={21} />
              </span>
              <Badge variant={profile.status === "running" ? "success" : "outline"}>
                {profile.status === "running" ? "Running" : "Stopped"}
              </Badge>
            </div>

            <div className="dcode-provider-card__copy">
              <h3>{profile.name}</h3>
              <p>Independent DeepSeek web session with its own cookies and login.</p>
            </div>

            <dl className="dcode-provider-card__details">
              <div>
                <dt>Profile</dt>
                <dd>DeepSeek Web</dd>
              </div>
              <div>
                <dt>Browser</dt>
                <dd>Microsoft Edge</dd>
              </div>
              <div>
                <dt>Mode</dt>
                <dd>{profile.headless ? "Headless" : "Visible"}</dd>
              </div>
            </dl>

            <div className="dcode-provider-card__actions">
              <Button
                size="sm"
                variant="outline"
                icon={profile.status === "running" ? <Square size={13} /> : <Play size={14} />}
                disabled={busyProfile === profile.id}
                onClick={() => void setSession(profile, profile.status === "running" ? "stop" : "start")}
              >
                {busyProfile === profile.id
                  ? "Working…"
                  : profile.status === "running" ? "Stop session" : "Launch session"}
              </Button>
              <IconButton
                label={`More ${profile.name} actions`}
                icon={<MoreHorizontal size={16} />}
                variant="ghost"
                disabled
              />
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}

async function readError(response: Response) {
  try {
    const body = (await response.json()) as { error?: string; detail?: string };
    return body.error ?? body.detail ?? `Request failed (HTTP ${response.status})`;
  } catch {
    return `Request failed (HTTP ${response.status})`;
  }
}
