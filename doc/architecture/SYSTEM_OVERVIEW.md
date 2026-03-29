# System Overview

Status: `active`
Updated: `2026-03-26`

## Topology

HELPER is a local-first product shell with three primary runtime layers:

1. React UI in the repository root
2. `Helper.Api` as the backend host and browser-facing boundary
3. `Helper.Runtime` and related projects as the domain/runtime implementation

## Main Flows

### Product Shell

`[Browser UI] -> [Helper.Api] -> [Helper runtime services]`

The browser never owns long-lived backend secrets. It bootstraps scoped sessions from the API host and uses generated client calls plus SignalR for live runtime state.

Historical note: older records may still refer to previous API/runtime aliases.
Those names map to the current `Helper.Api` and `Helper.Runtime` topology.

### Engineering Workflow

`Strategic Map -> Architecture Planner -> Live Builder -> Build / Mutation / Workspace lifecycle`

This is the main operator-facing product pipeline and is now backed by shared client state so context survives tab switches.

### Knowledge / Operations Workflow

`Evolution / Indexing panels -> shared operations runtime -> learning coordinator -> runtime library / queue / telemetry`

These surfaces now share one runtime state source instead of maintaining parallel local pollers.

## Main Code Entry Points

1. UI shell: [App.tsx](../../App.tsx)
2. API startup: [Program.cs](../../src/Helper.Api/Program.cs)
3. API endpoint registration: [EndpointRegistrationExtensions.cs](../../src/Helper.Api/Hosting/EndpointRegistrationExtensions.cs)
4. generation endpoints: [EndpointRegistrationExtensions.Evolution.Generation.cs](../../src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Generation.cs)
5. research/rag endpoints: [EndpointRegistrationExtensions.Evolution.Research.cs](../../src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Research.cs)
6. shared runtime services: [Helper.Runtime](../../src/Helper.Runtime)

## Data Roots

Source tree and runtime tree are intentionally separated:

1. code/docs stay under the repository
2. runtime data lives under `HELPER_DATA_ROOT`
3. auth key material lives under `HELPER_DATA_ROOT`, not under `src/`
4. projects, logs, templates, and library roots are resolved from the runtime configuration

See [Repo Hygiene And Runtime Artifact Governance](../security/REPO_HYGIENE_AND_RUNTIME_ARTIFACT_GOVERNANCE.md).

