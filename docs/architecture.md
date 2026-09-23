# Architecture

## High-level architecture

DCode.Host
    |
    +-- WebView2
    |     |
    |     +-- DCode.Web
    |
    +-- Native Bridge

DCode.Web
    |
    +-- HTTP / SSE / WebSocket
          |
          +-- DCode.Server
                  |
                  +-- Project Service
                  +-- File Service
                  +-- Agent Runtime
                  +-- Tool Runtime
                  +-- Provider Manager
                  +-- Context Manager
                  +-- Session Manager
                  +-- Logging

## Dependency rules

The frontend must not directly access arbitrary local files.

Native OS capabilities belong to DCode.Host.

Project and repository operations belong to DCode.Server.

The agent runtime must not depend on a specific provider.

Provider implementations must conform to a common provider interface.

Tools must conform to a common tool interface.

Agents and providers must request local operations through `ToolExecutor` and
`ToolRegistry`; they must not instantiate or depend on concrete tools. Built-in
and future plugin-provided tools share the same `ITool` registration contract.

UI components must not contain provider-specific browser automation.

Provider-specific browser automation must live behind provider adapters.

Provider transports may use direct APIs or managed Playwright sessions, but
both must implement the same provider interface consumed by the agent runtime.

Credential persistence and Playwright lifecycle management belong to
DCode.Server. DCode.Web only provides provider configuration and session UI.

Persistent application metadata belongs to the DCode.Server SQLite storage
layer. Repository contents remain on disk and must not be copied into the
application database.
