# HELPER Certification Compile Hang Remediation Plan

Date: 2026-04-12
Source audit: `doc/analysis/HELPER_CERTIFICATION_COMPILE_HANG_CAUSAL_AUDIT_2026-04-12.md`

## Objective

Remove the false-positive "hang" diagnosis for certification compile runs, harden teardown when a real stall happens, and reduce the runtime cost that currently makes `PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke` vulnerable to blame-timeout kills.

## Root-Cause Order

Work must follow the actual cause chain from the audit:

1. The current `60s` blame timeout is below the real runtime of the console promotion path.
2. The target test performs repeated nested `dotnet build` work by design.
3. When the blame collector kills the inner host/build tree, the outer `dotnet test` root can remain as idle residue.
4. `DotnetService.BuildAsync` still has a weak target-selection contract, which is a separate correctness risk.

The plan therefore starts with observability and teardown, then fixes target selection, then removes unnecessary repeated build work.

## Phase 0. Freeze Evidence And Stop Reproducing False Signals

### Step 0.1. Preserve current diagnostic artifacts

Keep the existing evidence set unchanged:

- `temp/verification/hang_trace_promotion_class/certification_process_trace.jsonl`
- `test/Helper.Runtime.Certification.Compile.Tests/TestResults/324308ba-b453-49ed-b4d7-cd64b1656241/*`
- `C:\Users\rovsh\.codex\memories\msbuild-intermediate\repo\bin\test\Helper.Runtime.Certification.Compile.Tests\Debug\runs\20260411T170901530Z_a77003f1\certification_process_trace.jsonl`

Reason:

- these files prove that the blame run was terminated during the final active build, while the no-blame run completed normally.

### Step 0.2. Stop using `--blame-hang-timeout 60s` as baseline reproduction for this lane

Change the certification compile operational contract so that:

- baseline local verification uses the wrapper script, not raw `dotnet test --blame-hang`
- blame collection is a separate diagnostic mode with a materially larger timeout

Files to update:

- `scripts/run_certification_compile_tests.ps1`
- `scripts/check_certification_compile_lock_wait.ps1`
- any workflow or operator script that currently relies on `60s` blame timeout for this lane

Target behavior:

- default lane run: no blame collector
- diagnostic lane run: blame collector allowed, but only with a timeout above measured steady-state runtime

Recommended diagnostic floor:

- start with `180s`
- never below `120s` for the console promotion class

## Phase 1. Make The Wrapper Deterministic Under Inner-Host Failure

### Step 1.1. Keep the certification wrapper as the only supported entrypoint for this lane

Treat `scripts/run_certification_compile_tests.ps1` as the canonical launcher for:

- CI
- local operator runs
- certification compile investigations

Reason:

- the wrapper is where wall-time, idle-time, trace-path and process-tree cleanup are enforced.

### Step 1.2. Extend teardown semantics from "best effort" to "contract"

The wrapper must guarantee that after failure, timeout, or external abortion:

- no root `dotnet test` residue remains alive
- no child `testhost` residue remains alive
- diagnostics are preserved under the run root

Files:

- `scripts/run_certification_compile_tests.ps1`
- `scripts/run_compile_path_tests.ps1`

Implementation steps:

1. Keep `taskkill /PID <root> /T /F` as the primary tree kill path.
2. After kill, explicitly poll for root-process disappearance.
3. If the root still exists after the poll window, fail the wrapper with a teardown-specific error.
4. Record teardown outcome in the preserved run root.

Expected artifacts on failure:

- trace file
- preserved `TestResults`
- wrapper-owned teardown summary

### Step 1.3. Detect idle residue using root process plus filesystem signals

The wrapper should continue using all three activity channels:

- trace-file writes
- `TestResults` writes
- process CPU delta

Add one more explicit diagnostic output:

- last-seen child-process snapshot before teardown

Files:

- `scripts/run_certification_compile_tests.ps1`

Reason:

- if the root remains alive but the child tree is gone, the wrapper must distinguish "dead inner workload" from "slow useful work".

### Step 1.4. Mirror the same contract into compile-path sibling scripts

The same residue pattern is possible anywhere a wrapper launches `dotnet test` and waits on the root process.

Files:

- `scripts/run_compile_path_tests.ps1`
- any other sibling script that uses the same launcher pattern

Definition:

- identical teardown semantics
- identical failure-artifact preservation
- identical idle detection contract

## Phase 2. Fix `DotnetService` Target Resolution

### Step 2.1. Stop recursive first-match target discovery

Current behavior in `DotnetService.BuildAsync` is weak because it recursively grabs the first `*.sln` or `*.csproj`.

Files:

- `src/Helper.Runtime/DotnetService.cs`

Replace this contract with deterministic target resolution:

1. Ignore paths under `bin`, `obj`, `.compile_gate`, `.git`, `.vs`, `node_modules`.
2. Prefer a project or solution file located directly in `workingDirectory`.
3. If multiple top-level targets exist, return a structured ambiguity error instead of guessing.
4. Only recurse if the caller explicitly allows recursive discovery.

### Step 2.2. Introduce explicit-target overloads

Add a deterministic API surface so call sites can pass the intended build target directly.

Suggested shape:

- `BuildAsync(string workingDirectory, string targetPath, CancellationToken ct = default)`
- optionally `RestoreAsync(..., targetPath, ...)`
- optionally `TestAsync(..., targetPath, ...)` if needed

Files:

- `src/Helper.Runtime/Core/Contracts/OperationsContracts.cs`
- `src/Helper.Runtime/DotnetService.cs`
- call sites that currently depend on implicit discovery

### Step 2.3. Make compile-gate callers pass explicit targets

`GenerationCompileGate` already knows its workspace contract. It should not rely on recursive target guessing.

Files:

- `src/Helper.Runtime/Generation/GenerationCompileGate.cs`
- `src/Helper.Runtime/Generation/CompileGateWorkspacePreparer.cs`

Implementation direction:

1. `CompileGateWorkspacePreparer` returns the generated compile project path.
2. `GenerationCompileGate` passes that path explicitly into `DotnetService`.

### Step 2.4. Make real build validation pass the intended project explicitly

The real build phase in certification should target the actual template project, not "first project found somewhere under the root".

Files:

- `src/Helper.Runtime/LocalBuildExecutor.cs`
- any helper that detects the main project under a template root

Implementation direction:

1. detect the canonical project file once
2. pass it explicitly into `DotnetService.BuildAsync`
3. fail fast if the template root has zero or multiple ambiguous primary projects

### Step 2.5. Add tests for target selection

Add regression tests covering:

- root-level single project
- nested `.compile_gate` plus root project
- stale `bin/obj` project artifacts
- ambiguous multi-project template root

Files:

- `test/Helper.Runtime.Tests/DotnetServiceTraceBehaviorTests.cs`
- a new dedicated target-selection test file if needed
- `test/Helper.Runtime.Tests/ArchitectureFitnessTests.RuntimeLanes.cs`

## Phase 3. Remove Unnecessary Repeated Certification Cost

### Step 3.1. Split full certification from post-activation verification

The audit shows the current flow performs heavy certification twice:

1. candidate certification
2. active-version certification after activation

Files:

- `src/Helper.Runtime/Generation/GenerationTemplatePromotionService.cs`
- `src/Helper.Runtime/Generation/TemplateCertificationService.cs`

Refactor the contract into two separate responsibilities:

1. `full certification`
   - compile gate
   - real build validation
   - artifact validation
   - smoke
   - safety
   - placeholder scan
2. `post-activation verification`
   - verify active pointer changed correctly
   - verify published root exists
   - verify certification status/report was published with the promoted version
   - optionally verify published tree matches the certified candidate tree

### Step 3.2. Add candidate-to-published integrity verification instead of rebuilding

If the system still needs confidence after `Directory.Move`, do not rebuild the same template again by default.

Recommended replacement:

1. generate a file-manifest hash set for the candidate after successful certification
2. after publish/activation, verify the published tree matches the certified manifest
3. only rebuild again if an explicit stricter mode is enabled

Files:

- `src/Helper.Runtime/Generation/TemplateCertificationService*.cs`
- `src/Helper.Runtime/Generation/GenerationTemplatePromotionService.cs`
- possibly a new helper such as `TemplateTreeIntegrityVerifier`

### Step 3.3. Gate the expensive double-check behind an explicit feature flag

If a second full certification is still desired for some scenarios, it should not be the default lane behavior.

Suggested env flag:

- `HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY`

Default:

- `false`

Behavior:

- `false`: run lightweight post-activation verification only
- `true`: run full re-certification after activation

### Step 3.4. Update the test to assert the new contract directly

The console promotion test should validate:

- promotion succeeded
- certification status exists
- active version points to the promoted version
- no duplicate full build cycle was required in the default path

Files:

- `test/Helper.Runtime.Tests/TemplatePromotionEndToEndAndChaosTests.cs`

## Phase 4. Tune Diagnostic And CI Contracts

### Step 4.1. Separate "operational lane" from "forensic lane"

Operational lane:

- runs fast enough for CI
- no aggressive blame collector
- uses wrapper timeout/idle protection

Forensic lane:

- opt-in only
- uses blame collector
- uses a larger timeout
- preserves artifacts by design

Files:

- `scripts/run_certification_compile_tests.ps1`
- `.github/workflows/runtime-test-lanes.yml`
- any related lane orchestration script

### Step 4.2. Set lane-level budgets from measured runtime, not guesswork

Current measured console promotion build segment is about `70s`.

Set:

- operational wrapper idle timeout comfortably above real heartbeat gaps
- diagnostic blame timeout above full measured class runtime

Initial recommendation:

- `HELPER_CERTIFICATION_COMPILE_IDLE_TIMEOUT_SEC=180`
- `--blame-hang-timeout 180s` for forensic reruns

Do not hardcode final values until after the refactor in Phase 3, because runtime should fall once duplicate certification work is removed.

### Step 4.3. Add documentation for the new lane contract

Document:

- when to use the wrapper
- when to use blame mode
- where preserved diagnostics land
- how to interpret idle residue vs useful work

Files:

- `doc/operator/HELPER_RUNTIME_TEST_LANES_2026-04-01.md`
- related certification compile runbooks

## Phase 5. Verification Matrix

### Step 5.1. Prove target resolution is deterministic

Run focused tests for `DotnetService` target selection and compile-gate ownership.

Required result:

- no recursive first-match ambiguity remains

### Step 5.2. Prove the wrapper kills residue on forced failure

Use a controlled failing or intentionally stalled scenario and verify:

- wrapper exits non-zero
- process tree is gone after cleanup
- diagnostics are preserved under the run root

Required result:

- no idle root `dotnet` remains after wrapper exit

### Step 5.3. Prove the console promotion path passes without false hang diagnosis

Run the console promotion test through the wrapper in its operational mode.

Required result:

- test completes
- no blame-triggered kill
- no residue

### Step 5.4. Re-run the forensic mode with the larger timeout

Run the same class under blame mode with the increased timeout.

Required result:

- if the test completes, the earlier "hang" is formally downgraded to a false-positive timeout artifact
- if it still fails, the preserved artifacts must point to a new narrower blocking site

## Concrete File Backlog

### Mandatory code changes

- `src/Helper.Runtime/DotnetService.cs`
- `src/Helper.Runtime/Core/Contracts/OperationsContracts.cs`
- `src/Helper.Runtime/Generation/GenerationCompileGate.cs`
- `src/Helper.Runtime/Generation/CompileGateWorkspacePreparer.cs`
- `src/Helper.Runtime/LocalBuildExecutor.cs`
- `src/Helper.Runtime/Generation/GenerationTemplatePromotionService.cs`
- `src/Helper.Runtime/Generation/TemplateCertificationService.cs`
- `scripts/run_certification_compile_tests.ps1`
- `scripts/run_compile_path_tests.ps1`

### Mandatory test/doc changes

- `test/Helper.Runtime.Tests/DotnetServiceTraceBehaviorTests.cs`
- `test/Helper.Runtime.Tests/TemplatePromotionEndToEndAndChaosTests.cs`
- `test/Helper.Runtime.Tests/ArchitectureFitnessTests.RuntimeLanes.cs`
- `doc/operator/HELPER_RUNTIME_TEST_LANES_2026-04-01.md`

## Definition Of Done

The remediation is complete only when all of the following are true:

1. `DotnetService` no longer guesses build targets via unrestricted recursive first-match.
2. Compile gate and real build validation pass explicit intended targets.
3. Default promotion flow does not perform duplicate heavy full certification after activation.
4. `scripts/run_certification_compile_tests.ps1` leaves no idle `dotnet test` residue after timeout or failure.
5. The console promotion test passes in the operational lane without a false hang diagnosis.
6. Forensic blame mode uses a larger timeout and produces interpretable artifacts.
7. The updated tests and architecture guards lock the new contract in place.

## Recommended Execution Order

1. Phase 1: wrapper teardown and residue contract
2. Phase 2: deterministic `DotnetService` target selection
3. Phase 3: remove duplicate full certification after activation
4. Phase 4: adjust blame/CI/runbook contracts
5. Phase 5: verification and closure

