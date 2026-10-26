# MCP & tooling setup

This project uses Model Context Protocol (MCP) servers to help produce and validate
engineering artifacts (architecture diagrams, etc.). Configuration lives in version
control so the setup is reproducible across machines.

## Configured MCP servers

Defined in two places (both committed):

- **`.mcp.json`** (repo root) — the generic, tool-agnostic MCP manifest. Portable `npx`
  invocations so it works on any machine with Node.js.
- **`.kiro/settings/mcp.json`** — Kiro's workspace MCP settings. Mirrors the root manifest
  and adds Kiro-specific fields (`disabled`, `autoApprove`).

| Server | Command | Purpose |
| --- | --- | --- |
| `playwright` | `npx @playwright/mcp@latest` | Drives the Playwright end-to-end tests under `e2e/`. |
| `mermaid` | `npx -y mcp-mermaid@0.4.1` | Validates and renders the Mermaid diagrams in `docs/architecture/`. |

Both use `npx`, so no global install is required — the package is fetched on first use.

## Prerequisites

- **Node.js** 18+ (developed against v22). Provides `npx` for the MCP servers.
- **.NET SDK** 8.0 (developed against 8.0.426). Required to build and run the backend and
  the structural test. If `dotnet` is not on your `PATH`, install it from
  <https://dotnet.microsoft.com/download/dotnet/8.0> or via the official install script.

## First-run note (Mermaid MCP)

`mcp-mermaid` pulls in a headless rendering stack, so the **first** `npx` launch downloads
packages and can take a minute. If your MCP client times out on the initial connection,
pre-warm the cache once, then reconnect:

```sh
npm install -g mcp-mermaid@0.4.1
```

After the package is cached/installed, the server starts quickly and the connection
succeeds within the normal timeout.

## Running the pieces

```sh
# Backend + tests (includes the structural architecture test)
dotnet test backend/InvoiceApp.sln

# Just the structural rule test
dotnet test backend/InvoiceApp.sln --filter FullyQualifiedName~DomainDependencyRuleTests

# End-to-end tests
cd e2e && npm install && npx playwright install chromium && npm test
```
