# HELPER Architecture Overview

## Runtime Layers

HELPER has three primary layers:

1. React UI in the repository root
2. `Gemini.Api` as the browser-facing API boundary
3. `Gemini.Genesis` as the backend runtime and domain implementation

Main flow:

`Browser UI -> Gemini.Api -> Gemini.Genesis runtime services`

## Design Principles

- local-first operation
- backend-owned secret boundary
- explicit runtime/data separation through `HELPER_DATA_ROOT`
- source-backed certification and evidence tracking

## Major Product Flows

- research and answer synthesis
- strategic planning and architecture workflow
- generation and workspace lifecycle
- runtime telemetry and operator review

## Data Boundary

Code and canonical docs stay in the repository. Runtime data, auth material, logs, projects, templates, and library roots are expected outside the repository.

## Canonical Technical References

- `doc/architecture/SYSTEM_OVERVIEW.md`
- `doc/architecture/RUNTIME_SURFACES.md`
- `doc/security/REPO_HYGIENE_AND_RUNTIME_ARTIFACT_GOVERNANCE.md`
