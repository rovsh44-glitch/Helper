# HELPER Architecture Overview

## Runtime Layers

HELPER has three primary layers:

1. React UI in the repository root
2. `Helper.Api` as the browser-facing API boundary
3. `Helper.Runtime` as the backend runtime and domain implementation

Main flow:

`Browser UI -> Helper.Api -> Helper.Runtime runtime services`

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
- library indexing and knowledge ingestion

## Product Surfaces

- `Primary Workspace` as the main generation workspace label
- `Runtime Console`
- `Strategic Map`
- `Objectives`
- `Architecture`
- `Evolution`
- `Live Builder`
- `Library Indexing`
- `Settings`

`Primary Workspace` in this section refers to a UI surface label, not the backend module name. The public runtime layers are `Helper.Api` and `Helper.Runtime`.

## Data Boundary

Code and canonical docs stay in the repository. Runtime data, auth material, logs, projects, templates, and library roots are expected outside the repository.

## Naming And Solution Policy

Canonical transport/config naming uses `Helper:*`, `/api/helper/*`, and `/hubs/helper`.
Legacy compatibility aliases are time-bound and deprecated.

The solution view intentionally includes the full primary runtime graph, and the framework split is deliberate:

1. production runtime and API stay on `net8.0`
2. runtime slice and selected verification surfaces stay on `net9.0`

## Public Boundary

This document describes the product architecture at a public overview level.

The private implementation repo contains the full runtime graph, but that code is intentionally outside this public default branch.

For the public repository boundary, use:

- [Repository scope](repository-scope.md)
- [Public release checklist](public-release-checklist.md)
