# DCode Agent Instructions

Read `docs/README.md` and all relevant documents in `docs/` before making architectural changes.

The documentation is authoritative.

Do not:
- introduce a second state-management architecture without justification
- bypass DCode.Server for repository mutations
- put native Windows dialogs in ASP.NET request handlers
- hard-code provider logic into the agent runtime
- hard-code DCode product colors outside the theme package
- duplicate shared DBS Studio UI primitives
- replace WebView2 with Electron
- make Files a separate top-level workspace

Prefer small vertical slices that compile and can be tested.

Before finishing a task:
- build the affected project
- run relevant tests
- report changed files
- report any architectural deviation
Fas