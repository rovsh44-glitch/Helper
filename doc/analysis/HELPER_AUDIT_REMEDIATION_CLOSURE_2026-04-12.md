# HELPER Audit Remediation Closure

Date: `2026-04-12`
Source plan: `doc/analysis/HELPER_AUDIT_REMEDIATION_PLAN_2026-04-12.md`
Source audit: `doc/analysis/HELPER_AUDIT_2026-04-12.md`

## Baseline

- baseline branch observed during remediation: `main`
- baseline `HEAD`: `36aa39ccbc9ed3feb485085911580e90059d7106`
- remediation was executed in the existing local worktree; no branch or commit was created as part of this turn

## Fixed

### 1. Gate trust restored

- `scripts/openapi_gate.ps1` now uses the strict filtered-test helper and fails on no-match filtered runs instead of reporting false green
- `scripts/refresh_openapi_snapshot.ps1` now targets the API contract-test project consistently
- `scripts/check_solution_build_coverage.ps1` now compares repository `*.csproj` files under `src` and `test` against `Helper.sln` and a tracked exclusion policy
- the solution coverage script now handles single-project repositories correctly under `Set-StrictMode`
- NuGet audit execution is now split by connectivity boundary:
  - local `scripts/ci_gate.ps1` keeps `best-effort-local`
  - hosted strict-online audit moved out of `repo-gate` into `.github/workflows/nuget-security-audit.yml`
  - the connected workflow clears dead proxy variables, uses repo-owned `NuGet.Config`, and uploads a report artifact

### 2. Canonical solution perimeter closed

- `Helper.sln` now includes:
  - `src/Helper.Runtime.WebResearch.Browser/Helper.Runtime.WebResearch.Browser.csproj`
  - `src/Helper.RuntimeLogSemantics/Helper.RuntimeLogSemantics.csproj`
  - `test/Helper.Runtime.CompilePath.Tests/Helper.Runtime.CompilePath.Tests.csproj`
- tracked policy file added: `scripts/config/solution-project-exclusions.json`
- repo policy note added: `doc/architecture/RUNTIME_SERVICE_AND_TOOLING_POLICIES.md`

### 3. `/api/architecture/plan` made truthful

- `SimplePlanner` no longer fabricates a WPF fallback when planning output is empty or malformed
- typed failure added through `ProjectPlanningException`
- `/api/architecture/plan` now maps planner parse and validation failures to explicit `422 Unprocessable Entity` responses with `planningErrorCode`
- regression coverage added for valid planning and planner-failure API behavior

### 4. Production DI separated from prototype behavior

- production registration now defaults `ICodeExecutor` to `DisabledCodeExecutor`
- prototype runtime services require explicit opt-in through `HELPER_ENABLE_PROTOTYPE_RUNTIME_SERVICES`
- CLI runtime builder now mirrors the same production-vs-prototype split
- fake-success behavior was removed from `PythonSandbox`
- `SimpleCoder` no longer injects WPF-specific assumptions into generic code generation

### 5. Generic shell surface retired from production defaults

- `shell_execute` removed from the default built-in tool registry and extension manifest
- `ToolPermitService` now explicitly denies `shell_execute`
- `ProcessGuard` now blocks interpreter commands such as `pwsh`, `powershell`, `cmd`, `python`, and `node`
- security and catalog tests updated so this surface cannot silently return

### 6. Maintainability follow-up completed

- broad nullability suppression was reduced in the strategy endpoint registration path touched by the planner fix
- historical empty trees `src/SelfEvolvingAI.Infrastructure` and `src/SelfEvolvingAI.Infrastructure.Core` now contain README markers explaining that they are legacy placeholders, not active subsystem roots

## Added Or Updated Regression Coverage

- `test/Helper.Runtime.Tests/SimplePlannerTests.cs`
- `test/Helper.Runtime.Tests/SimpleCoderTests.cs`
- `test/Helper.Runtime.Tests/RuntimeServiceProfileTests.cs`
- `test/Helper.Runtime.Tests/ArchitecturePlanEndpointTests.cs`
- updated runtime/security/catalog tests for solution coverage, shell retirement, process blocking, tool registry, and solution membership assertions

## Verification Evidence

The following checks passed in this workspace on `2026-04-12`:

```powershell
dotnet build .\Helper.sln -m:1 -v:m
dotnet test .\test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj --no-build -m:1 -v:m
dotnet test .\test\Helper.Runtime.Api.Tests\Helper.Runtime.Api.Tests.csproj --no-build -m:1 -v:m
dotnet test .\test\Helper.RuntimeSlice.Api.Tests\Helper.RuntimeSlice.Api.Tests.csproj --no-build -m:1 -v:m
dotnet test .\test\Helper.Runtime.CompilePath.Tests\Helper.Runtime.CompilePath.Tests.csproj --no-build -m:1 -v:m
powershell -ExecutionPolicy Bypass -File .\scripts\check_solution_build_coverage.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\openapi_gate.ps1
npm run frontend:check
npm run docs:check
npm run config:check
npm run security:scan:repo
npm run build
npm run ci:gate
```

Observed aggregate results:

- `Helper.Runtime.Tests`: `220/220`
- `Helper.Runtime.Api.Tests`: `171/171`
- `Helper.RuntimeSlice.Api.Tests`: `4/4`
- `Helper.Runtime.CompilePath.Tests`: `20/20`
- `OpenAPI` contract gate: `5/5`
- `SolutionCoverage`: `19` solution projects cover `19` repository projects

## Intentional Residuals

- broad nullability suppressions still exist in other `src/Helper.Api/Hosting` files outside the planner path changed here; they remain a follow-up cleanup candidate, but they are no longer blocking the audited correctness and security defects

## Outcome

The audited trust, correctness, and default-safety defects were remediated locally and verified with the repository gate entrypoints.
