# HELPER CI Gate Causal Remediation Plan

Date: `2026-04-11`

Scope: deterministic repair of the current local `ci:gate` contract, based on reproduced failures and direct lane-ownership evidence in the repository.

Source evidence:

- interactive causal analysis performed on `2026-04-11`
- existing audit documents:
  - `doc/analysis/HELPER_GITHUB_AUDIT_2026-04-11.md`
  - `doc/analysis/HELPER_GITHUB_AUDIT_REMEDIATION_PLAN_2026-04-11.md`

## Executive summary

The current `ci:gate` is red for structural reasons, not for one isolated broken test.

The confirmed causal chain is:

1. `scripts/ci_gate.ps1` invokes `Eval`, `Load/Chaos`, and `Tool benchmark` through scripts that target the wrong test projects.
2. The `Helper.Runtime.Tests` runtime lane explicitly excludes `EvalHarnessTests.cs`, `HumanLikeCommunicationEvalTests.cs`, `LoadChaosSmokeTests.cs`, `ToolCallBenchmarkTests.cs`, and `WebResearchParityEvalTests.cs`.
3. Those tests are actually linked into `Helper.Runtime.Certification.Tests` and, for the core eval harness, also into `Helper.Runtime.Eval.Tests`.
4. Because of that mismatch, the current scripts produce one of three invalid outcomes:
   - `No test matches` false-green behavior
   - `--no-build` failures against projects that were never built
   - parent/child PowerShell exit-code drift that can print `Passed` after a failing child step
5. The current `ci:gate` also mixes deterministic repo hygiene checks with heavy or operator-bound steps, which conflicts with the intended hosted `repo-gate` contract.

This plan fixes the problem in the correct order:

1. repair the lane ownership model
2. repair script orchestration and exit propagation
3. redefine `ci:gate` as a deterministic gate
4. move heavy lanes into explicit non-default entry points
5. rerun targeted stages and then the full gate

## Success criteria

The remediation is complete only when all of the following are true:

1. every `ci:gate` step points to the project that actually owns the tests it claims to run
2. `Load`, `ToolBenchmark`, `Eval`, `EvalV2`, and `EvalOffline` can be reproduced directly with their owning project paths
3. no wrapper script can print `Passed` after a failing child command
4. no lane step relies on `--no-build` unless its target project is guaranteed to be built earlier in the same contract
5. base `npm run ci:gate` is deterministic and green on the local repo without requiring parity evidence, UI runtime orchestration, or operator infrastructure
6. heavy lanes remain runnable through explicit scripts, with their status separated from the deterministic repo gate

## Root-cause map

### A. Wrong lane ownership in `ci:gate`

Confirmed evidence:

1. `scripts/ci_gate.ps1` runs:
   - `scripts/run_eval_gate.ps1`
   - `scripts/run_load_chaos_smoke.ps1`
   - `scripts/run_tool_benchmark.ps1`
2. `test/Helper.Runtime.Tests/Helper.Runtime.Tests.RuntimeLane.props` excludes:
   - `EvalHarnessTests.cs`
   - `HumanLikeCommunicationEvalTests.cs`
   - `LoadChaosSmokeTests.cs`
   - `ToolCallBenchmarkTests.cs`
   - `WebResearchParityEvalTests.cs`
3. `test/Helper.Runtime.Certification.Tests/Helper.Runtime.Certification.Tests.csproj` includes:
   - `EvalHarnessTests.cs`
   - `HumanLikeCommunicationEvalTests.cs`
   - `LoadChaosSmokeTests.cs`
   - `ToolCallBenchmarkTests.cs`
   - `WebResearchParityEvalTests.cs`
4. `test/Helper.Runtime.Eval.Tests/Helper.Runtime.Eval.Tests.csproj` includes:
   - `EvalHarnessTests.cs`

Meaning:

- the current scripts are pointed at the wrong assemblies
- the current failing behavior is not random; it is a direct consequence of the lane manifest configuration

### B. `Load/Chaos` parent-child orchestration is wrong

Confirmed evidence:

1. `scripts/run_load_chaos_smoke.ps1` launches a child PowerShell process.
2. `scripts/load_streaming_chaos.ps1` throws on non-zero test exit.
3. the parent script can still print `Passed` because it does not validate the child exit contract robustly enough.

Meaning:

- even a correct test target would still be reported unreliably until wrapper propagation is fixed

### C. `Eval` uses a project that is not built by the canonical solution path

Confirmed evidence:

1. `Helper.Runtime.Eval.Tests` is not listed in `Helper.sln`
2. `run_eval_gate.ps1` currently uses `--no-build`
3. direct `dotnet test test\Helper.Runtime.Eval.Tests\Helper.Runtime.Eval.Tests.csproj --no-build` fails if the project has not been built first

Meaning:

- `Eval` cannot be reliable until either:
  - the eval project is added to the solution and built in the standard path
  - or the eval script stops assuming a prior solution build

### D. The base gate still contains heavy or operator-bound steps

Conflict with intended contract:

1. existing remediation guidance already classifies parity gates, UI smoke/perf, release baseline capture, and load/chaos as heavy or environment-bound
2. current `scripts/ci_gate.ps1` still executes those steps inside the default gate

Meaning:

- even after lane-target fixes, the contract remains unstable unless the base gate is narrowed to deterministic repo checks

## Recommended target state

The repository should expose two explicit gate layers:

### Layer 1: deterministic repo gate

This becomes the meaning of `npm run ci:gate`.

Include only:

1. secret scan
2. remediation freeze
3. root layout
4. config governance
5. R&D governance
6. execution step closure
7. docs entrypoints
8. trailing-space directories
9. UI API consistency
10. frontend architecture
11. NuGet security gate
12. solution build coverage
13. canonical solution build
14. fast runtime tests
15. deterministic test categories that do not need operator infrastructure and target the correct owning project
16. OpenAPI gate
17. generated client diff
18. monitoring config
19. frontend build
20. bundle budget

### Layer 2: heavy and operator-bound gate

This must move to a separate explicit entry point, for example:

1. `npm run ci:gate:heavy`
2. `scripts/ci_gate_heavy.ps1`
3. or integration into existing certification/release scripts

Place here:

1. load/chaos smoke
2. generation parity gate
3. generation parity benchmark
4. generation parity window gate
5. control-plane thresholds
6. latency budget
7. UI workflow smoke
8. UI perf regression
9. release baseline capture

## Phased implementation plan

## Phase 0: Freeze evidence and protect cleanup

### Step 0.1

Keep the current evidence files for the broken state:

1. `doc/load_streaming_chaos_report.md`
2. current parity gate markdown/json artifacts
3. this plan document

### Step 0.2

Keep a safe cleanup entry point for orphaned local runs:

1. retain `scripts/cleanup_ci_gate_orphan_dotnet.ps1`
2. document that it only targets explicit PID lists and validated `dotnet` processes
3. use it only after a run has already been diagnosed as orphaned

Definition of done:

- cleanup tooling exists and does not require unsafe blanket process kills

## Phase 1: Repair lane ownership first

### Step 1.1

Create a single lane ownership matrix in code or docs.

Minimum mapping:

1. `Runtime lane`
   - owner: `test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj`
2. `API lane`
   - owner: `test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj`
3. `Browser lane`
   - owner: `test/Helper.Runtime.Browser.Tests/Helper.Runtime.Browser.Tests.csproj`
4. `Integration lane`
   - owner: `test/Helper.Runtime.Integration.Tests/Helper.Runtime.Integration.Tests.csproj`
5. `Certification lane`
   - owner: `test/Helper.Runtime.Certification.Tests/Helper.Runtime.Certification.Tests.csproj`
6. `Certification compile lane`
   - owner: `test/Helper.Runtime.Certification.Compile.Tests/Helper.Runtime.Certification.Compile.Tests.csproj`
7. `Eval lane`
   - owner: `test/Helper.Runtime.Eval.Tests/Helper.Runtime.Eval.Tests.csproj`

Files:

1. `doc/operator/HELPER_RUNTIME_TEST_LANES_2026-04-01.md`
2. optionally a new machine-readable manifest under `scripts/` or `test/`

Definition of done:

- there is one authoritative mapping from test category to owning project

### Step 1.2

Decide the final ownership of eval-related tests.

Recommended choice:

1. move all eval-only tests into `Helper.Runtime.Eval.Tests`
2. stop duplicating `EvalHarnessTests.cs` across `Eval` and `Certification`
3. keep `Certification` focused on certification and promotion evidence

Files likely involved:

1. `test/Helper.Runtime.Eval.Tests/Helper.Runtime.Eval.Tests.csproj`
2. `test/Helper.Runtime.Certification.Tests/Helper.Runtime.Certification.Tests.csproj`
3. possibly supporting shared files such as `EvalTestPackageFactory.cs`

Minimum eval lane contents after cleanup:

1. `EvalHarnessTests.cs`
2. `EvalRunnerV2Tests.cs`
3. `HumanLikeCommunicationEvalTests.cs`
4. `WebResearchParityEvalTests.cs`
5. any eval-only helper files they require

Definition of done:

- eval tests have one clear owning project
- certification does not carry eval-only closure by accident

### Step 1.3

Keep `LoadChaosSmokeTests.cs` and `ToolCallBenchmarkTests.cs` in one authoritative project.

Recommended immediate choice:

1. keep them in `Helper.Runtime.Certification.Tests`
2. do not try to make runtime lane own them if the runtime lane manifest intentionally excludes them

Optional later refinement:

1. split them into a dedicated benchmark/chaos project if they grow materially

Definition of done:

- `Load` and `ToolBenchmark` target one project only

## Phase 2: Repair script targets and exit propagation

### Step 2.1

Fix `scripts/run_load_chaos_smoke.ps1`.

Required changes:

1. stop using a child PowerShell invocation that can hide failure semantics
2. call the underlying command with strict exit handling
3. never print `Passed` unless the command actually returned success

Preferred implementation:

1. inline the logic from `load_streaming_chaos.ps1`
2. or call it with `&` and explicitly check `$LASTEXITCODE`

Definition of done:

- a failing load/chaos run cannot print `Passed`

### Step 2.2

Fix `scripts/load_streaming_chaos.ps1`.

Required changes:

1. stop targeting `Helper.sln`
2. target the real owner:
   - `test\Helper.Runtime.Certification.Tests\Helper.Runtime.Certification.Tests.csproj`
3. keep the report output
4. include the actual command line used
5. fail if zero tests match

Recommended command model:

```powershell
dotnet test test\Helper.Runtime.Certification.Tests\Helper.Runtime.Certification.Tests.csproj --no-build --filter "Category=Load" -v minimal
```

Definition of done:

- `Load` runs the five intended tests from the actual owning assembly

### Step 2.3

Fix `scripts/run_tool_benchmark.ps1`.

Required changes:

1. stop targeting `Helper.sln`
2. target `test\Helper.Runtime.Certification.Tests\Helper.Runtime.Certification.Tests.csproj`
3. fail if zero tests match
4. explicitly check `$LASTEXITCODE`

Definition of done:

- `ToolBenchmark` runs the intended test, not a solution-wide fuzzy filter

### Step 2.4

Fix `scripts/run_eval_gate.ps1`.

Recommended target model:

1. point `Category=Eval` at `test\Helper.Runtime.Eval.Tests\Helper.Runtime.Eval.Tests.csproj`
2. point `Category=EvalV2` at the same eval project after Step 1.2 consolidates ownership

If Step 1.2 is not completed first, use a temporary split:

1. `Category=Eval` on `Helper.Runtime.Eval.Tests`
2. `Category=EvalV2` on `Helper.Runtime.Certification.Tests`

Important:

1. do not rely on `--no-build` for `Helper.Runtime.Eval.Tests` until the project is either:
   - added to `Helper.sln`
   - or built inside the script before test execution

Definition of done:

- `Eval` and `EvalV2` both run against projects that actually contain those tests

### Step 2.5

Fix `scripts/run_eval_runner_v2.ps1`.

Required changes:

1. build the actual owning eval project
2. run the `EvalV2` category against that same project
3. keep the subsequent export actions
4. fail immediately if the preparation test phase returns zero matching tests

Definition of done:

- `run_eval_runner_v2.ps1` no longer depends on excluded tests living in `Helper.Runtime.Tests`

### Step 2.6

Fix `scripts/run_offline_eval.ps1`.

Required changes:

1. stop targeting `Helper.sln`
2. target the dedicated eval project
3. build it when required
4. explicitly fail on zero test matches

Definition of done:

- offline eval becomes reproducible through one owning test assembly

## Phase 3: Make zero-match test execution impossible to misreport

### Step 3.1

Introduce a shared helper for category-based test execution.

Suggested capability:

1. target project path
2. optional build-before-test behavior
3. category or fully-qualified-name filter
4. `--list-tests` preflight
5. hard fail if zero tests match
6. structured stdout/stderr logging
7. strict exit-code propagation

Candidates:

1. a new `scripts/common/Invoke-StrictDotnetCategoryTest.ps1`
2. or an extension of existing wrappers

Definition of done:

- no lane script implements ad-hoc `dotnet test ... --filter ...` logic differently

### Step 3.2

Refactor these scripts to use the shared helper:

1. `scripts/load_streaming_chaos.ps1`
2. `scripts/run_tool_benchmark.ps1`
3. `scripts/run_eval_gate.ps1`
4. `scripts/run_eval_runner_v2.ps1`
5. `scripts/run_offline_eval.ps1`

Definition of done:

- category-based scripts share one strict execution model

## Phase 4: Repair the solution/build contract for eval

### Step 4.1

Add `Helper.Runtime.Eval.Tests` to `Helper.sln` if the repository intends eval to be part of standard deterministic verification.

Recommended choice:

1. add the project to `Helper.sln`
2. add `Debug|Any CPU` and `Release|Any CPU` `Build.0` mappings
3. extend `check_solution_build_coverage.ps1` if needed

Reason:

- otherwise any script using `--no-build` against the eval project will always be contract-fragile after a normal solution build

Definition of done:

- `dotnet build Helper.sln` builds `Helper.Runtime.Eval.Tests` when eval is part of the deterministic repo gate

### Step 4.2

If the repository does not want eval inside the canonical solution, then make that explicit:

1. keep `Helper.Runtime.Eval.Tests` outside the solution
2. remove all `--no-build` assumptions for eval scripts
3. document that eval scripts self-build their owning project

Only one model should exist.

Definition of done:

- there is no hidden dependency on a project that the canonical solution never builds

## Phase 5: Narrow `ci:gate` to deterministic checks

### Step 5.1

Remove heavy and operator-bound steps from `scripts/ci_gate.ps1`.

Move out:

1. `Load/Chaos smoke`
2. `Generation parity gate`
3. `Generation parity benchmark`
4. `Generation parity window gate`
5. `Control-plane thresholds`
6. `Latency budget`
7. `UI workflow smoke`
8. `UI perf regression`
9. `Release baseline capture`

Keep in:

1. repo hygiene
2. build checks
3. deterministic tests
4. deterministic documentation and API checks
5. frontend build and budget checks

Definition of done:

- `npm run ci:gate` means deterministic repo validation only

### Step 5.2

Create a separate heavy gate entry point.

Recommended names:

1. `scripts/ci_gate_heavy.ps1`
2. package script `ci:gate:heavy`

Move the heavy steps there in the same order they currently appear.

Definition of done:

- operator-bound and parity-bound failures no longer make the base repo gate permanently red

### Step 5.3

Align local `ci:gate` with the hosted `.github/workflows/repo-gate.yml`.

Implementation:

1. compare local `scripts/ci_gate.ps1` against `.github/workflows/repo-gate.yml`
2. keep their deterministic step sets equivalent
3. keep heavy lanes out of hosted branch-protection validation unless intentionally added later

Definition of done:

- local and hosted deterministic gates mean the same thing

## Phase 6: Hardening against process leaks and build-lock regressions

### Step 6.1

Ensure every wrapper with child processes has explicit failure propagation.

Audit at minimum:

1. `scripts/run_load_chaos_smoke.ps1`
2. `scripts/run_eval_runner_v2.ps1`
3. any script that shells out to PowerShell from PowerShell

Definition of done:

- no parent script can mask a failing child command

### Step 6.2

Document or enforce the build isolation rule for local parallel runs.

Reason:

- the shared root in `Directory.Build.props` can produce file locks during concurrent builds in the same local environment

Options:

1. document that lane scripts must run serially in one workspace
2. or scope `HELPER_MSBUILD_INTERMEDIATE_ROOT` per lane when self-building

Definition of done:

- local ad-hoc parallel verification does not create misleading MSBuild lock failures

## Verification plan

Run in this order after implementation.

### Stage 1: direct lane verification

1. `dotnet test test\Helper.Runtime.Certification.Tests\Helper.Runtime.Certification.Tests.csproj --no-build --filter "Category=Load" -v minimal`
2. `dotnet test test\Helper.Runtime.Certification.Tests\Helper.Runtime.Certification.Tests.csproj --no-build --filter "Category=ToolBenchmark" -v minimal`
3. `dotnet test test\Helper.Runtime.Eval.Tests\Helper.Runtime.Eval.Tests.csproj --filter "Category=Eval" -v minimal`
4. `dotnet test <final eval owning project> --filter "Category=EvalV2" -v minimal`
5. `dotnet test <final eval owning project> --filter "Category=EvalOffline" -v minimal`

Expected result:

- each command runs real tests
- zero commands report `No test matches`

### Stage 2: script-level verification

1. `powershell -ExecutionPolicy Bypass -File scripts/run_load_chaos_smoke.ps1`
2. `powershell -ExecutionPolicy Bypass -File scripts/run_tool_benchmark.ps1`
3. `powershell -ExecutionPolicy Bypass -File scripts/run_eval_gate.ps1`
4. `powershell -ExecutionPolicy Bypass -File scripts/run_eval_runner_v2.ps1`
5. `powershell -ExecutionPolicy Bypass -File scripts/run_offline_eval.ps1`

Expected result:

- correct exit codes
- no false `Passed` banner after a real failure
- no orphaned `dotnet` left behind

### Stage 3: contract verification

1. `dotnet build Helper.sln -m:1`
2. `powershell -ExecutionPolicy Bypass -File scripts/check_solution_build_coverage.ps1`
3. `npm run ci:gate`

Expected result:

- the base gate is green and deterministic

### Stage 4: heavy lane verification

1. `npm run ci:gate:heavy`
2. verify each heavy failure is now isolated to the heavy gate, not the deterministic base gate

Expected result:

- heavy/parity state is visible, but it no longer corrupts the meaning of base repo health

## Definition of done

This remediation is complete only when:

1. every lane script points to the assembly that actually contains its tests
2. `Helper.Runtime.Tests.RuntimeLane.props` exclusions no longer conflict with `ci:gate` script targets
3. `Helper.Runtime.Eval.Tests` ownership is explicit and operationally consistent
4. base `ci:gate` excludes parity and other operator-bound heavy steps
5. `npm run ci:gate` passes end-to-end
6. no step in the deterministic gate can produce `No test matches` and still appear successful
7. no child PowerShell or `dotnet` process is left orphaned after a failed deterministic gate step

## Recommended execution order

Implement in this exact order:

1. fix lane ownership decisions
2. retarget `load`, `tool benchmark`, and `eval` scripts
3. add strict zero-match and exit-propagation helper
4. decide whether `Helper.Runtime.Eval.Tests` belongs in `Helper.sln`
5. narrow base `ci:gate`
6. create `ci:gate:heavy`
7. rerun targeted scripts
8. rerun full `npm run ci:gate`

If Step 4 is skipped, the rest of the plan stays structurally fragile.
