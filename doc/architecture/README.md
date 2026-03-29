# HELPER Architecture Atlas

Status: `active`
Updated: `2026-03-29`

## Purpose

This atlas documents HELPER by stable primitives and public-safe operating notes.

Read in this order:

1. [System Overview](SYSTEM_OVERVIEW.md)
2. [Runtime Surfaces](RUNTIME_SURFACES.md)
3. [Frontend Structure](FRONTEND_STRUCTURE.md)
4. [Telemetry Vocabulary](../telemetry/VOCABULARY.md)
5. [Capability Catalogs](CAPABILITY_CATALOGS.md)
6. [Repo Atlas](REPO_ATLAS.md)
7. [Runtime Review Slice Plan](HELPER_RUNTIME_REVIEW_SLICE_IMPLEMENTATION_PLAN_2026-03-25.md)
8. [Public Tree Policy](HELPER_PUBLIC_ZERO_LEGACY_REPO_PLAN_2026-03-29.md)
9. [Repository Cutover Playbook](HELPER_PUBLIC_REPO_CUTOVER_PLAN_2026-03-29.md)
10. [ADR Index](../adr/README.md)

## Stable Primitives

1. `UI shell`: React application, tab model, and shared client-side state.
2. `API host`: ASP.NET host, auth boundary, readiness, control-plane, and endpoint surface.
3. `Helper runtime`: conversation, planning, generation, indexing, evolution, and governance services.
4. `Workspace/runtime roots`: externalized data, projects, templates, logs, and auth artifacts.
5. `Certification/evidence`: counted execution, parity, smoke, and operator review artifacts.

## Boundary

Use this atlas for system understanding. Keep dated private-only working notes outside the future public tree.
