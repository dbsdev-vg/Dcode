# Local Storage

## Ownership

DCode.Server owns persistent application state through a local SQLite database.
DCode.Web accesses persistent state only through DCode.Server APIs.

The default database location is:

`%LOCALAPPDATA%\DCode\dcode.db`

Development and tests may override the location with the
`Storage:DatabasePath` configuration value.

## Stored data

SQLite stores application metadata:

- registered and recent projects
- active-project metadata
- open editor tabs and active file
- Explorer expansion and workspace layout state
- provider configuration metadata
- Playwright browser-profile metadata
- local chat conversations and message history
- application settings
- global tool execution policies
- the current persisted project-chat conversation binding
- project Agent identities and their provider-account/model assignments
- Agent Runs with ownership, lifecycle, provider snapshots, and execution events
- hidden project Agent sessions and their persistent provider conversations

Project sessions are saved after workspace changes and restore:

- open editor tabs
- the active file
- expanded Explorer folders
- Explorer, Agent Chat, and Terminal visibility

## Data that is not stored in SQLite

- repository file contents remain in the repository
- the complete file tree is rebuilt from disk
- API credentials are never stored as plaintext database values
- browser cookies and authenticated storage remain inside isolated Playwright
  user-data directories

Provider rows may contain a credential reference. The referenced secret must be
stored using a Windows protected-secret mechanism such as Credential Manager or
DPAPI.

## Schema evolution

The database contains a schema version. Schema changes must use explicit,
forward-only migrations and preserve existing user data.

Global tool policies are stored by tool ID in `tool_permission_policies`.
Policies belong to tool execution rather than providers or models. The initial
defaults allow read/list/search and require approval for write/edit/process
execution.

## Chat persistence

Each DCode chat is stored locally and permanently bound to the provider account
that created it. Chat metadata includes its local title and status, provider and
transport, browser-profile identity, timestamps, and optional remote provider
conversation ID and URL. Messages are stored locally as ordered role/content
records with optional provider message IDs.

The local chat remains available if its remote provider conversation becomes
unavailable. A browser profile with dependent chats cannot be deleted until its
conversations are explicitly deleted or moved, preventing orphaned history.

## Filesystem synchronization

Repository files remain authoritative. DCode periodically refreshes the
Explorer tree and clean open editor buffers from disk.

If a file changes externally while its DCode buffer has unsaved changes, DCode
must preserve the buffer and show a conflict instead of overwriting either
version automatically.
