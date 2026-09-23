# Settings

DCode Settings uses the existing global workspace navigation and contains:

- General
- Appearance
- Providers
- Tools & Permissions
- Advanced

Settings does not introduce a second sidebar. Sections use a compact horizontal
navigation row inside the page so the global DCode sidebar remains the only
primary navigation surface.

Tools & Permissions is the first functional section. The frontend discovers
tools dynamically from `GET /api/tools`, loads policies from
`GET /api/settings/tool-policies`, and persists changes through DCode.Server.
It does not maintain a separate browser-side source of truth.

Provider credentials and accounts remain in the Providers workspace. Tool
permissions apply to execution regardless of which provider or model requested
the operation.
