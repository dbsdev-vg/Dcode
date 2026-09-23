# Tool Execution Runtime

The DCode.Server tool runtime is the single boundary for local operations
requested by future agents and providers. Providers are not connected to this
runtime yet.

## Execution flow

```text
ToolCall
  -> ToolExecutor
  -> ToolRegistry
  -> permission validation
  -> ITool.ExecuteAsync
  -> ToolResult
```

Consumers depend only on `ToolRegistry`, `ToolExecutor`, and the tool contracts.
Built-in tools are registered with `ToolRegistry.Register(ITool)`. A future
plugin tool must use the same method and will be indistinguishable to runtime
consumers; adding one must not require changes to the executor or agent loop.

## Context and security

`ToolContext` contains only the project ID, canonical project root, working
directory, and cancellation token. It does not
expose global application state.

## Project conversation loop

Project Chat uses a provider-agnostic bounded conversation runner. Browser
providers without native function calling receive a strict text protocol. A
provider response may contain prose around one tool request:

```text
<tool>
{"name":"filesystem.read","arguments":{"path":"package.json"}}
</tool>
```

The runner intercepts this block, executes through `ToolExecutor`, formats a
structured `<tool_result>` message, and sends it back using the returned remote
conversation reference. Tool markup and intermediate results are not stored or
displayed as assistant messages. Multiple sequential calls are allowed, with an
eight-turn limit.

Tool blocks are parsed deterministically rather than as greedy markup. Exactly
one opening and closing tag is allowed, and only the trimmed text between the
tags is deserialized as JSON. Surrounding prose becomes ordered intermediate
provider-text events. Missing tags, multiple blocks, and malformed JSON are
rejected as controlled malformed calls.

Browser-provider tool mistakes are recoverable within the same run and remote
conversation. Parser/runtime classifications are `tool_call_malformed`,
`tool_call_truncated`, `tool_call_too_large`, and `tool_call_unknown`. The
runner persists `tool_call_rejected`, `provider_retry_started`,
`provider_retry_completed`, and `provider_retry_failed` events, then sends an
internal corrective continuation that is never stored as a user or assistant
message. A provider gets at most three automatic recovery attempts. Recovery
asks for one compact tool block, recommends insert/patch over oversized edits,
requests smaller sequential calls for oversized payloads, and includes the
available registered tool IDs for unknown names. Exhaustion returns the last
controlled classification instead of creating a new conversation.

Inspection failures such as `invalid_line_range`, `path_outside_project`, and
missing files/directories are bounded recoverable provider mistakes. The
failure result explicitly states that no evidence was obtained. Range recovery
uses `totalLineCount` metadata, and linked dependency paths remain rejected;
the provider must choose a safe project-relative source or manifest path.
Identical successful read/list/search requests in one run reuse the prior
structured result and emit `tool_reused` rather than executing duplicate I/O.

Tool payloads above 131,072 characters are rejected before JSON
deserialization. This ceiling protects the browser-provider boundary while
remaining far above normal read/search/edit calls; large mutations should be
split into compact insert or patch operations.

Project Chat persists the original user message before the first provider turn.
Tool activity remains compact UI state, raw tool markup is excluded from local
messages, and the final provider response is appended only after continuation
completes. A stopped or failed tool loop therefore does not remove the user's
message or reset the local conversation.

Project Chat exposes a streaming NDJSON endpoint at
`POST /api/projects/{projectId}/chat/messages/stream`. It first emits the
persisted local exchange, then provider/tool progress events, and finally the
normal project-chat result. The UI presents these events as a live, collapsible
timeline with elapsed time, per-stage durations, optional tool-call details,
and cancellation. Cancellation propagates through the HTTP request token while
preserving the user message and all already completed timeline steps. Provider
turn and tool durations are also written to backend logs for latency analysis.
The latest timeline, controlled error, and tool-call details are persisted in
SQLite per chat and restored with the local conversation after workspace
navigation or an application restart.
Execution events include an event stage, human-readable label, status, duration,
timestamp, and optional structured technical detail. Tool completion details
contain the tool ID, arguments, structured result/error, and result metadata;
the normal UI keeps this collapsed by default.

Each local DCode chat has one persistent provider/account/remote-conversation
binding. The first provider turn creates and immediately stores the remote
conversation reference; later user turns and tool-result continuations must
reuse it. Provider transports must report a controlled
`remote_conversation_unavailable` state when that conversation cannot be
restored and must not silently create a replacement conversation. Bindings are
stored in SQLite and therefore survive application restarts.

The DeepSeek browser transport performs a readiness check before every send.
It verifies authentication and conversation URL continuity, waits for the page
shell and blocking overlays, and resolves the composer through ordered semantic
textarea/ARIA/contenteditable selectors. Readiness failures log the page URL,
title, contenteditable count, login state, and conversation ID. A 30-second
composer timeout also writes a screenshot under the operating system temporary
directory at `DCode/diagnostics`.

Project Chat advertises read, list, search, write, edit, insert, and patch tools.
Every tool still executes through `ToolExecutor`; mutations remain governed by their
stored global policy and default to Ask. Ask and Deny stop the loop with real
permission/error execution events and cannot be bypassed by the provider.
`process.run` is advertised to specialized project Agents such as Coder, but not to Lead. Code-changing runs are instructed to execute the narrowest relevant typecheck, test, or build before reporting completion. The stored `process.execute` policy still applies, so its default `Ask` policy produces a normal permission request. A failed verification is returned to the same Agent session for correction and retry within the bounded recovery loop.

Filesystem paths must be relative to the active project. The shared sandbox
rejects absolute paths, traversal outside the project, and paths through child
symbolic links or junctions. Command working directories use the same sandbox,
default to the project root, and may only select project subdirectories.

Recursive search validates entries before descending. Unsafe links, paths that
cannot be resolved within the project, and inaccessible entries are skipped
without weakening direct-access checks. Search results report the number of
skipped entries as `metadata.skippedCount`.

Recursive file listing follows the same rule: unsafe symbolic links, junctions,
out-of-project paths, and inaccessible entries are skipped while safe project
entries continue to be returned. `filesystem.list` reports the skipped total as
`metadata.skippedCount`. Direct access to any skipped path remains rejected.

`process.run` launches an executable directly rather than through a command
shell, captures stdout/stderr/exit code, kills the process tree on timeout or
cancellation, and limits requested timeouts to ten minutes. The current
permission gate is not an operating-system process sandbox: a permitted process
can still access resources available to the DCode.Server account. Stronger OS
isolation can be added without changing `ITool`.

## Permissions and policies

- `filesystem.read`
- `filesystem.write`
- `process.execute`

Each registered tool has a persistent global execution policy:

- `allow`: execute automatically
- `ask`: return `approval_required` until a future approval flow authorizes it
- `deny`: return `permission_denied`

Policies resolve in this order: agent override, project override, global
default. Only global policies are implemented today; the resolver contract
already accepts future project and agent context. An unconfigured tool defaults
to `ask`, including a future plugin tool on first registration.

`ToolExecutor` reads the resolved stored policy. The temporary development
request still accepts the old `grantedPermissions` field for compatibility, but
it is ignored and cannot authorize or bypass a stored policy.

### Project-chat approval flow

When an `Ask` policy is reached, Project Chat persists a pending call containing
the run ID, unique tool-call ID, unique permission-event ID, chat/user/project IDs, arguments, required permission,
remote conversation reference, next iteration, and creation time. The turn
remains `waiting_permission`, so reopening DCode restores its approval card.

- `GET /api/chats/{chatId}/pending-approval` restores the pending record.
- `GET /api/chats/{chatId}/tool-approvals` restores the complete ordered
  pending/resolved approval history.
- `POST /api/project-chat/runs/{runId}/tool-calls/{toolCallId}/approve` accepts
  `{ "alwaysAllow": false }` for one-call authorization or `true` to persist the
  global Allow policy before resuming.
- `POST /api/project-chat/runs/{runId}/tool-calls/{toolCallId}/deny` persists a
  `permission_denied` event and resolves the pending call.

Allow-once is scoped to the claimed call and does not change stored policy.
Approval executes through `ToolExecutor`, sends the result to the same remote
conversation, and continues the original run. Conditional claiming makes a
second approval or denial return Conflict rather than execute twice.

Approvals are records rather than a conversation-level boolean. If an approved
edit fails validation and recovery requests another Ask-governed edit, the
continuation persists a second approval with a new tool-call/event identity in
the same run. The frontend renders every unresolved record independently and
keeps approved/denied records as non-interactive history. Always Allow updates
the stored tool policy before continuation, so subsequent matching calls skip
Ask; Allow Once never authorizes a later call.

## Built-in tools

- `filesystem.read`
- `filesystem.list`
- `filesystem.search`
- `filesystem.write`
- `filesystem.edit`
- `filesystem.insert`
- `filesystem.patch`
- `process.run`

Definitions include descriptions, JSON argument schemas, and permission IDs.
Results include success, structured output or a controlled error, metadata, and
execution duration.

`filesystem.read` accepts optional 1-based inclusive `startLine` and `endLine`
arguments. Omitting both preserves full-file behavior. Ranged responses include
the selected raw `content`, a `numberedContent` diagnostic view, and
`actualStartLine`, `actualEndLine`, `totalLineCount`, and `ranged` metadata. The
end is clamped to the file length; reversed, non-positive, or wholly
out-of-file ranges return `invalid_line_range`.

### Editing tools

`filesystem.edit` performs one small exact replacement:

```json
{"path":"src/file.ts","oldText":"const value = 1","newText":"const value = 2","replaceAll":false}
```

`oldText` and `newText` are each limited to 16,384 characters. Larger payloads
return `edit_too_large` with guidance to use insert or patch. This permits
normal functions and configuration blocks while discouraging full-file payloads.

`filesystem.insert` adds content adjacent to a stable anchor:

```json
{"path":"src/file.ts","anchor":"export default App;","position":"before","content":"export const version = 1;\n\n"}
```

`position` is `before` or `after`. By default the anchor must occur exactly
once. Missing and repeated anchors return `anchor_not_found` and
`anchor_ambiguous` without a write. Repeated anchors may be selected explicitly
with `"occurrence":"first"`, `"occurrence":"last"`, or a 1-based numeric value
such as `"occurrence":2`. An invalid or out-of-range selection never modifies
the file. Results report `matchedOccurrenceCount`, `selectedOccurrence`, and
`insertionOffset` in metadata.

Anchors are limited to 2,048 characters and should be short but structurally
unique. For array appends, prefer a closing delimiter plus nearby unique
context. Use `last` only when the last match is semantically the intended
location, and never resend a large existing object as the anchor. Oversized
anchors return `anchor_too_large` so the provider can retry with a compact
delimiter or choose `filesystem.patch`.

`filesystem.patch` applies ordered exact-replacement hunks:

```json
{"path":"src/file.ts","hunks":[{"oldText":"const one = 1","newText":"const one = 10"},{"oldText":"const two = 2","newText":"const two = 20"}]}
```

Each hunk must match exactly once in the candidate produced by earlier hunks.
All hunks are validated in memory before a same-directory temporary file
replaces the target. A missing, ambiguous, or invalid hunk leaves the original
file unchanged.

`filesystem.write` remains appropriate for new files and intentional full-file
replacement. Future plugin mutation tools should expose similarly controlled
match errors and validate an entire operation before committing it, while using
the unchanged `ITool` registration contract.

### Post-edit syntax validation

Write, edit, insert, and patch validate recognized source files before their
atomic commit. The first validator supports `.ts`, `.tsx`, `.js`, and `.jsx`
through the TypeScript compiler's fast `createSourceFile` parse diagnostics; it
does not load or build the complete project. DCode resolves the project's local
TypeScript compiler first and its own frontend runtime copy second.

An invalid candidate returns `syntax_validation_failed` with concise line and
column diagnostics. Because validation occurs against the in-memory candidate
before the atomic replacement, the previous file remains byte-for-byte intact.
The project-chat loop treats this as a recoverable editing error and returns the
diagnostics to the same provider conversation for a corrected retry.

Recoverable editing failures—including syntax validation, missing or ambiguous
matches/anchors, patch failures, and oversized edits—remain inside the same run
and remote conversation. The runner sends the structured failure plus explicit
rollback and inspection guidance back to the provider and persists
`tool_failed`, `recovery_started`, `recovery_completed`, and
`recovery_exhausted` events. Inspection reads may occur while recovery remains
active; only a successful mutation completes it. Three automatic recoverable
edit continuations are allowed before `recovery_exhausted`. This is separate
from malformed tool-call recovery and the overall eight-iteration loop.

Tool metadata includes `validationPerformed`, `language`, and
`diagnosticCount`. Unrecognized extensions report validation as not performed.
If a recognized language validator cannot be loaded, the controlled
`syntax_validation_unavailable` error prevents an unvalidated source write.
`ISourceSyntaxValidator` and `SourceSyntaxValidationService` form the extension
point for future C#, JSON, CSS, and plugin-provided validators. This integrity
check complements rather than replaces normal project build and test commands.

## Development API

`GET /api/tools` returns definitions without exposing tool implementations.

`POST /api/tools/execute` accepts:

```json
{
  "toolId": "filesystem.read",
  "projectId": "PROJECT_ID",
  "arguments": { "path": "README.md" }
}
```

Tool policies are available through `GET /api/settings/tool-policies` and can be
updated with `PUT /api/settings/tool-policies/{toolId}` using a JSON body such
as `{ "policy": "ask" }`. These endpoints are debugging/application settings
interfaces, not the final agent approval protocol.

### curl examples

Replace `PROJECT_ID` below with an ID returned by `GET /api/projects`.

```bash
curl http://localhost:5283/api/tools

curl -X POST http://localhost:5283/api/tools/execute -H "Content-Type: application/json" -d '{"toolId":"filesystem.read","projectId":"PROJECT_ID","arguments":{"path":"README.md"}}'

curl -X POST http://localhost:5283/api/tools/execute -H "Content-Type: application/json" -d '{"toolId":"filesystem.list","projectId":"PROJECT_ID","arguments":{"path":"src","recursive":false}}'

curl -X POST http://localhost:5283/api/tools/execute -H "Content-Type: application/json" -d '{"toolId":"filesystem.search","projectId":"PROJECT_ID","arguments":{"query":"TODO","path":"."}}'

curl -X POST http://localhost:5283/api/tools/execute -H "Content-Type: application/json" -d '{"toolId":"filesystem.write","projectId":"PROJECT_ID","arguments":{"path":"notes/tool-test.txt","content":"Created by DCode","createDirectories":true}}'

curl -X POST http://localhost:5283/api/tools/execute -H "Content-Type: application/json" -d '{"toolId":"filesystem.edit","projectId":"PROJECT_ID","arguments":{"path":"notes/tool-test.txt","oldText":"Created","newText":"Edited"}}'

curl -X POST http://localhost:5283/api/tools/execute -H "Content-Type: application/json" -d '{"toolId":"filesystem.insert","projectId":"PROJECT_ID","arguments":{"path":"notes/tool-test.txt","anchor":"Edited","position":"after","content":" by an agent"}}'

curl -X POST http://localhost:5283/api/tools/execute -H "Content-Type: application/json" -d '{"toolId":"filesystem.patch","projectId":"PROJECT_ID","arguments":{"path":"notes/tool-test.txt","hunks":[{"oldText":"Edited","newText":"Patched"},{"oldText":"agent","newText":"DCode"}]}}'

curl -X POST http://localhost:5283/api/tools/execute -H "Content-Type: application/json" -d '{"toolId":"process.run","projectId":"PROJECT_ID","arguments":{"command":"dotnet","arguments":["--version"],"workingDirectory":".","timeoutMs":30000}}'
```
