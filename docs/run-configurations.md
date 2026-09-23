# Run Configurations

Run Configurations are project-owned, reusable commands started explicitly by
the user. They are separate from Tasks, Agent Runs, Agent sessions, and
`process.run` tool calls.

## Model

A configuration persists:

- name
- executable
- ordered argument array
- project-relative working directory
- whether it was detected from project metadata

An execution snapshots the command and retains status, stdout, stderr, exit
code, error, and timestamps independently of later configuration changes.

Package scripts are detected from `package.json` when a project has no saved
configuration. On Windows, DCode bypasses the fragile `npm.cmd` shim and
launches the verified npm CLI through the `node.exe` from the same installation.

## Security and lifecycle

Commands launch directly with `UseShellExecute=false`; DCode does not pass a
free-form string through a command shell. Working directories are canonicalized
and sandboxed to the project root, including rejection of traversal and paths
through junctions or symbolic links.

The user pressing Run is the authorization for that execution. Agent-requested
commands remain a separate path through `process.run` and stored tool policy.

The project toolbar owns the primary execution control. Its main action starts
the project's last-selected configuration and opens the Runtime output drawer.
The adjacent menu can start any configuration and makes that choice the local
default for the project. While a process is active the control exposes Stop and
Restart; Restart terminates the current process tree before launching the same
configuration again. Configuration editing and historical output remain in the
Runtime drawer rather than expanding the primary toolbar.

Statuses currently include `running`, `completed`, `failed`, `stopped`, and
`interrupted`. Stop terminates the complete process tree. A running process left
behind by a DCode shutdown is marked interrupted when the backend starts again.
Output is capped in memory and persisted locally with execution history.
The Runtime renderer removes ANSI terminal control sequences before presenting
captured stdout/stderr so color and cursor codes never appear as raw text.

## API

- `GET /api/projects/{projectId}/run-configurations`
- `POST /api/projects/{projectId}/run-configurations`
- `PUT /api/projects/{projectId}/run-configurations/{configurationId}`
- `DELETE /api/projects/{projectId}/run-configurations/{configurationId}`
- `POST /api/projects/{projectId}/run-configurations/{configurationId}/start`
- `GET /api/projects/{projectId}/run-executions`
- `POST /api/projects/{projectId}/run-executions/{executionId}/stop`

The first UI uses short polling for live output. A PTY, interactive stdin,
environment/secret profiles, health detection, ports, compound configurations,
and Agent/Task invocation are intentionally future work.
