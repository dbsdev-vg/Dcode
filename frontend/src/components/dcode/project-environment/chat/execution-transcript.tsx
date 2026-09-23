"use client";

import { useState } from "react";
import { Bot, Check, Clock3, Copy, LoaderCircle, ShieldAlert, X } from "lucide-react";
import { MarkdownMessage } from "../../project/chat/markdown-message";
import type { ConversationProgress, PendingToolApproval } from "../../project/chat/project-chat-types";
import styles from "../project-environment.module.css";

export function ExecutionTranscript({ events, approvals, busyApproval, leadName, coderName, onApproval }: { events: ConversationProgress[]; approvals: PendingToolApproval[]; busyApproval: string | null; leadName:string;coderName:string; onApproval(approval: PendingToolApproval, action: "once" | "always" | "deny"): void }) {
  return <div className={styles.executionTranscript}>{events.map((event, index) => {
    if (event.stage === "provider_text") return isRepeatedProviderText(events,index,event) ? null : <AgentCommentary key={event.id} content={event.label} name={isChildEvent(event)?coderName:leadName} child={isChildEvent(event)} />;
    if (event.stage === "provider_started") {
      if (hasLaterProviderResponse(events, index, event)) return null;
      const interrupted = event.status === "cancelled" || event.status === "failed";
      return <StatusRow key={event.id} event={event} label={interrupted ? "Agent run interrupted" : "Thinking…"} state={interrupted ? "failed" : "running"} />;
    }
    if (event.stage === "permission_required") { const approval = approvals.find((item) => approvalMatchesEvent(item,event)); const busy = approval && busyApproval?.startsWith(`${approval.toolCallId}:`) ? busyApproval.split(":").at(-1) ?? null : null; return <PermissionEvent key={event.id} event={event} approval={approval} busy={busy} onAction={onApproval} />; }
    if (event.stage === "tool_started") return hasLaterToolTerminal(events, index, event) ? null : <StatusRow key={event.id} event={event} label={`${isChildEvent(event)?`${coderName} · `:""}${humanLabel(event)}`} state="running" />;
    if (event.stage === "tool_completed") return <ToolCompletion key={event.id} event={event} owner={isChildEvent(event)?coderName:null} />;
    if (event.stage === "tool_failed") return <StatusRow key={event.id} event={event} label={`${isChildEvent(event)?`${coderName} · `:""}${humanLabel(event)}`} state="failed" />;
    if (event.stage === "recovery_started") return hasLaterRecoveryTerminal(events, index) ? null : <StatusRow key={event.id} event={event} label="Recovering…" state="running" />;
    if (event.stage === "recovery_completed") return <StatusRow key={event.id} event={event} label="Recovered" state="completed" />;
    if (event.stage === "recovery_exhausted") return <StatusRow key={event.id} event={event} label="Recovery failed" state="failed" />;
    if (event.stage === "provider_retry_started") return hasLaterProviderRetryTerminal(events, index) ? null : <StatusRow key={event.id} event={event} label="Recovering agent response…" state="running" />;
    if (event.stage === "provider_retry_completed") return <StatusRow key={event.id} event={event} label="Recovered" state="completed" />;
    if (event.stage === "provider_retry_failed" || event.stage === "tool_call_rejected") return null;
    if (event.stage === "tool_reused") return <StatusRow key={event.id} event={event} label="Reused previous inspection" state="completed" />;
    if (event.stage === "delegation_started") return hasLaterDelegationTerminal(events,index) ? null : <StatusRow key={event.id} event={event} label={event.label} state="running" />;
    if (event.stage === "delegation_completed") return <StatusRow key={event.id} event={event} label={event.label} state="completed" />;
    if (event.stage === "delegation_waiting") return <StatusRow key={event.id} event={event} label="Coder is waiting for approval" state="running" />;
    if (event.stage === "delegation_failed") return <StatusRow key={event.id} event={event} label={event.label} state="failed" />;
    if (event.stage === "error") return <StatusRow key={event.id} event={event} label={event.label || "Execution failed"} state="failed" />;
    return null;
  })}</div>;
}

function AgentCommentary({ content,name,child }: { content: string;name:string;child:boolean }) { return <article className={`${styles.transcriptCommentary} ${child?styles.childCommentary:""}`}><header><Bot size={15} /><strong>{name}</strong><span>{child?"Delegated work":"Planning"}</span></header><MarkdownMessage content={content} /></article>; }

function ToolCompletion({ event,owner }: { event: ConversationProgress;owner:string|null }) {
  const detail = parseDetail(event.detail); const validated = detail?.result?.Metadata?.validationPerformed === true || detail?.result?.metadata?.validationPerformed === true;
  return <><StatusRow event={event} label={`${owner?`${owner} · `:""}${humanLabel(event)}`} state="completed" />{validated ? <div className={styles.validationStatus}><Check size={14} /><span>Syntax validation passed</span></div> : null}</>;
}

function PermissionEvent({ event, approval, busy, onAction }: { event: ConversationProgress; approval?: PendingToolApproval; busy: string | null; onAction(approval: PendingToolApproval, action: "once" | "always" | "deny"): void }) {
  const [review, setReview] = useState(false); const detail = parseDetail(event.detail); const path = detail?.arguments?.path; const command = detail?.arguments?.command; const target = approval ? targetOf(approval) : typeof path === "string" ? path : typeof command === "string" ? command : "this action";
  if (approval && approval.status !== "pending") return <div className={styles.permissionResolved}><Check size={14} /><span>{approval.status === "approved" ? "Approved once" : approval.status === "always_allowed" ? "Always allowed" : "Denied"}</span></div>;
  return <section className={styles.inlinePermission}><header><ShieldAlert size={16} /><div><strong>{actionName(approval?.toolId ?? detail?.toolId)} {target}</strong><span>This action needs permission.</span></div></header>{review ? <details open><summary>Technical details</summary><pre>{JSON.stringify(approval?.arguments ?? detail?.arguments ?? {}, null, 2)}</pre></details> : null}<footer><button onClick={() => setReview(!review)}>Review</button><button disabled={!approval || Boolean(busy)} onClick={() => approval && onAction(approval, "once")}>{busy === "once" ? "Allowing…" : "Allow once"}</button><button disabled={!approval || Boolean(busy)} onClick={() => approval && onAction(approval, "always")}>{busy === "always" ? "Allowing…" : "Always allow"}</button><button disabled={!approval || Boolean(busy)} onClick={() => approval && onAction(approval, "deny")}>{busy === "deny" ? "Denying…" : "Deny"}</button></footer></section>;
}

function StatusRow({ event, label, state }: { event: ConversationProgress; label: string; state: "running" | "completed" | "failed" }) {
  const [copied, setCopied] = useState(false); const details = readableDetail(event.detail);
  return <div className={`${styles.transcriptStatus} ${styles[state]}`}><span>{state === "completed" ? <Check size={14} /> : state === "running" ? <LoaderCircle size={14} /> : <X size={14} />}</span><div><strong>{label}</strong>{details ? <details><summary>Technical details</summary><div><pre>{details}</pre><button onClick={() => { void navigator.clipboard.writeText(details); setCopied(true); window.setTimeout(() => setCopied(false), 1200); }}>{copied ? <Check size={11} /> : <Copy size={11} />}{copied ? "Copied" : "Copy"}</button></div></details> : null}</div>{event.durationMilliseconds != null ? <small><Clock3 size={11} />{formatDuration(event.durationMilliseconds)}</small> : null}</div>;
}

function humanLabel(event: ConversationProgress) { if (event.stage === "tool_failed") { const code = parseDetail(event.detail)?.result?.Error?.Code ?? parseDetail(event.detail)?.result?.error?.code; return code === "syntax_validation_failed" ? "Edit failed validation" : `${toPast(event.label)} failed`; } return event.stage === "tool_completed" ? toPast(event.label) : event.label; }
function toPast(label: string) { return label.replace(/^Reading /, "Read ").replace(/^Searching /, "Searched ").replace(/^Listing /, "Listed ").replace(/^Editing /, "Edited ").replace(/^Writing /, "Wrote ").replace(/^Inserting into /, "Inserted into ").replace(/^Running /, "Ran "); }
function actionName(toolId?: string) { return toolId?.includes("edit") ? "Editing" : toolId?.includes("insert") ? "Inserting into" : toolId?.includes("patch") ? "Patching" : toolId?.includes("write") ? "Writing" : toolId?.includes("run") ? "Running" : "Using"; }
function targetOf(approval: PendingToolApproval) { return typeof approval.arguments.path === "string" ? approval.arguments.path : typeof approval.arguments.command === "string" ? approval.arguments.command : approval.toolId; }
type EventDetail = {
  toolId?: string;
  arguments?: Record<string, unknown>;
  result?: {
    Metadata?: Record<string, unknown>;
    metadata?: Record<string, unknown>;
    Error?: { Code?: string };
    error?: { code?: string };
  };
};
function parseDetail(detail: string | null): EventDetail | null { if (!detail) return null; try { return JSON.parse(detail) as EventDetail; } catch { return null; } }
function readableDetail(detail: string | null) { if (!detail) return null; try { return JSON.stringify(JSON.parse(detail), null, 2); } catch { return detail; } }
function formatDuration(ms: number) { return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(1)}s`; }
function iteration(id: string) { return id.split(":").at(-1); }
function hasLaterProviderResponse(events: ConversationProgress[], index: number, event: ConversationProgress) { return events.slice(index + 1).some((item) => item.stage === "provider_response" && iteration(item.id) === iteration(event.id)); }
function hasLaterToolTerminal(events: ConversationProgress[], index: number, event: ConversationProgress) { return events.slice(index + 1).some((item) => ["tool_completed", "tool_failed", "permission_required", "permission_denied"].includes(item.stage) && iteration(item.id) === iteration(event.id)); }
function hasLaterRecoveryTerminal(events: ConversationProgress[], index: number) { return events.slice(index + 1).some((item) => item.stage === "recovery_completed" || item.stage === "recovery_exhausted"); }
function hasLaterProviderRetryTerminal(events: ConversationProgress[], index: number) { return events.slice(index + 1).some((item) => item.stage === "provider_retry_completed" || item.stage === "provider_retry_failed"); }
function hasLaterDelegationTerminal(events: ConversationProgress[], index: number) { return events.slice(index + 1).some((item) => item.stage === "delegation_completed" || item.stage === "delegation_waiting" || item.stage === "delegation_failed"); }
function isChildEvent(event:ConversationProgress){return event.id.includes(":child:");}
function approvalMatchesEvent(approval:PendingToolApproval,event:ConversationProgress){return approval.eventId===event.id||event.id.endsWith(`:child:${approval.eventId}`);}
function isRepeatedProviderText(events:ConversationProgress[],index:number,event:ConversationProgress){for(let cursor=index-1;cursor>=0;cursor--){const candidate=events[cursor];if(candidate.stage==="user_message"||candidate.stage==="final_response")break;if(candidate.stage==="provider_text"&&candidate.label.trim()===event.label.trim()&&isChildEvent(candidate)===isChildEvent(event))return true;}return false;}
