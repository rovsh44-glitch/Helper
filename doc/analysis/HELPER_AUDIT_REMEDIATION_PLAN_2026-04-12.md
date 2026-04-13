# HELPER Audit Remediation Plan

Date: `2026-04-12`
Source audit: `doc/analysis/HELPER_AUDIT_2026-04-12.md`

## Objective

Bring `Helper` to a state where:

1. quality gates fail on real defects instead of producing false green
2. the canonical build perimeter is explicit and enforceable
3. `/api/architecture/plan` returns truthful results without silent fallback
4. production DI does not point at prototype or stub implementations by default
5. command-execution paths are either hardened to a narrow contract or removed from the production surface
6. structural debt is reduced enough that future regressions become easier to detect and cheaper to fix

## Causal Order

The audit findings should be fixed in this order:

1. restore trust in CI and local gates
2. close the solution-perimeter gap
3. make the planning API behavior truthful
4. separate production composition from prototype scaffolding
5. harden or retire shell execution
6. then spend effort on maintainability cleanup

Reason:

- until the gates are trustworthy, every later green signal is suspect
- until solution coverage is explicit, builds can silently miss real code
- until planner failures are surfaced honestly, API consumers receive misleading data
- until prototypes are isolated, operational behavior remains semantically unstable

## Definition Of Done

Remediation is complete only when all of the following are true:

1. `scripts/openapi_gate.ps1` exits non-zero when no contract tests match the filter
2. `scripts/check_solution_build_coverage.ps1` compares the real repository `*.csproj` set against `Helper.sln` and an explicit exclusion policy
3. every currently missing project is either added to `Helper.sln` or formally listed in a tracked allowlist with justification
4. `/api/architecture/plan` no longer fabricates a WPF result on planner parse failure
5. planner failures produce an explicit API-level failure or degraded-status contract
6. production service registration no longer wires ambiguous prototype implementations by default
7. `shell_execute` is either removed from the default tool registry or protected by a strict non-interpreter allowlist plus tests
8. at least one maintainability pass reduces the largest known risk hotspots: oversized files, broad nullability suppression, or historical dead directories

## Phase 0. Freeze The Baseline

### Step 0.1. Keep the audit as the immutable before-state

Preserve:

- `doc/analysis/HELPER_AUDIT_2026-04-12.md`

Reason:

- this file is the reference evidence set for all remediation decisions

### Step 0.2. Record the exact remediation branch baseline

Actions:

1. create a dedicated remediation branch from the current audited state
2. capture `git rev-parse HEAD` into the first remediation commit message or PR description
3. state explicitly in every follow-up note whether evidence refers to `local branch`, `review branch`, or `main`

Acceptance criteria:

- remediation work is not mixed into unrelated local changes
- reviewers can map every fix back to the audited baseline

## Phase 1. Restore Trust In Gates

### Step 1.1. Fix the false-green OpenAPI gate

Problem:

- `scripts/openapi_gate.ps1` currently treats the run as passed when `dotnet test` returns exit code `0`, even if the filter matched no tests
- the audit already confirmed a real false-green path through `npm run ci:gate`

Files in scope:

- `scripts/openapi_gate.ps1`
- `scripts/common/StrictDotnetFilteredTest.ps1`
- any script or `package.json` task that calls the OpenAPI gate

Implementation steps:

1. replace the raw `dotnet test` handling in `scripts/openapi_gate.ps1` with the stricter helper path already used elsewhere
2. treat these conditions as hard failure:
   - non-zero process exit code
   - `No test matches the given testcase filter`
   - empty test discovery when a filtered lane is expected to exist
3. print a failure reason that names the filter and the tested project
4. ensure the script returns a non-zero exit code on every hard failure path
5. keep the current success path small and deterministic: passed only when tests actually executed and passed
6. add a regression check for the no-match case

Acceptance criteria:

1. a no-match filtered run fails locally
2. a valid filtered contract-test run passes
3. `npm run ci:gate` can no longer report green when the OpenAPI lane silently ran zero tests

Recommended verification:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\openapi_gate.ps1
npm run ci:gate
```

### Step 1.2. Make solution coverage compare against the repository, not only the solution file

Problem:

- `scripts/check_solution_build_coverage.ps1` only inspects projects that already appear inside `Helper.sln`
- the audit found `19` `*.csproj` in the repo but only `16` inside `Helper.sln`

Missing projects found by the audit:

- `test\Helper.Runtime.CompilePath.Tests\Helper.Runtime.CompilePath.Tests.csproj`
- `src\Helper.Runtime.WebResearch.Browser\Helper.Runtime.WebResearch.Browser.csproj`
- `src\Helper.RuntimeLogSemantics\Helper.RuntimeLogSemantics.csproj`

Files in scope:

- `scripts/check_solution_build_coverage.ps1`
- `Helper.sln`
- optional tracked allowlist manifest such as `scripts/config/solution-project-exclusions.json`

Implementation steps:

1. enumerate all repository `*.csproj` under `src` and `test`
2. enumerate all `*.csproj` referenced by `Helper.sln`
3. compute the exact set difference: `repo projects - solution projects`
4. for each missing project, decide one of two states:
   - `must be in solution`
   - `intentionally excluded`
5. if a project must build with the canonical repo, add it to `Helper.sln`
6. if a project is intentionally excluded, add it to a tracked allowlist with a short reason
7. update the script so any uncovered project outside the allowlist fails the check
8. print uncovered projects in a stable, reviewer-friendly format
9. document the policy in a short repo note so future projects do not drift out of the solution unnoticed

Acceptance criteria:

1. the three audited omissions are no longer invisible
2. the script fails if a new `*.csproj` appears without explicit classification
3. `Helper.sln` and repository policy describe the same build perimeter

Recommended verification:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check_solution_build_coverage.ps1
dotnet build .\Helper.sln
dotnet test .\test\Helper.Runtime.CompilePath.Tests\Helper.Runtime.CompilePath.Tests.csproj --no-restore
dotnet build .\src\Helper.Runtime.WebResearch.Browser\Helper.Runtime.WebResearch.Browser.csproj
dotnet build .\src\Helper.RuntimeLogSemantics\Helper.RuntimeLogSemantics.csproj
```

### Step 1.3. Re-run the entire gate suite only after Steps 1.1 and 1.2 are closed

Actions:

1. run the repo’s documented local checks
2. compare the new results against the audited baseline
3. do not start runtime-behavior refactors until the gate layer is trustworthy

Recommended verification:

```powershell
npm run frontend:check
npm run docs:check
npm run config:check
npm run build
npm run security:scan:repo
npm run ci:gate
```

## Phase 2. Make `/api/architecture/plan` Truthful

### Step 2.1. Define the correct failure contract before changing code

Problem:

- `SimplePlanner` currently catches planner parse failures and silently substitutes a WPF fallback
- callers cannot distinguish `valid planning result` from `planner failure disguised as success`

Files in scope:

- `src/Helper.Runtime/SimplePlanner.cs`
- `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Strategy.cs`
- planning DTOs, result types, and tests around this flow

Implementation steps:

1. choose the intended contract for planner failure:
   - explicit failure response
   - or explicit degraded response with `mode=fallback`
2. document that contract in code comments near the response type or endpoint mapping
3. keep the contract narrow: no silent substitution, no hidden mode switch

Decision rule:

- if downstream consumers need a response body for partial UX continuity, use `degraded` and make the fallback explicit
- if correctness matters more than continuity, return failure and let the caller decide next action

Acceptance criteria:

1. the API contract makes degraded or failed planning observable
2. consumers can tell the difference between a real plan and fallback behavior

### Step 2.2. Remove the silent fallback from `SimplePlanner`

Implementation steps:

1. remove the catch-all path that unconditionally returns:
   - `MainWindow.xaml`
   - `MainWindow.xaml.cs`
2. replace it with one of:
   - typed failure result
   - exception mapped by the API layer
   - explicit degraded result containing diagnostics
3. preserve error context sufficient for logs and tests
4. avoid leaking raw internals into public API payloads if the endpoint is externally consumed

Acceptance criteria:

1. parse failure no longer produces a false success payload
2. logs contain enough detail to diagnose the failing branch

### Step 2.3. Update endpoint mapping and tests

Implementation steps:

1. update `/api/architecture/plan` endpoint handling to map planner outcomes correctly
2. add tests for:
   - valid plan generation
   - parser failure
   - empty or malformed planner input
   - degraded mode, if that contract is chosen
3. add at least one integration-style API test so the endpoint contract cannot regress silently

Suggested test targets:

- `test\Helper.Runtime.Tests`
- `test\Helper.Runtime.Api.Tests`

Acceptance criteria:

1. the endpoint status code and payload now reflect real planner state
2. the regression suite fails if fallback behavior returns silently again

Recommended verification:

```powershell
dotnet test .\test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj --no-restore
dotnet test .\test\Helper.Runtime.Api.Tests\Helper.Runtime.Api.Tests.csproj --no-restore
```

## Phase 3. Separate Production Composition From Prototype Scaffolding

### Step 3.1. Inventory every runtime service currently wired into production DI

Problem:

- the audit found prototype-style implementations still registered through production service wiring
- this creates semantic drift between class names, runtime behavior, and operator expectations

Files in scope:

- `src/Helper.Api/Hosting/ServiceRegistrationExtensions.Core.cs`
- `src/Helper.Runtime/SupportingImplementations.cs`
- `src/Helper.Runtime/SimpleCoder.cs`
- any related planner/test-generator/sandbox registrations

Implementation steps:

1. enumerate every service registration in `ServiceRegistrationExtensions.Core`
2. classify each implementation as:
   - `production-ready`
   - `prototype`
   - `stub/mock`
   - `legacy transitional`
3. rename ambiguous classes if the current names imply production quality they do not provide
4. capture the classification in a short architecture note

Acceptance criteria:

1. the team has a single written classification of what is safe to run in production
2. no registration remains semantically ambiguous

### Step 3.2. Split DI registration into explicit service profiles

Implementation steps:

1. create separate registration paths, for example:
   - `AddRuntimeProductionServices`
   - `AddRuntimePrototypeServices`
2. move `PythonSandbox` and other stubs out of the default production registration path unless there is a strong operational reason to keep them
3. if prototype services must remain reachable, gate them behind explicit configuration flags and clear environment names
4. make the default boot path conservative: production profile only
5. update startup tests so a production boot no longer picks prototype services by accident

Acceptance criteria:

1. default app startup does not depend on stub or fake-success implementations
2. prototype services require an explicit opt-in

### Step 3.3. Remove fake-success behavior from operationally named components

Problem:

- `PythonSandbox` currently returns success without actually executing the intended work
- `SimpleCoder` embeds narrow WPF-specific assumptions into a general-looking runtime service

Implementation steps:

1. for each operationally named component, choose one of:
   - implement the real behavior
   - rename it to reflect prototype status
   - remove it from the production path
2. reject “pretend success” in runtime services that can influence orchestration decisions
3. where real implementation is not ready, return an explicit `not supported` or `disabled` result instead of success
4. add tests that assert these components cannot report successful work without real execution

Acceptance criteria:

1. orchestrators can no longer mistake a stub for a successful execution path
2. naming and behavior now match

Recommended verification:

```powershell
dotnet test .\test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj --no-restore
dotnet test .\test\Helper.Runtime.Api.Tests\Helper.Runtime.Api.Tests.csproj --no-restore
```

## Phase 4. Harden Or Retire `shell_execute`

### Step 4.1. Make a product decision first: remove or keep

Problem:

- `shell_execute` is disabled by default today, but if enabled it exposes a risky path guarded only by a broad process check
- interpreter-style commands such as `pwsh`, `powershell`, `python`, `node`, and `cmd` are too powerful to treat as a safe operational surface

Files in scope:

- `src/Helper.Runtime/Infrastructure/BuiltinToolRegistry.cs`
- `src/Helper.Runtime/ToolService.cs`
- `src/Helper.Runtime/Infrastructure/ToolExecutionGateway.cs`
- `src/Helper.Runtime/ToolPermitService.cs`
- `src/Helper.Runtime/ProcessGuard.cs`

Decision rule:

1. if no documented product scenario truly needs generic shell access, remove `shell_execute` from the built-in production registry
2. if shell access is required, replace generic shell execution with narrowly scoped tool-specific commands

Preferred direction:

- retire generic shell execution from the production path

### Step 4.2. If kept, replace the current guard with a strict command model

Implementation steps:

1. stop allowlisting interpreters by base executable name
2. define a fixed allowlist of concrete commands or task profiles
3. validate full argument payloads, not just the first token
4. block shells, interpreters, redirection patterns, and command concatenation by default
5. require explicit audit logging for every approved command execution
6. require environment flag plus policy registration, not environment flag alone

Acceptance criteria:

1. enabling shell tools does not implicitly enable arbitrary scripting
2. interpreter escape paths are rejected by tests

### Step 4.3. Add negative security tests

Implementation steps:

1. add tests that verify rejection of:
   - `pwsh`
   - `powershell`
   - `cmd`
   - `python`
   - `node`
   - argument payloads containing chaining or redirection markers
2. add tests that verify a narrow approved command still works if such a command remains supported

Acceptance criteria:

1. security regressions in command validation become test-detectable

Recommended verification:

```powershell
dotnet test .\test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj --no-restore
npm run security:scan:repo
```

## Phase 5. Structural Cleanup After The Operational Defects

### Step 5.1. Reduce oversized hotspot files

Problem:

- the audit identified several very large source and test files
- these files increase review cost and hide behavioral coupling

Priority candidates:

- `test/Helper.Runtime.Tests/ConversationRuntimeTests.cs`
- `test/Helper.Runtime.Tests/RetrievalPipelineTests.cs`
- `src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs`
- `services/generatedApiClient.ts`
- `hooks/useSettingsViewState.ts`

Implementation steps:

1. split by responsibility, not arbitrary line count
2. extract helper builders, fixtures, or submodules only when names remain clear
3. preserve behavior with characterization tests before major moves

Acceptance criteria:

1. hotspot files are smaller and easier to review
2. test behavior remains unchanged after extraction

### Step 5.2. Reduce broad nullability suppression

Problem:

- the audit found structural use of broad warning suppression across endpoint registration files

Implementation steps:

1. inventory every `#pragma` or broad suppression in `src/Helper.Api/Hosting`
2. classify each case:
   - real false positive
   - missing annotation
   - unsafe dereference risk
3. remove suppressions where correct annotations or guards can replace them
4. leave only narrowly justified suppressions with a short reason comment

Acceptance criteria:

1. nullability warnings are actionable again
2. suppressions are specific rather than blanket

### Step 5.3. Remove or document historical dead directories

Problem:

- the audit found apparently empty `src/SelfEvolvingAI.Infrastructure*` directories

Implementation steps:

1. verify they are truly unused and not build-generated placeholders
2. either remove them or add a short README explaining why they exist
3. ensure the repo root and solution structure no longer imply dead subsystems

Acceptance criteria:

1. the repository tree better reflects the real active architecture

## Validation Matrix

Run this matrix only after each preceding phase is closed enough to produce meaningful output.

### Core gates

```powershell
npm run frontend:check
npm run docs:check
npm run config:check
npm run build
npm run security:scan:repo
npm run ci:gate
```

### Targeted .NET validation

```powershell
dotnet build .\Helper.sln
dotnet test .\test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj --no-restore
dotnet test .\test\Helper.Runtime.Api.Tests\Helper.Runtime.Api.Tests.csproj --no-restore
dotnet test .\test\Helper.RuntimeSlice.Api.Tests\Helper.RuntimeSlice.Api.Tests.csproj --no-restore
dotnet test .\test\Helper.Runtime.CompilePath.Tests\Helper.Runtime.CompilePath.Tests.csproj --no-restore
```

### Script-level validation

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\openapi_gate.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\check_solution_build_coverage.ps1
```

## Suggested Execution Sequence

Use this exact order unless a newly discovered blocker forces reordering:

1. Phase 0 baseline freeze
2. Phase 1.1 OpenAPI gate
3. Phase 1.2 solution coverage
4. Phase 1.3 full gate rerun
5. Phase 2 planner truthfulness
6. Phase 3 DI and prototype isolation
7. Phase 4 shell hardening or retirement
8. Phase 5 structural cleanup

## Suggested Deliverables

The remediation work should end with these concrete outputs:

1. merged code changes for Phases 1 through 4
2. updated or added tests covering the regression paths found by the audit
3. a short architecture note describing:
   - solution-membership policy
   - production vs prototype service profiles
   - command-execution policy
4. a closure document such as `doc/analysis/HELPER_AUDIT_REMEDIATION_CLOSURE_2026-04-12.md` capturing:
   - what was fixed
   - what remains intentionally deferred
   - exact verification evidence

## Immediate Next Actions

If only the highest-leverage fixes are taken first, start here:

1. repair `scripts/openapi_gate.ps1`
2. repair `scripts/check_solution_build_coverage.ps1` and classify the 3 missing projects
3. remove silent fallback from `SimplePlanner`
4. add regression tests around planner failure and shell blocking behavior

This sequence closes the main trust, correctness, and security gaps identified by the audit before spending time on secondary cleanup.
