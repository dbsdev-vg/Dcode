# Project Environment V2

Project Environment V2 is a clean frontend boundary over the existing DCode
project, file, Monaco, conversation, provider, tool, and permission APIs. The
previous command-bar/activity-rail workbench was removed; backend contracts and
persisted user data were not migrated or replaced.

## Information architecture

Project Environment V2 is one workspace canvas containing Project navigation,
the Editor, a contextual Workbench, and an optional Runtime drawer. The
Workbench provides Agent, Team, Runs, and Project surfaces without navigating
away from the active file. The former Develop, Coordinate, and Manage mode
switcher has been removed because those modes duplicated hierarchy and made the
workspace feel like separate applications.

Project navigation switches compactly between Files, Search, and Changes.
Execution activity is chronological conversation content rather than a second
Activity destination. Future Tasks belong in the Workbench. The Runtime drawer reserves
Terminal, Problems, Output, and Logs; unconnected surfaces state that they are
unavailable and never fabricate data.

## Frontend boundaries

```text
project-environment/
  domain.ts
  shell/
  state/
    use-workspace-panels.ts
  explorer/
  editor/
  chat/
  project-environment.module.css
```

`files-view.tsx` remains only as the global workspace route adapter. Editor
state owns registration, tree refresh, tabs, file reads/writes, external-file
refresh, session restoration, and Monaco diagnostics. Layout state owns only
presentation mode and visibility. Project Chat consumes the persisted NDJSON
execution stream; its real events restore by conversation turn and assistant
content uses the sanitized GFM Markdown pipeline. The chat surface renders one
chronological execution transcript rather than separate transport, activity,
and message cards. Project navigation and Agent widths are resizable and saved
per project in local frontend preferences. The composer remains anchored while
the timeline scrolls independently.

Develop uses shared DCode workspace theme tokens for its canvas levels,
separator, typography scale, line height, and control height. Explorer, editor,
agent, runtime, and status regions consume the same tokens and icon treatment.
The obsolete geometry stylesheet and Workstream mode abstraction were removed.

The transcript exposes provider commentary, meaningful tool activity,
permissions, recovery, and the final answer. Provider transport events such as
`provider_response` and `continuation_started` remain persisted for diagnostics
but are hidden from the normal presentation. Provider/account/remote binding
metadata is available under Technical details. Automatic scrolling only occurs
while the reader is near the latest entry; otherwise the UI offers a jump-to-
latest action.

When a connected browser-provider account is selected, the project chat
acquires a backend warm-session lease while the workspace is open. The session
manager restores a headless authenticated browser context before the first
message and the existing provider adapter reuses it for messages and tool
continuations. Leaving the workspace or switching accounts releases the lease;
the backend retains warm-owned contexts for a three-minute idle grace period
before closing them. Manual browser sessions are never closed by warm-lease
cleanup.

## Ordered provider turns

Each project owns a collection of user conversations and persists one active
conversation selection. Each local conversation remains bound to exactly one
remote provider conversation. The selector only lists conversations belonging
to the active project; switching restores that conversation's messages,
execution turns, approvals, provider account, and remote reference. Creating a
conversation does not create its remote provider conversation until its first
message.

The `project_conversations` membership is separate from the legacy active
pointer in `project_chat_sessions`: membership answers which conversations
belong to a project, while the pointer answers which one should reopen. The
membership also carries a `kind` boundary so future internal agent sessions do
not need to appear as user conversations. Tasks are not represented by these
records.

Each provider response is parsed into ordered `ProviderText` and
`ToolCall` segments. Intermediate text is persisted as `provider_text` runtime
history, tool markup is intercepted, and only a later tool-free provider turn
becomes the canonical assistant response.

The runtime emits provider, tool, continuation, permission, final-response, and
error lifecycle events through the existing NDJSON stream. An `Ask` policy
pauses the run in `waiting_permission`; its persisted permission event renders
an inline Review, Allow once, Always allow, and Deny control at the exact point
where execution paused. Resolving it resumes the same run and remote provider
conversation. Each tool call has an independent approval record, so repeated
approvals in one run remain actionable and resolved approvals stay in history.

## Preserved contracts

- project registration, tree, file read/write, and session endpoints
- project conversation and message persistence
- NDJSON project-chat execution stream
- provider/account and remote conversation binding
- ToolExecutor, ToolRegistry, and stored permission policy

## Project agents (Phase 2)

Every registered project now owns persisted Agent identities independently from
provider accounts and conversations. DCode creates one Lead and one Coder
identity for a project on first access. Each identity stores its name, role,
instructions, enabled state, optional provider-account/model assignment, and a
future-facing permissions document.

User project conversations are assigned to the Lead identity at the membership
boundary while retaining their existing one-to-one remote conversation binding.
The Agent is the responsible entity; the conversation is only persistent
context. The Manage > Agents surface edits these identities through
`/api/projects/{projectId}/agents` and lists accounts through the provider-
neutral `/api/provider-accounts` projection.

No delegation occurs in this phase. The Coder identity does not receive user
messages or create runs until an explicit Agent Run boundary exists.

## Agent Runs (Phase 3)

`agent_runs` is now the canonical durable record for one Agent execution
attempt. A run stores project and Agent ownership, its objective and trigger,
conversation context when applicable, lifecycle state, provider/account/model
snapshots, ordered runtime events, errors, and timestamps. Existing project-chat
execution turns migrate into this model without changing the transcript API.

Every new project-chat request creates a `project_chat` run owned by the
conversation's Lead Agent. Approval pause/resume and cancellation update that
same run. Coordinate presents a real project execution ledger sourced from
`GET /api/projects/{projectId}/agent-runs`.

Nullable `task_id` and `parent_run_id` boundaries exist so a future Task can
retain multiple attempts and a Lead run can create delegated child runs. They
are identifiers only: no Task records, scheduling, delegation, or Coder
execution behavior is implemented in Phase 3.

## Agent Sessions and delegation (Phase 4)

Specialized Agents now own persistent internal sessions independently from the
Lead's user-visible project conversations. An internal session binds one
project Agent to one hidden local/provider conversation and is excluded from
the Develop conversation selector. Repeated delegated runs reuse the same
remote provider conversation and therefore retain Coder context.

A Lead-owned run may create a child run through
`POST /api/agent-runs/{parentRunId}/delegate`. The child records the parent ID,
target Agent, objective, provider snapshot, events, and result. Coordinate can
start a bounded Coder delegation and displays both attempts in the execution
ledger. If the Coder reaches an Ask permission, its child run and pending tool
call persist and Coordinate exposes approval controls; approval resumes the same
child run and internal provider conversation.

Delegation is explicit and interactive in this phase. There is no scheduler,
queue, autonomous planning, arbitrary Agent graph, or Task creation. A future
Lead orchestration loop can call the same delegation service rather than
inventing another execution path.

## Lead orchestration (Phase 5)

The Lead provider protocol now exposes `agent.delegate` only for Lead-owned
project-chat runs. A valid request names a specialized Agent role and one
bounded objective. DCode intercepts it as orchestration—not as a filesystem
tool—executes the child through the Phase 4 delegation service, streams child
activity into the parent transcript, and returns a structured delegation result
to the same Lead remote conversation. The Lead retains responsibility for
evaluating the result and producing the final user-facing response.

Specialized child runs do not receive `agent.delegate`, preventing recursive
delegation. A child that reaches Ask permission remains persisted as
`waiting_permission`; the parent becomes `waiting_delegation` rather than
claiming completion. Resolving the child approval resumes that same Coder run;
when it completes, DCode forwards its result into the same Lead remote
conversation and resumes the parent automatically.

Delegated Coder runs have a bounded 20-provider-turn budget, separate from the
shorter interactive Lead budget, so multi-file implementation and verification
can complete without making ordinary chat runs unbounded. Only an unresolved
permission leaves the Lead in `waiting_delegation`. Completed, failed,
cancelled, denied, and provider-error child outcomes return to the same Lead
conversation, which must evaluate the result and must not claim success for
failed work.

Lead execution is phase-aware. During planning it may inspect safe project
paths and delegate; Coder alone owns mutations and command verification. After
a Coder run completes, Lead enters a finalization-only phase: it must produce a
prose answer from the persisted child result and cannot request another tool or
delegation. DCode permits one corrective provider response if that final answer
is malformed, then completes from the verified child result rather than turning
successful work into a failed parent run. Failed child work may be corrected by
at most one additional delegation attempt and must never be described as
successful.

If a browser provider repeatedly emits malformed Lead planning/tool markup,
DCode performs one normal protocol-repair attempt and then recovers by handing
the original user objective directly to Coder. This fallback stays in the same
parent run and remote conversation, retains normal permissions, and does not
grant Lead mutation or process tools. It prevents a clear implementation
request from ending only because the browser model truncated `agent.delegate`
JSON.

Runtime evidence rules prohibit conclusions from failed inspection calls.
Invalid line ranges return their real bounds for a corrected read, unsafe
dependency links remain sandboxed, and identical successful read/list/search
calls within one run reuse the prior result instead of repeating provider-driven
work. These controls reduce browser-model drift without giving Lead mutation or
process capabilities.

The normal transcript hides individual `tool_call_rejected` and
`provider_retry_failed` transport records, collapses repeated identical Agent
commentary in a run, and shows only the meaningful recovery/delegation outcome.
The underlying events remain persisted for technical inspection.

## Agent workspace management

Develop presents the selected Lead as the owner of the visible project
conversation and shows the configured Coder beside it as the delegation target.
Lead and Coder commentary retain separate identities in the chronological
execution transcript; delegated tool activity is attributed to the Coder.

Agent responsibilities are enforced by runtime capabilities. Lead project-chat
runs receive read/list/search plus `agent.delegate`; they cannot call write,
edit, insert, or patch directly. Every project mutation is delegated to Coder,
whose isolated run owns editing tools, validation, permission pauses, and
recovery. If a provider nevertheless requests a mutation as Lead, DCode rejects
it and asks the same remote Lead conversation to retry with `agent.delegate`.

Coordinate owns operational Agent management. It lists persistent internal
sessions, their provider binding state, and the recent parent/child execution
ledger. An idle internal session can be reset without deleting historical Agent
runs; the next delegation creates a fresh local and remote conversation.
Sessions with active or approval-blocked runs cannot be reset.

Saved and detected project commands live in the Develop Runtime dock. Run
Configurations and their execution history remain independent from Tasks,
Agent Runs, and `process.run`; this keeps the boundary ready for later Task or
Agent invocation without treating a command as work ownership.

## Explicit foundations

Agent delegation, task execution, Git UI, PTY terminal, usage telemetry,
integrations, project sync, and project-level settings remain unavailable until
their backend contracts exist. The UI must not simulate their behavior.
