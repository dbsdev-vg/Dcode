# Workspace

## Home

DCode starts in the global Home workspace. Home is a practical launch surface,
not a replacement for the project workspace. It restores and summarizes the
existing local state so the user can continue the active project, open recent
projects and conversations, inspect backend/provider readiness, or navigate to
Projects, Chat, and Providers.

The full `Build software.` brand lockup belongs on Home. Clicking the compact
DCode identity in the global sidebar returns to Home.

## Projects vs Workspace

Projects and project files are not separate peer-level concepts.

The Projects screen is used to open and manage repositories.

Once a project is opened, DCode enters the project workspace.

## Project Workspace

The active implementation is Project Environment V2. Its component and domain
architecture is documented in [project-environment-v2.md](./project-environment-v2.md).

The project workspace contains:

- Explorer
- Editor
- Chat
- Git
- Terminal
- Problems
- Agent activity

Files are displayed through the Explorer panel and opened in Monaco.

Project Chat is scoped to the active project. A project may contain multiple
user conversations, with one active selection restored when the project opens.
Every conversation retains its own provider account, messages, execution
history, approvals, and opaque remote conversation reference. Conversations
from other projects are excluded. The first tool-enabled loop supports read,
list, and search operations only and shows compact execution activity without
rendering provider tool markup.

New user conversations receive a stable project-scoped identity such as
`ui-playground Lead #1`. Users can switch, rename, and delete them from the
conversation header. When an established browser-provider conversation is
renamed, DCode updates the remote provider title before committing the local
title so the two representations do not silently diverge. A conversation that
has not sent its first message has no remote identity yet and is renamed locally
until that remote conversation is created. Deleting DCode conversation history
does not delete the provider-owned conversation and the confirmation states
that boundary explicitly.

Provider conversation actions are executed through the bound browser account.
`Open in DeepSeek` launches or focuses a visible authenticated provider session
and navigates that profile to the saved remote conversation; the frontend does
not open the remote URL in the user's unrelated default-browser profile.

Assistant responses render sanitized GitHub-flavored Markdown with DCode-native
typography, tables, links, inline code, and highlighted code blocks with copy
controls. Raw provider HTML and raw `<tool>` markup are never rendered.

Provider turns, tool requests/results, permission states, errors, retries, and
completion share a reusable execution-event shape. Project Chat renders the
events as compact expandable history and persists their technical detail,
duration, status, and timestamp in SQLite. This event shape is intentionally
independent of the chat layout so future Tasks can consume the same history.
Execution history is stored per user turn rather than as a single latest-run
snapshot, so multiple tool-enabled messages restore in their original position
between the user request and final assistant response.

The DeepSeek browser adapter converts the provider's semantic response DOM to
Markdown before persistence. Headings, lists, tables, blockquotes, links, inline
code, and fenced code retain their structure, while provider-owned Copy/Download
controls are excluded. Using `innerText` for assistant response persistence is
not allowed because it flattens formatting and leaks provider chrome.

The user should not navigate to a separate full-screen "Files" workspace.

## Focus Mode

Opening a project transitions DCode from the global application shell into
project focus mode.

In focus mode:

- the global sidebar and top bar are collapsed
- the unified project bar becomes the primary navigation surface
- Project navigation, DCode Agent, and Runtime are independently collapsible
- returning to Projects restores the global application shell
- browser Back and Forward navigation continue to work
- expanded folders, open tabs, active file, and panel layout restore per project

Repository changes made by external editors must appear in DCode. Clean open
files may refresh automatically. Dirty files must show a conflict and must not
be overwritten silently.

## Unified project workspace

The project environment is one persistent workspace rather than separate
Develop, Coordinate, and Manage destinations. Its primary toolbar owns project
identity, file visibility, save, Run, diagnostics, and the contextual Workbench.
The resizable Workbench switches between Agent, Team, Runs, and Project without
replacing the editor or losing the user's file context. A single status strip
reports the active file, local state, diagnostics, and Runtime visibility.

Agent activity remains chronological conversation content. Team configuration,
Agent sessions, execution history, and project metadata are contextual tools in
the same workspace. Future Tasks should join this Workbench instead of
reintroducing a separate Coordinate application mode.

File save belongs to the Editor Stage rather than a global menu. Capabilities
without a backend remain visibly unavailable and do not expose pretend actions.

## Editor identity

Monaco uses the `dcode-dark` and `dcode-light` themes and follows the application
theme. The palettes align with DCode semantic surfaces and restrained red accent,
while syntax colors remain readable and neutral enough for extended editing.
Tabs, breadcrumbs, toolbar, Explorer, Chat, and Terminal chrome remain owned by
DCode React components rather than Monaco or VS Code-compatible workbench UI.

## Intended flow

Home
    -> Projects
    -> Open project
    -> Project becomes active
    -> Workspace opens
    -> Explorer shows project tree
    -> User opens file
    -> Monaco displays file
    -> Agent can inspect or modify the same project
