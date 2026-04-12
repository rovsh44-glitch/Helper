# HELPER Certification Compile Hang Causal Audit

Date: 2026-04-12
Scope: `Helper.Runtime.Certification.Compile.Tests` hang diagnosis for `TemplatePromotionEndToEndAndChaosTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke`

## Executive Verdict

The observed "hang" is not a single deadlock inside `TemplateCertificationService` or `TemplateLifecycleService`.

The dominant causal chain is:

1. The target test executes a duplicated promotion and certification pipeline with multiple nested `dotnet build` invocations.
2. That path is legitimately long enough to exceed the `--blame-hang --blame-hang-timeout 60s` budget.
3. The blame collector kills the inner `testhost` and nested `dotnet build` while the final post-activation build is still running.
4. After the inner processes are killed, the outer `dotnet test` root can remain alive as an idle residue with no CPU and no file activity.
5. That idle residue is then misread as "the wrapper is hanging", although the useful workload has already been aborted upstream.

This means the primary failure is a timeout-contract mismatch plus incomplete teardown semantics after external interruption, not a proven intrinsic deadlock in the final `ConsoleTool.csproj` build.

## Highest-Confidence Findings

### 1. The 60-second blame timeout is lower than the real runtime of the target test

Evidence:

- The blame run sequence file marks the console smoke test as the only unfinished test:
  - `test/Helper.Runtime.Certification.Compile.Tests/TestResults/324308ba-b453-49ed-b4d7-cd64b1656241/Sequence_65a5afe291e54354be51e3f272b8c47d.xml`
- The blame trace shows the final post-activation build was killed with `exitCode=-1`, not completed normally:
  - `temp/verification/hang_trace_promotion_class/certification_process_trace.jsonl`
- A normal certification-compile lane run completed the same console promotion path without blame:
  - `C:\Users\rovsh\.codex\memories\msbuild-intermediate\repo\bin\test\Helper.Runtime.Certification.Compile.Tests\Debug\runs\20260411T170901530Z_a77003f1\certification_process_trace.jsonl`

Measured timings from the successful no-blame trace for the console promotion segment:

- candidate compile gate: `21.895s`
- candidate real build: `21.924s`
- active compile gate: `21.972s`
- active real build: `2.011s`
- total nested build time for the console promotion segment: `69.839s`

Measured timings from the blame trace:

- candidate compile gate: `21.964s`
- candidate real build: `2.082s`
- active compile gate: `2.037s`
- active real build: started, then killed at `12.201s`

Conclusion:

- A `60s` hang budget is not enough for this test class under cold or semi-cold build conditions.
- The blame collector is terminating a still-progressing test path before it naturally completes.

### 2. The test path duplicates expensive certification work by design

The problematic test constructs a real promotion pipeline with real certification dependencies:

- `test/Helper.Runtime.Tests/TemplatePromotionEndToEndAndChaosTests.cs:11`
- `test/Helper.Runtime.Tests/TemplatePromotionEndToEndAndChaosTests.cs:33`
- `test/Helper.Runtime.Tests/TemplatePromotionEndToEndAndChaosTests.cs:35`
- `test/Helper.Runtime.Tests/TemplatePromotionEndToEndAndChaosTests.cs:46`

The promotion service runs:

1. initial compile gate before certification
2. certification on the candidate
3. activation
4. certification again on the active version

Relevant code:

- `src/Helper.Runtime/Generation/GenerationTemplatePromotionService.cs:148`
- `src/Helper.Runtime/Generation/GenerationTemplatePromotionService.cs:160`
- `src/Helper.Runtime/Generation/GenerationTemplatePromotionService.cs:182`
- `src/Helper.Runtime/Generation/GenerationTemplatePromotionService.cs:257`

Inside certification, the service runs:

1. compile gate
2. real build validation
3. artifact validation
4. smoke evaluation
5. safety scan
6. placeholder scan

Relevant code:

- `src/Helper.Runtime/Generation/TemplateCertificationService.cs:55`
- `src/Helper.Runtime/Generation/TemplateCertificationService.cs:62`
- `src/Helper.Runtime/Generation/TemplateCertificationService.cs:63`
- `src/Helper.Runtime/Generation/TemplateCertificationService.cs:69`
- `src/Helper.Runtime/Generation/TemplateCertificationService.cs:81`
- `src/Helper.Runtime/Generation/TemplateCertificationService.cs:88`

This creates the following nested `dotnet build` chain for the console smoke test:

1. promotion pre-certification compile gate
2. candidate certification compile gate
3. candidate certification real build
4. post-activation certification compile gate
5. post-activation certification real build

Conclusion:

- The test is structurally expensive.
- Its runtime profile is consistent with the blame timeout being the trigger.

### 3. The apparent "wrapper hang" happens after inner process termination, not during useful build work

Observed monitored behavior after the blame run:

- `testhost` and inner build process disappear.
- The outer `dotnet test` root stays alive.
- `CPU delta = 0`.
- `certification_process_trace.jsonl` no longer changes.
- `TestResults` no longer change.

This is consistent with an idle launcher residue, not with an actively blocked build.

Conclusion:

- The user-visible stuck state is caused by the outer root process not completing lifecycle cleanly after the inner host/build are killed.
- This is why the wrapper needed explicit wall-time, idle-time and process-tree teardown logic.

### 4. The final active build is the point of interruption, but not yet proven to be the root deadlock

The blame trace shows the last active operation was:

- `templates/Template_ConsoleTool/1.0.0/ConsoleTool.csproj`

Relevant trace:

- `temp/verification/hang_trace_promotion_class/certification_process_trace.jsonl`

However, the successful no-blame trace shows that the same final active build can exit normally with `exitCode=0` in about `2s`.

Conclusion:

- The final active build is the point where the timeout lands.
- The available evidence does not prove that this build intrinsically deadlocks.
- The stronger diagnosis is: the run is externally terminated while this build is in flight.

## Secondary Findings

### 5. `DotnetService.BuildAsync` has a weak target-selection contract

Current code:

- `src/Helper.Runtime/DotnetService.cs:18`
- `src/Helper.Runtime/DotnetService.cs:19`
- `src/Helper.Runtime/DotnetService.cs:20`

Behavior:

- It recursively scans for the first `*.sln`.
- If none, it recursively scans for the first `*.csproj`.
- It does not explicitly exclude nested `bin`, `obj`, or `.compile_gate` trees during target selection.

Why this matters:

- It makes build target selection dependent on filesystem enumeration order.
- In a template tree with transient workspaces or stale artifacts, this can become nondeterministic.

What the current evidence says:

- In the observed traces, the selected target was the expected `ConsoleTool.csproj`.
- Therefore this is a real defect risk, but not the demonstrated cause of the current hang symptom.

### 6. The expensive path is amplified by compile-gate duplication, not by report writing or lifecycle file I/O

Reviewed code paths:

- `src/Helper.Runtime/Generation/TemplateCertificationService.Reporting.cs`
- `src/Helper.Runtime/Generation/TemplateCertificationService.Safety.cs`
- `src/Helper.Runtime/TemplateLifecycleService.cs`
- `src/Helper.Runtime/ProjectTemplateManager.cs`

Assessment:

- report writing is linear file I/O after validation
- lifecycle activation writes `.active_version` and history files only
- manager logic is metadata/copy logic, not a blocking orchestration loop

Conclusion:

- These components are not the primary blocking site in the observed failure chain.

## What Was Ruled Out

### Ruled out: wrapper cleanup as the primary original hang source

Reason:

- The evidence shows the root process remains after the inner host/build are already gone.
- That means the visible stuck state is downstream of the inner abort, not upstream inside ordinary cleanup code.

### Ruled out: post-certification reporting as the active blocking step

Reason:

- In the blame trace, interruption happens during the final active `dotnet build`, before report writing.

### Ruled out: global test parallelism as the main cause

Reason:

- Test parallelization is already disabled:
  - `test/Helper.Runtime.Tests/TestCollectionConfig.cs:3`
  - `test/Helper.Runtime.Tests/TestInfrastructure/ProcessEnvironmentCollection.cs:5`

## Causal Model

The most defensible causal model is:

1. `PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke` creates a real promotion pipeline.
2. The pipeline performs repeated nested builds because promotion and certification both invoke build-heavy validation.
3. On a blame run with `60s` timeout, the test reaches the final post-activation build before completing.
4. The blame collector kills the inner host/build tree.
5. The outer `dotnet test` root sometimes remains alive and idle.
6. Monitoring sees a live `dotnet` process with no CPU and no file writes, and this gets interpreted as a hang.

This is the actual cause-effect chain behind the observed symptom.

## Practical Implications

1. If the goal is accurate diagnosis, `--blame-hang --blame-hang-timeout 60s` is too aggressive for this test and produces false-positive hang evidence.
2. If the goal is operational robustness, the outer certification wrapper must aggressively detect idle residue and kill the full process tree.
3. If the goal is runtime reduction, the promotion/certification design should avoid repeated compile/build validation of the same template state.
4. If the goal is deterministic nested-build behavior, `DotnetService` should eventually stop using recursive first-match target discovery.

## Recommended Next Remediation Order

1. Keep the wrapper watchdog and process-tree teardown in `scripts/run_certification_compile_tests.ps1`.
2. Stop using `60s` blame-hang timeout as the primary diagnostic for this class; use a materially larger timeout or no blame for baseline reproduction.
3. Refactor promotion/certification ownership so post-activation certification does not repeat the full candidate certification cost without necessity.
4. Harden `DotnetService.BuildAsync` target resolution to choose the intended root project explicitly and ignore transient workspaces/artifacts.

