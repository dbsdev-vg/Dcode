"use client";

import { useEffect, useState } from "react";
import { Badge, Button } from "@dbs-studio/ui";
import {
  Bot,
  CircleGauge,
  Palette,
  PlugZap,
  Settings2,
  ShieldCheck,
  Wrench,
} from "lucide-react";

import { useWorkspace } from "../../state/workspace-context";

type SettingsSection = "general" | "appearance" | "providers" | "tools" | "advanced";
type ToolPolicy = "allow" | "ask" | "deny";

interface ToolDefinition {
  id: string;
  description: string;
  requiredPermissions: string[];
}

interface ToolPolicyResponse {
  toolId: string;
  policy: ToolPolicy;
  scope: string;
}

const serverUrl = "http://localhost:5283";
const sections = [
  { id: "general" as const, label: "General", icon: Settings2 },
  { id: "appearance" as const, label: "Appearance", icon: Palette },
  { id: "providers" as const, label: "Providers", icon: PlugZap },
  { id: "tools" as const, label: "Tools & Permissions", icon: ShieldCheck },
  { id: "advanced" as const, label: "Advanced", icon: CircleGauge },
];
const policies: { id: ToolPolicy; label: string; description: string }[] = [
  { id: "allow", label: "Allow", description: "Execute automatically" },
  { id: "ask", label: "Ask", description: "Require user approval" },
  { id: "deny", label: "Deny", description: "Block execution" },
];

export function SettingsView() {
  const [activeSection, setActiveSection] = useState<SettingsSection>("tools");

  return (
    <section className="dcode-settings-page">
      <header className="dcode-settings-page__header"><h1>Settings</h1><p>Configure your DCode workspace.</p></header>
      <nav className="dcode-settings-nav" aria-label="Settings sections">
        {sections.map((section) => {
          const Icon = section.icon;
          return (
            <button type="button" key={section.id} className={activeSection === section.id ? "is-active" : ""} onClick={() => setActiveSection(section.id)}>
              <Icon size={15} /><span>{section.label}</span>
            </button>
          );
        })}
      </nav>
      <main className="dcode-settings-content">
        {activeSection === "tools" ? <ToolsPermissionsSettings /> : <SettingsPlaceholder section={activeSection} />}
      </main>
    </section>
  );
}

function ToolsPermissionsSettings() {
  const [tools, setTools] = useState<ToolDefinition[]>([]);
  const [toolPolicies, setToolPolicies] = useState<Record<string, ToolPolicy>>({});
  const [loading, setLoading] = useState(true);
  const [savingToolId, setSavingToolId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    Promise.all([
      fetch(`${serverUrl}/api/tools`),
      fetch(`${serverUrl}/api/settings/tool-policies`),
    ]).then(async ([toolsResponse, policiesResponse]) => {
      if (!toolsResponse.ok || !policiesResponse.ok) throw new Error("Unable to load tool permissions.");
      const definitions = await toolsResponse.json() as ToolDefinition[];
      const storedPolicies = await policiesResponse.json() as ToolPolicyResponse[];
      if (!active) return;
      setTools(definitions);
      setToolPolicies(Object.fromEntries(storedPolicies.map((policy) => [policy.toolId, policy.policy])));
    }).catch((reason: unknown) => {
      if (active) setError(reason instanceof Error ? reason.message : "Unable to load tool permissions.");
    }).finally(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, []);

  async function updatePolicy(toolId: string, policy: ToolPolicy) {
    setSavingToolId(toolId);
    setError(null);
    try {
      const response = await fetch(`${serverUrl}/api/settings/tool-policies/${encodeURIComponent(toolId)}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ policy }),
      });
      if (!response.ok) throw new Error(`Unable to update ${toolId}.`);
      const saved = await response.json() as ToolPolicyResponse;
      setToolPolicies((current) => ({ ...current, [saved.toolId]: saved.policy }));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to update tool policy.");
    } finally {
      setSavingToolId(null);
    }
  }

  return (
    <div className="dcode-settings-section">
      <header className="dcode-settings-section__header">
        <span className="dcode-page-eyebrow">Execution security</span>
        <h2>Tools & Permissions</h2>
        <p>Set the global behavior for local operations requested by every DCode agent and model.</p>
      </header>

      <div className="dcode-settings-notice">
        <ShieldCheck size={18} />
        <div><strong>Global defaults</strong><span>These policies are inherited by all projects and agents. More specific overrides will be added later.</span></div>
        <Badge variant="outline">Agent → Project → Global</Badge>
      </div>

      {error ? <div className="dcode-settings-error">{error}</div> : null}
      {loading ? <div className="dcode-settings-loading">Loading available tools…</div> : null}
      {!loading && !tools.length ? <div className="dcode-settings-loading">No tools are registered.</div> : null}

      <div className="dcode-tool-policy-list">
        {tools.map((tool) => {
          const selectedPolicy = toolPolicies[tool.id] ?? "ask";
          return (
            <article className="dcode-tool-policy" key={tool.id}>
              <div className="dcode-tool-policy__identity">
                <span className="dcode-tool-policy__icon"><Wrench size={17} /></span>
                <div><h3>{tool.id}</h3><p>{tool.description}</p><div>{tool.requiredPermissions.map((permission) => <Badge variant="outline" key={permission}>{permission}</Badge>)}</div></div>
              </div>
              <div className="dcode-tool-policy__options" aria-label={`${tool.id} policy`}>
                {policies.map((policy) => (
                  <button
                    type="button"
                    key={policy.id}
                    className={selectedPolicy === policy.id ? `is-active is-${policy.id}` : ""}
                    disabled={savingToolId === tool.id}
                    title={policy.description}
                    onClick={() => void updatePolicy(tool.id, policy.id)}
                  >
                    {policy.label}
                  </button>
                ))}
              </div>
            </article>
          );
        })}
      </div>
    </div>
  );
}

function SettingsPlaceholder({ section }: { section: Exclude<SettingsSection, "tools"> }) {
  const { setActiveWorkspace } = useWorkspace();
  const details = {
    general: { title: "General", description: "Workspace startup, project behavior, and application preferences will live here.", icon: Settings2 },
    appearance: { title: "Appearance", description: "Theme, editor presentation, and interface density settings will live here.", icon: Palette },
    providers: { title: "Providers", description: "Provider connections remain managed in the dedicated Providers workspace.", icon: PlugZap },
    advanced: { title: "Advanced", description: "Diagnostics and lower-level runtime configuration will live here.", icon: Bot },
  }[section];
  const Icon = details.icon;

  return (
    <div className="dcode-settings-section">
      <header className="dcode-settings-section__header"><span className="dcode-page-eyebrow">DCode settings</span><h2>{details.title}</h2><p>{details.description}</p></header>
      <div className="dcode-settings-placeholder"><Icon size={24} /><strong>{details.title}</strong><span>This section is prepared for future settings.</span>{section === "providers" ? <Button onClick={() => setActiveWorkspace("providers")}>Manage providers</Button> : null}</div>
    </div>
  );
}
