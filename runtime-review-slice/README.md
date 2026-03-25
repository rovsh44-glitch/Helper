# Helper Runtime Review Slice

`Helper Runtime Review Slice` is the first public-safe runnable slice for HELPER.

It proves a narrow operator-facing boundary:

1. `Runtime Console`
2. `Evolution`
3. `Library Indexing`
4. `Route Telemetry`

This slice is:

1. read-only
2. fixture-backed
3. sanitized
4. local-first
5. intentionally separate from the private core

## Quickstart

From the `runtime-review-slice` directory:

```powershell
npm install
dotnet restore
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
```

To run the slice:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/start.ps1
```

Then open:

`http://localhost:5076`

## API Surface

1. `GET /api/about`
2. `GET /api/health`
3. `GET /api/readiness`
4. `GET /api/openapi.json`
5. `GET /api/runtime/logs`
6. `GET /api/evolution/status`
7. `GET /api/evolution/library`
8. `GET /api/telemetry/routes`

## Boundary

Excluded from this slice:

1. chat and conversation flows
2. generation and template lifecycle
3. auth bootstrap and machine keys
4. web research providers
5. private-core scripts
6. live model execution

## Sample Data

Fixtures live under:

`sample_data/`

They are sanitized and intended to prove public boundary shape, not the full private runtime.
