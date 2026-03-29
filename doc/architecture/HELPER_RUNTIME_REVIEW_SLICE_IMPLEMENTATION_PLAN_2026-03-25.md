# HELPER Runtime Review Slice Implementation Plan

Status: `completed 2026-03-26`
Updated: `2026-03-26`

Closure report:

- [HELPER_RUNTIME_REVIEW_SLICE_IMPLEMENTATION_CLOSURE_REPORT_2026-03-26.md](./HELPER_RUNTIME_REVIEW_SLICE_IMPLEMENTATION_CLOSURE_REPORT_2026-03-26.md)

## Decision

The first public-safe runnable slice for HELPER should be `Helper Runtime Review Slice`.

This slice exposes only read-only operator-review surfaces:

1. `Runtime Console`
2. `Evolution`
3. `Library Indexing`
4. `Route Telemetry`

It should not expose chat, generation, auth bootstrap, template lifecycle, web-research providers, or private-core runtime scripts.

## Why This Slice

This slice is the strongest public-safe proof because it demonstrates what HELPER is actually differentiated on:

1. operator-facing runtime visibility
2. structured telemetry instead of opaque model behavior
3. local-first review flows
4. real API contracts and real UI surfaces

It also maps cleanly onto code that already exists in the main repo:

1. [RuntimeLogService](../../src/Helper.Api/Hosting/RuntimeLogService.cs)
2. [RuntimeLogSemanticDeriver](../../src/Helper.Api/Hosting/RuntimeLogSemanticDeriver.cs)
3. [RouteTelemetryService](../../src/Helper.Runtime/RouteTelemetryService.cs)
4. [RuntimeTelemetryContracts](../../src/Helper.Runtime/Core/Contracts/RuntimeTelemetryContracts.cs)
5. [StartupReadinessService](../../src/Helper.Api/Hosting/StartupReadinessService.cs)
6. [EndpointRegistrationExtensions.SystemAndGovernance](../../src/Helper.Api/Hosting/EndpointRegistrationExtensions.SystemAndGovernance.cs)
7. [EndpointRegistrationExtensions.Evolution.Runtime](../../src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Runtime.cs)

## Outcome

The deliverable is a public-safe mini-product that a technical reviewer can:

1. clone
2. build
3. run locally in under five minutes
4. inspect through real HTTP endpoints and a real UI
5. validate through tests without any model key or private runtime dependency

## Non-Goals

The slice must not try to prove:

1. full HELPER conversation quality
2. generation parity
3. human-level parity
4. private-core orchestration depth
5. production deployment readiness of the full system

The slice is a boundary proof, not a proof of the entire private engine.

## Public Boundary

The slice must be default-safe by construction.

Allowed:

1. sanitized fixture data
2. read-only API endpoints
3. local-only runtime review UI
4. deterministic tests
5. public-safe OpenAPI contract

Disallowed:

1. live model calls
2. private templates
3. internal auth keys
4. operator-only scripts
5. machine-specific paths
6. hidden dependency on `HELPER_DATA_ROOT`
7. direct access to real private logs or private evidence bundles

## Recommended Repo Shape

Implementation should be built in the main repo first, then exported to the public showcase repo.

Add these new paths:

1. `src/Helper.RuntimeSlice.Contracts/`
2. `src/Helper.RuntimeSlice.Api/`
3. `src/Helper.RuntimeSlice.Web/`
4. `test/Helper.RuntimeSlice.Api.Tests/`
5. `test/Helper.RuntimeSlice.Web.E2E/`
6. `slice/runtime-review/sample_data/`
7. `slice/runtime-review/README.md`
8. `slice/runtime-review/openapi/runtime-review-openapi.json`
9. `slice/runtime-review/scripts/`

## Architecture

### Backend

The backend should be a new minimal ASP.NET host, not a thin wrapper around the full `Helper.Api` startup path.

Reason:

1. direct reference to the full host would drag too many unrelated services
2. build reproducibility would degrade
3. the public boundary would become harder to audit
4. accidental disclosure risk would increase

The new backend should selectively reuse trimmed logic and contracts from safe areas only.

### Frontend

The frontend should be a small React app with exactly four panels:

1. `Runtime Console`
2. `Evolution`
3. `Library Indexing`
4. `Route Telemetry`

No global product shell clone is needed. The point is to prove a narrow, inspectable operator surface.

## Exact API Surface

The slice should expose these endpoints only:

1. `GET /api/health`
2. `GET /api/readiness`
3. `GET /api/openapi.json`
4. `GET /api/runtime/logs`
5. `GET /api/evolution/status`
6. `GET /api/evolution/library`
7. `GET /api/telemetry/routes`

Optional:

1. `GET /api/about`
2. `GET /api/version`

Do not include write endpoints in the first public-safe release.

## Exact Data Contracts

The first release should use a dedicated contracts project and keep only public-safe DTOs:

1. `StartupReadinessSnapshot`
2. `RuntimeLogSourceDto`
3. `RuntimeLogEntryDto`
4. `RuntimeLogSemanticsDto`
5. `RuntimeLogsSnapshotDto`
6. `LibraryItemDto`
7. `RouteTelemetryEvent`
8. `RouteTelemetryBucket`
9. `RouteTelemetrySnapshot`

Do not reference the entire [ApiContracts.cs](../../src/Helper.Api/Hosting/ApiContracts.cs) file from the public slice. Extract only the DTOs that the slice needs.

## Fixture Model

The slice should be fixture-backed by default.

Required fixture files:

1. `sample_data/logs/runtime-main.log`
2. `sample_data/logs/indexing-worker.log`
3. `sample_data/indexing_queue.json`
4. `sample_data/evolution_status.json`
5. `sample_data/route_telemetry.jsonl`
6. `sample_data/readiness.json`

Fixture design rules:

1. every file must be generated from a real local session and then sanitized
2. every path must be redacted to public-safe placeholders
3. every API response must work without external processes
4. every fixture should be stable enough for snapshot tests

## Redaction Rules

Before any fixture enters the slice, run mandatory redaction against:

1. Windows user names
2. absolute drive paths
3. `HELPER_DATA_ROOT` locations
4. local hostnames
5. API keys, tokens, session material
6. internal URLs
7. reviewer names or personal metadata

Replace them with deterministic placeholders:

1. `C:/REDACTED_RUNTIME/...`
2. `USER_REDACTED`
3. `TOKEN_REDACTED`
4. `HOST_REDACTED`

The redaction pass must be scriptable and repeatable.

## Step-By-Step Plan

### Phase 0: Freeze Scope

1. Create a branch or workstream for `runtime-review-slice`.
2. Freeze the first release scope to the seven read-only endpoints above.
3. Freeze the first release UI to the four panels above.
4. Write a boundary note that explicitly excludes chat, generation, auth, and research execution.

Exit criteria:

1. one written scope statement exists
2. one written non-goals list exists
3. no one is still treating this slice as a mini full HELPER

### Phase 1: Extract Safe Contracts

1. Create `src/Helper.RuntimeSlice.Contracts/`.
2. Move or copy only the DTOs required for readiness, logs, indexing, and route telemetry.
3. Keep namespaces neutral and public-safe.
4. Remove any dependency on conversation, auth, generation, or internal control-plane packages.
5. Add unit tests for serialization shape of the extracted DTOs.

Exit criteria:

1. contracts project builds independently
2. contracts have no dependency on `Helper.Api.Conversation`
3. contracts have no dependency on model gateway or auth code

### Phase 2: Build Fixture Loaders

1. Create `src/Helper.RuntimeSlice.Api/Fixtures/`.
2. Implement loader services for readiness, logs, indexing queue, and route telemetry fixtures.
3. Add schema validation for all fixture files on startup.
4. Fail fast with readable errors if fixture files are missing or malformed.
5. Keep the fixture root configurable, but default it to `slice/runtime-review/sample_data`.

Exit criteria:

1. the backend can start without any external runtime
2. malformed fixture input fails deterministically
3. all fixtures load from relative repo paths

### Phase 3: Port Runtime Log Review Logic

1. Reuse the logic shape from [RuntimeLogService.cs](../../src/Helper.Api/Hosting/RuntimeLogService.cs).
2. Reuse or trim [RuntimeLogSemanticDeriver.cs](../../src/Helper.Api/Hosting/RuntimeLogSemanticDeriver.cs).
3. Make the service fixture-first, not filesystem-discovery-first.
4. Keep severity derivation, timestamp parsing, and semantic projection.
5. Add explicit redaction checks so no raw absolute paths leak to UI responses.

Exit criteria:

1. `GET /api/runtime/logs` returns stable structured output
2. semantics fields are present for representative lines
3. no response contains raw local machine paths

### Phase 4: Port Evolution And Indexing Snapshots

1. Define public-safe DTO shape for evolution status using the existing `GET /api/evolution/status` response as the model.
2. Define library queue response using `LibraryItemDto`.
3. Implement fixture-backed services for:
   `EvolutionStatusService`
   `LibraryQueueService`
4. Keep the initial version read-only.
5. Add alerts when fixture coverage is incomplete.

Exit criteria:

1. `GET /api/evolution/status` returns meaningful progress state
2. `GET /api/evolution/library` returns a non-empty queue snapshot
3. the UI can render both screens without special-case fake code

### Phase 5: Port Route Telemetry

1. Reuse the model from [RuntimeTelemetryContracts.cs](../../src/Helper.Runtime/Core/Contracts/RuntimeTelemetryContracts.cs).
2. Reuse the aggregation logic shape from [RouteTelemetryService.cs](../../src/Helper.Runtime/RouteTelemetryService.cs).
3. Implement fixture ingestion from `route_telemetry.jsonl`.
4. Add alert generation when degraded or failed events dominate the recent window.
5. Expose the result on `GET /api/telemetry/routes`.

Exit criteria:

1. route snapshots aggregate correctly
2. alerts appear under degraded fixture scenarios
3. the endpoint stays independent from the full control-plane object graph

### Phase 6: Build The Minimal API Host

1. Create `src/Helper.RuntimeSlice.Api/Program.cs`.
2. Register only the services required for the seven endpoints.
3. Publish a dedicated `OpenApiDocumentFactory` for the slice.
4. Add permissive local CORS for the slice frontend only.
5. Disable auth entirely for v1 unless a simple local-only token is strictly needed.

Exit criteria:

1. `dotnet run` starts the slice backend on a known local port
2. `/api/openapi.json` matches the actual runtime surface
3. the backend has no dependency on model keys

### Phase 7: Build The Minimal Frontend

1. Create `src/Helper.RuntimeSlice.Web/`.
2. Add a compact shell with four tabs only.
3. Build `Runtime Console` first because it is the strongest proof surface.
4. Add `Evolution`, `Library Indexing`, and `Route Telemetry`.
5. Keep the UI clean, narrow, and explicit about fixture mode.
6. Add one banner stating that this is a public-safe runtime review slice backed by sanitized sample data.

Exit criteria:

1. all four panels render from live backend responses
2. no panel requires hidden mock branches
3. the UI reads as an operator review tool, not as a generic admin dashboard

### Phase 8: Create Real Sanitized Fixtures

1. Export one real internal session's logs and runtime snapshots.
2. Sanitize them with a scripted redaction pass.
3. Review the redacted fixtures manually.
4. Add a fixture provenance note saying they come from a real local HELPER session and have been redacted.
5. Freeze these fixtures for v1.

Exit criteria:

1. fixture provenance is documented
2. redaction review is complete
3. public-safe sample data is actually real, not invented noise

### Phase 9: Testing

1. Add API tests for all seven endpoints.
2. Add serialization snapshot tests for the DTOs.
3. Add unit tests for log severity and semantic derivation.
4. Add unit tests for route telemetry aggregation and alerts.
5. Add E2E tests for frontend render and tab switching.
6. Add one end-to-end smoke path:
   `start backend -> open UI -> four panels load -> expected text is visible`

Exit criteria:

1. tests run locally without external infrastructure
2. green test run is reproducible
3. no hidden dependency on the full HELPER runtime remains

### Phase 10: Packaging And DX

1. Add `slice/runtime-review/README.md`.
2. Add exact commands for install, run, test, and expected output.
3. Add `slice/runtime-review/scripts/start.ps1`.
4. Add `slice/runtime-review/scripts/test.ps1`.
5. Export a stable OpenAPI snapshot to `slice/runtime-review/openapi/runtime-review-openapi.json`.
6. Optionally add a release zip only after source-based startup works cleanly.

Exit criteria:

1. reviewer can go from clone to running UI in under five minutes
2. README is enough without tribal knowledge
3. OpenAPI snapshot matches the running service

### Phase 11: Public Repo Integration

1. Add a new section in `showcase_repo/README.md` linking to the runnable slice.
2. Add a doc entry in `showcase_repo/docs/demo-guide.md`.
3. Add one screenshot or short GIF from the running slice.
4. Keep the current honest-status disclaimer unchanged.
5. Make sure the repo still clearly says this slice is narrow and not the full private core.

Exit criteria:

1. the public repo gains a real technical proof path
2. no one can reasonably misread the slice as the entire product
3. the showcase becomes stronger without overstating scope

## Implementation Order

The order should stay strict:

1. contracts
2. fixture loaders
3. runtime logs
4. evolution and library
5. route telemetry
6. API host
7. frontend
8. fixture sanitization
9. tests
10. packaging
11. public repo integration

Do not build the frontend first. That would recreate the current problem: polished surface before technical proof.

## Acceptance Criteria

The slice is complete only when all of the following are true:

1. it builds without private secrets
2. it runs without external services
3. it ships real sanitized fixtures
4. it exposes only read-only public-safe endpoints
5. it has passing tests
6. it has a five-minute quickstart
7. it proves real HELPER boundary logic rather than mock-only demo code

## What This Will Improve

If implemented correctly, this slice will materially improve HELPER's technical reputation because it will let a reviewer verify:

1. code quality on a real slice
2. API contract discipline
3. frontend and backend integration quality
4. reproducibility
5. observability vocabulary
6. redaction and boundary discipline

It will not prove the entire private core. It will prove that HELPER can expose a real, inspectable, engineering-grade public boundary.

## Post-v1 Extensions

Only after the runtime review slice is stable should HELPER consider:

1. a public release build
2. a second slice for library indexing internals
3. a narrow executable generation validation slice
4. a public-safe CLI for loading alternative fixture packs

The first release should stay narrow.
