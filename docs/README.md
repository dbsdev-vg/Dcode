# DCode

DCode is a local-first AI development environment.
cocacolalite
It combines a native Windows desktop host, a web-based IDE interface,
a local backend, and an extensible AI agent runtime.

The goal is to provide a development experience where a user can:

- open local repositories
- browse and edit source files
- use Monaco as the code editor
- interact with AI coding agents
- let agents read, search, create, edit, and test code
- manage models and providers
- manage context, sessions, tokens, tools, logs, and errors
- review and approve agent changes
- use multiple specialized agents
- integrate additional providers and plugins later

The product brand system, logo usage, and visual tokens are documented in
[brand.md](./brand.md).

The built-in local operation boundary and its temporary development API are
documented in [tools.md](./tools.md).

Application settings and the current Tools & Permissions surface are documented
in [settings.md](./settings.md).

The rewritten project workspace, management boundary, and agent/task foundations
are documented in [project-environment-v2.md](./project-environment-v2.md).

Project-owned commands and execution history are documented in
[run-configurations.md](./run-configurations.md).

## Core Architecture

DCode is composed of three primary applications.

### DCode.Host

Native Windows application written in C#.

Responsibilities:

- application lifecycle
- WebView2 hosting
- native Windows dialogs
- native filesystem integration where required
- launching and managing DCode services
- WebView2 <-> native bridge
- future system tray and update integration

### DCode.Web

Next.js application rendered inside WebView2.

Responsibilities:

- project management UI
- file explorer
- Monaco editor
- chat
- agents UI
- providers UI
- sessions
- settings
- token/context visualization
- logs and errors
- diff/review UI

### DCode.Server

ASP.NET Core backend.

Responsibilities:

- project registry
- filesystem operations
- file reading and writing
- search
- process execution
- Git
- agent runtime
- tool execution
- provider abstraction
- context management
- sessions
- usage accounting
- logs and errors

## Product Principles

1. Local-first.
2. Windows-first for initial releases.
3. Native access is owned by DCode.Host.
4. Filesystem and agent operations are owned by DCode.Server.
5. The frontend never receives unrestricted filesystem authority.
6. Providers are abstracted behind a common interface.
7. Agents are model-agnostic.
8. Tools are explicit and auditable.
9. Destructive operations require permission controls.
10. Documentation is the source of truth.
