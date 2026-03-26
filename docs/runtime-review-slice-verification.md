# Runtime Review Slice Verification

This note defines the canonical public verification path for the `runtime-review-slice`.

Use it to answer a narrow question: can an external reviewer build, test, run, and inspect the public slice without needing the private core.

## Verification Target

Successful public verification means all of the following are true:

1. the slice frontend builds locally
2. the slice API builds locally
3. the public slice tests pass
4. the local slice host starts and serves the read-only UI and API
5. the observed behavior stays within the documented public proof boundary

It does not mean that the full HELPER private core has been verified.

## Prerequisites

Use a machine with:

- PowerShell
- Node.js and npm
- .NET 9 SDK

The .NET target for the slice is `net9.0`, as defined in:

- [`../runtime-review-slice/src/Helper.RuntimeSlice.Api/Helper.RuntimeSlice.Api.csproj`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Helper.RuntimeSlice.Api.csproj)
- [`../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/Helper.RuntimeSlice.Api.Tests.csproj`](../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/Helper.RuntimeSlice.Api.Tests.csproj)

## Clean-Machine Setup

From the `runtime-review-slice` directory, the canonical Stage 1 setup is already built into the public test script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
```

That script now performs:

1. `npm ci` against the committed `package-lock.json`
2. `dotnet restore` for the public xUnit test project
3. frontend build
4. API build
5. test-project build
6. public slice test execution

If you want to prefetch dependencies manually before running the script, use:

```powershell
npm ci
dotnet restore test\Helper.RuntimeSlice.Api.Tests\Helper.RuntimeSlice.Api.Tests.csproj
```

## Canonical Build And Test Path

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
```

That script performs the public test path in this order:

1. `npm ci`
2. `dotnet restore test\Helper.RuntimeSlice.Api.Tests\Helper.RuntimeSlice.Api.Tests.csproj`
3. `npm run build`
4. `dotnet build src\Helper.RuntimeSlice.Api\Helper.RuntimeSlice.Api.csproj -c Debug -m:1 --no-restore`
5. `dotnet build test\Helper.RuntimeSlice.Api.Tests\Helper.RuntimeSlice.Api.Tests.csproj -c Debug -m:1 --no-restore`
6. `dotnet test test\Helper.RuntimeSlice.Api.Tests\Helper.RuntimeSlice.Api.Tests.csproj -c Debug --no-build`

Expected result:

- Vite produces a frontend build without errors
- the API project builds successfully
- the slice test project builds successfully
- the xUnit test suite passes

Representative test coverage includes:

- [`RuntimeSliceLogServiceTests.cs`](../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/RuntimeSliceLogServiceTests.cs)
- [`RuntimeLogSemanticDeriverTests.cs`](../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/RuntimeLogSemanticDeriverTests.cs)
- [`RuntimeSliceRouteTelemetryServiceTests.cs`](../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/RuntimeSliceRouteTelemetryServiceTests.cs)
- [`RuntimeSliceOpenApiDocumentFactoryTests.cs`](../runtime-review-slice/test/Helper.RuntimeSlice.Api.Tests/RuntimeSliceOpenApiDocumentFactoryTests.cs)

## Fixture Assumptions

The canonical public test path is intentionally fixture-backed.

It assumes:

1. the checked-in `sample_data/` tree is present
2. `sample_data/logs/` is present
3. fixtures remain sanitized according to [`../runtime-review-slice/sample_data/README.md`](../runtime-review-slice/sample_data/README.md)
4. tests run against those checked-in fixtures rather than live private-core state

If a reviewer swaps in a different fixture root for manual runtime experiments, that is outside the canonical Stage 1 public test contract.

## Canonical Run Path

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/start.ps1
```

That script:

1. sets `ASPNETCORE_URLS` to `http://127.0.0.1:5076`
2. runs `npm run build`
3. starts the slice API host with `dotnet run`

Then open:

`http://localhost:5076`

## Manual Verification Checklist

After the slice starts, a reviewer should confirm:

1. `/api/health` returns a healthy status payload
2. `/api/about` returns `fixtureMode: true`
3. `/api/openapi.json` returns the slice API contract
4. the UI loads and exposes the four panels:
   - `Runtime Console`
   - `Evolution`
   - `Library Indexing`
   - `Route Telemetry`
5. runtime logs render sanitized entries rather than raw private paths
6. evolution, library, and route telemetry views render fixture-backed data rather than empty placeholders

## What Counts As A Successful Public Verification

A public reviewer can treat verification as successful if:

1. setup completes with public dependencies only
2. the canonical test script passes
3. the canonical start script serves the slice locally
4. the documented endpoints return data
5. the observed behavior matches the scope described in:
   - [`runtime-review-slice-architecture.md`](runtime-review-slice-architecture.md)
   - [`public-proof-boundary.md`](public-proof-boundary.md)

## Expected Failure Modes

The most likely public verification failures are:

### `npm` or frontend build failures

Likely meaning:

- Node.js or npm is missing
- locked frontend dependencies could not be installed
- the local frontend toolchain is broken

### `dotnet restore`, `dotnet build`, or `dotnet test` failures

Likely meaning:

- the .NET 9 SDK is missing
- package restore has not completed
- the local .NET toolchain is broken

### `package-lock.json` missing or stale

Likely meaning:

- the committed JS lockfile was not included in the working tree
- `package.json` changed without regenerating the lockfile

### Fixture preflight failure before build/test

Likely meaning:

- the checked-in `sample_data/` tree is incomplete
- the checked-in `sample_data/logs/` tree is incomplete
- the reviewer is not running from the slice root

### `Runtime slice fixture root is missing`

Likely meaning:

- the slice is being run from the wrong working directory
- `sample_data/` is missing
- the fixture-root override points to an invalid location

### Fixture sanitization exceptions

Likely meaning:

- fixture content contains a non-redacted Windows path
- fixture content contains token-like material
- fixture content contains a non-local URL

These are boundary-protection failures, not proof that the private core is broken.

### Port-binding failure on `5076`

Likely meaning:

- another local process is already using the configured port

### Blank or partial UI after startup

Likely meaning:

- the frontend build did not complete
- the slice host was interrupted
- the reviewer is opening the wrong URL

## What This Verification Path Does Not Prove

This verification path does not prove:

- the full private-core runtime
- live model execution
- generation and template lifecycle
- web research integrations
- chat and conversation flows
- certification completion
- human-level parity

It proves only the public-safe runnable slice and the narrow engineering boundary around it.

## Not Required For Basic Public Verification

The following are not required for successful public verification:

- the private core
- external model keys
- external provider accounts
- private operator scripts
- `scripts/refresh-openapi.ps1`

## Practical Reading Rule

Read this note together with:

- [`../runtime-review-slice/README.md`](../runtime-review-slice/README.md)
- [`runtime-review-slice-architecture.md`](runtime-review-slice-architecture.md)
- [`public-proof-boundary.md`](public-proof-boundary.md)

That set gives the shortest complete picture of how to run the slice, what it is made of, and what its successful verification does and does not prove.
