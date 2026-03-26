# Runtime Review Slice Architecture

This note explains the actual engineering shape of the public `runtime-review-slice`.

It is meant to answer a narrow question: what is real, what is fixture-backed, and what is intentionally absent from this public slice.

## What This Slice Is

The runtime review slice is a local-first, read-only, fixture-backed application composed of:

- a minimal ASP.NET Core API
- a React frontend
- shared DTO contracts
- a checked-in fixture set
- a small test suite around the public slice behavior

It is not a mock landing page. It is also not the full HELPER runtime.

## Directory Map

- [`../runtime-review-slice/src/Helper.RuntimeSlice.Api/`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/) contains the API host, endpoint wiring, fixture access, and slice services
- [`../runtime-review-slice/src/Helper.RuntimeSlice.Contracts/`](../runtime-review-slice/src/Helper.RuntimeSlice.Contracts/) contains the DTOs exposed by the slice
- [`../runtime-review-slice/web/`](../runtime-review-slice/web/) contains the React UI
- [`../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/`](../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/) contains the public slice tests
- [`../runtime-review-slice/sample_data/`](../runtime-review-slice/sample_data/) contains the sanitized fixtures used by the slice
- [`../runtime-review-slice/openapi/runtime-review-openapi.json`](../runtime-review-slice/openapi/runtime-review-openapi.json) contains a checked-in API contract snapshot

## Runtime Topology

At runtime, the slice works like this:

1. [`Program.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Program.cs) discovers the slice root, resolves the fixture root, and registers the read-only services.
2. The API exposes a narrow set of endpoints under `/api/*`.
3. Each service reads sanitized fixture data through [`FixtureFileStore.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/FixtureFileStore.cs).
4. [`FixtureSecurityGuard.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/FixtureSecurityGuard.cs) validates fixture contents before they are returned.
5. The React app in [`App.tsx`](../runtime-review-slice/web/src/App.tsx) loads the snapshot endpoints and renders four operator-facing panels.

The frontend does not talk to live model providers. The backend does not call the private core. The slice is intentionally self-contained.

## Endpoint And Service Map

| UI / artifact | Endpoint | Service or source | Backing data |
| --- | --- | --- | --- |
| About / boundary summary | `/api/about` | [`RuntimeSliceAboutService.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/RuntimeSliceAboutService.cs) | in-memory slice options |
| Health | `/api/health` | inline endpoint in [`Program.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Program.cs) | in-memory status payload |
| Startup readiness | `/api/readiness` | [`RuntimeSliceReadinessService.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/RuntimeSliceReadinessService.cs) | [`readiness.json`](../runtime-review-slice/sample_data/readiness.json) |
| Evolution status | `/api/evolution/status` | [`RuntimeSliceEvolutionService.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/RuntimeSliceEvolutionService.cs) | [`evolution_status.json`](../runtime-review-slice/sample_data/evolution_status.json) |
| Library indexing queue | `/api/evolution/library` | [`RuntimeSliceLibraryService.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/RuntimeSliceLibraryService.cs) | [`indexing_queue.json`](../runtime-review-slice/sample_data/indexing_queue.json) |
| Runtime console | `/api/runtime/logs` | [`RuntimeSliceLogService.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/RuntimeSliceLogService.cs) and [`RuntimeLogSemanticDeriver.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/RuntimeLogSemanticDeriver.cs) | [`sample_data/logs/`](../runtime-review-slice/sample_data/logs/) |
| Route telemetry | `/api/telemetry/routes` | [`RuntimeSliceRouteTelemetryService.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/RuntimeSliceRouteTelemetryService.cs) | [`route_telemetry.jsonl`](../runtime-review-slice/sample_data/route_telemetry.jsonl) |
| OpenAPI document | `/api/openapi.json` | [`RuntimeSliceOpenApiDocumentFactory.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/RuntimeSliceOpenApiDocumentFactory.cs) | in-memory document factory plus checked-in OpenAPI snapshot |

## What The Sample Data Represents

The fixtures are sanitized snapshots derived from HELPER-oriented runtime scenarios.

They are used to prove:

- endpoint shape
- DTO structure
- UI composition
- log parsing and semantic enrichment behavior
- route telemetry aggregation behavior

They are not used to prove:

- live model execution
- live operator state
- private-core orchestration
- full production telemetry

## Fixture Safety And Boundary Controls

The slice deliberately uses a small amount of explicit guardrail code.

- [`FixtureSecurityGuard.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/FixtureSecurityGuard.cs) rejects non-redacted Windows paths, token-like material, and non-local URLs.
- [`RuntimeSlicePaths.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/RuntimeSlicePaths.cs) constrains fixture discovery to the slice root or an explicitly provided fixture override.
- The API only exposes `GET` endpoints.
- The frontend renders snapshots and does not offer mutation flows.

These controls matter because the public slice is supposed to be inspectable without accidentally exposing private operational data.

## What Is Real In This Public Slice

- the API host and endpoint wiring
- the React UI
- DTO contracts
- fixture loading and validation
- log parsing and semantic derivation
- route telemetry normalization and aggregation
- the checked-in OpenAPI contract
- the public slice tests

Representative tests include:

- [`RuntimeSliceLogServiceTests.cs`](../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/RuntimeSliceLogServiceTests.cs)
- [`RuntimeSliceRouteTelemetryServiceTests.cs`](../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/RuntimeSliceRouteTelemetryServiceTests.cs)
- [`RuntimeSliceOpenApiDocumentFactoryTests.cs`](../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/RuntimeSliceOpenApiDocumentFactoryTests.cs)

## What Is Intentionally Absent

This slice does not include:

- chat and conversation flows
- generation and template lifecycle
- web research providers
- auth bootstrap and machine keys
- private-core scripts
- live writes into HELPER data roots
- live model execution
- the full orchestration runtime

Those omissions are deliberate. They keep the slice public-safe and keep the claim boundary narrow.

## Practical Reading Rule

Read this note together with:

- [`../runtime-review-slice/README.md`](../runtime-review-slice/README.md)
- [`runtime-review-slice-verification.md`](runtime-review-slice-verification.md)
- [`public-proof-boundary.md`](public-proof-boundary.md)
- [`due-diligence-readiness.md`](due-diligence-readiness.md)

That combination explains both the engineering shape of the slice and the limits of what the public repository proves.
