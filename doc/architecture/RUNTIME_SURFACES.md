# Runtime Surfaces

Status: `active`
Updated: `2026-03-16`

## Browser Surfaces

The UI exposes these primary surfaces:

1. `Helper Core`
2. `Runtime Console`
3. `Strategic Map`
4. `Objectives`
5. `Architecture Planner`
6. `Evolution`
7. `Library Indexing`
8. `Live Builder`
9. `Settings`

## State Model

Current shared client-side state is intentionally split by domain, not centralized into one global god object:

1. `HelperHubProvider`
   SignalR-driven progress, thoughts, and mutation proposals.
2. `WorkflowStateProvider`
   strategy task/context/analysis and planner prompt/target/analysis.
3. `OperationsRuntimeProvider`
   shared evolution/indexing status, library, and lifecycle actions.
4. `GoalsStateProvider`
   objective lifecycle state and CRUD actions.
5. `BuilderWorkspaceProvider`
   current workspace session, selection, editor state, and build logs.

## Backend Surfaces

The browser talks to the backend through four broad surfaces:

1. `conversation`
2. `runtime_console`
3. `builder`
4. `evolution`

Auth/session bootstrap and scope boundaries are documented in [TRUST_MODEL.md](../security/TRUST_MODEL.md) and [ADR_BROWSER_AUTH_SESSION_BOOTSTRAP.md](../adr/ADR_BROWSER_AUTH_SESSION_BOOTSTRAP.md).

## Optional Extension Surface

Optional MCP providers are no longer defined by ad hoc `servers.json` semantics. They now load from versioned manifests under [mcp_config](../../mcp_config), with explicit category, trust level, transport, required env, and certification-mode behavior.

## Operational Rule

If a new UI panel needs state:

1. prefer reusing one of the domain-specific providers above
2. create a new narrow provider only when the state represents a distinct domain boundary
3. do not add another omniscient `App`-level orchestrator object
