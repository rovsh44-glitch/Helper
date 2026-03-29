# Repo Atlas

Status: `active`
Updated: `2026-03-26`

## Source Map

Use this as the shortest stable map of the repository:

1. [src/Helper.Api](../../src/Helper.Api)
   ASP.NET host, auth/session boundary, readiness, runtime control-plane, endpoint registration.
2. [src/Helper.Runtime](../../src/Helper.Runtime)
   generation, planning, orchestration, templates, workspace/runtime services.
3. [src/Helper.Runtime.Knowledge](../../src/Helper.Runtime.Knowledge)
   document parsing, indexing, and knowledge ingestion.
4. [src/Helper.Runtime.Evolution](../../src/Helper.Runtime.Evolution)
   evolution/runtime support services.
5. [components](../../components)
   UI panels and reusable presentational pieces.
6. [contexts](../../contexts)
   narrow shared-state providers by domain.
7. [services](../../services)
   browser transport, generated client, and client-side workflow services.
8. [scripts](../../scripts)
   operator automation, gates, certification flows, hygiene, smoke, and perf tooling.
9. [mcp_config](../../mcp_config)
   versioned extension manifest schema, checked-in built-in/internal manifests, and disabled sample providers.
10. [doc](..)
   canonical docs, certification hubs, evidence, ADRs, and archives.
11. [test](../../test)
    backend and runtime regression coverage.

## Runtime Boundaries

Historical note: older records may still refer to previous API/runtime aliases.
Those names map to the current Helper-first module layout.

Canonical product-facing transport/config naming is:

1. `Helper:*`
2. `/api/helper/*`
3. `/hubs/helper`

Legacy compatibility aliases remain only as time-bound exceptions.

The repository is not the runtime data root.

Runtime outputs must resolve outside source surfaces:

1. `HELPER_DATA_ROOT`
2. `HELPER_PROJECTS_ROOT`
3. `HELPER_LIBRARY_ROOT`
4. `HELPER_LOGS_ROOT`
5. `HELPER_TEMPLATES_ROOT`

## Solution And Framework Policy

`Helper.sln` explicitly includes the primary runtime graph, including:

1. `Helper.Runtime`
2. `Helper.Runtime.WebResearch`
3. `Helper.Runtime.Knowledge`
4. `Helper.Runtime.Evolution`

Target-framework split is intentional:

1. main runtime/api surfaces: `net8.0`
2. runtime review slice and selected verification surfaces: `net9.0`

## Where To Start

1. product behavior: [App.tsx](../../App.tsx)
2. API boot: [Program.cs](../../src/Helper.Api/Program.cs)
3. planning/generation APIs: [EndpointRegistrationExtensions.Evolution.Generation.cs](../../src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Generation.cs)
4. research/rag APIs: [EndpointRegistrationExtensions.Evolution.Research.cs](../../src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Research.cs)
5. docs/process: [doc/README.md](../README.md)

