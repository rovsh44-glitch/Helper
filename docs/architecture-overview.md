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

- `Genesis Core` as a legacy UI label for the main generation workspace
- `Runtime Console`
- `Strategic Map`
- `Objectives`
- `Architecture`
- `Evolution`
- `Live Builder`
- `Library Indexing`
- `Settings`

`Genesis Core` in this section refers to a UI surface label, not the backend module name. The public runtime layers are `Helper.Api` and `Helper.Runtime`.

## Data Boundary

Code and canonical docs stay in the repository. Runtime data, auth material, logs, projects, templates, and library roots are expected outside the repository.

## Visual Reference

- [Architecture diagram](../media/helper-architecture-overview.svg)

## Public Boundary Note

The private-core technical references are intentionally not published in this showcase repository. The public repo is meant to communicate the architecture shape, not disclose the entire implementation corpus.
