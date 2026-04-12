# HELPER Runtime Test Lanes

Status: `active`
Date: `2026-04-03`

## Purpose

This note defines the operator and developer execution policy for the split runtime test lanes.

## Lanes

1. `Fast`
   Run on the default local loop and pull-request validation.
   Entry point: `scripts/run_fast_tests.ps1`
   Scope: `Helper.Runtime.Tests`, `Helper.Runtime.Api.Tests`, `Helper.Runtime.Browser.Tests`

2. `Integration`
   Run before manual merge approval when orchestration, workspace state, template routing, or web-research flows changed.
   Entry point: `scripts/run_integration_tests.ps1`

3. `Certification`
   Run for nightly, release, load, benchmark, lifecycle, routing, diagnostics, and other non-compile heavy regression validation.
   Entry point: `scripts/run_certification_tests.ps1`
   Scope: load/chaos, tool benchmark, lifecycle, routing, diagnostics, and certification history coverage that does not spawn nested `dotnet`

4. `Eval`
   Run when the dedicated evaluation corpus, offline thresholds, or eval export preparation changes.
   Entry point: `scripts/run_eval_harness_tests.ps1`
   Scope: `Eval`, `EvalOffline`, `EvalV2`, human-like communication eval coverage, and web-research parity eval package preparation

5. `CertificationCompile`
   Run the compile-gate and nested `dotnet` subset that intentionally spins child build workspaces.
   Entry point: `scripts/run_certification_compile_tests.ps1`
   Scope: compile-gate, template certification smoke, promotion end-to-end chaos, and other tests that intentionally spawn nested build execution
   Modes:
   - operational: default wrapper run, no blame collector
   - forensic: opt-in `-EnableBlameHang -BlameHangTimeoutSec 180` for evidence preservation

## Execution Policy

1. PR and normal local development:
   `powershell -ExecutionPolicy Bypass -File scripts\run_fast_tests.ps1`

2. Pre-merge manual gate:
   `powershell -ExecutionPolicy Bypass -File scripts\run_fast_tests.ps1`
   `powershell -ExecutionPolicy Bypass -File scripts\run_integration_tests.ps1`

3. Nightly or explicit certification:
   `powershell -ExecutionPolicy Bypass -File scripts\run_eval_harness_tests.ps1`
   `powershell -ExecutionPolicy Bypass -File scripts\run_certification_tests.ps1`
   `powershell -ExecutionPolicy Bypass -File scripts\run_certification_compile_tests.ps1`

4. Targeted lock-wait regression:
   `powershell -ExecutionPolicy Bypass -File scripts\check_certification_compile_lock_wait.ps1`

5. Forensic certification-compile rerun:
   `powershell -ExecutionPolicy Bypass -File scripts\run_certification_compile_tests.ps1 -Configuration Debug -EnableBlameHang -BlameHangTimeoutSec 180`

## Notes

- `Fast` and `Integration` must stay clear of heavy parity and compile-gate scenarios.
- `Eval` owns `Eval`, `EvalOffline`, `EvalV2`, and eval-package preparation coverage.
- `Certification` owns load/chaos, tool benchmark, lifecycle, routing, diagnostics, and template regression coverage that stays in-process.
- `Certification` must not own tests that construct `GenerationCompileGate(new DotnetService())` or `LocalBuildExecutor(new DotnetService())`.
- `CertificationCompile` owns the compile-gate and nested build subset so those scenarios do not contaminate the normal inner loop.
- `run_certification_compile_tests.ps1` is serialized by lane lock and waits for the active compile-lane run instead of failing immediately on a concurrent launch.
- Each `CertificationCompile` run writes to a dedicated run root under `...Debug\runs\<runId>` with isolated `HELPER_DATA`, `TestResults`, and `certification_process_trace.jsonl`.
- Compile-lane interruption cleanup is repo-owned: the script uses `try/catch/finally`, idle and wall-time detection, process-tree snapshots, teardown summaries, and process-tree termination before releasing the lock.
- Lock waiting is bounded and configurable through `HELPER_CERTIFICATION_COMPILE_LOCK_WAIT_SEC` and `HELPER_CERTIFICATION_COMPILE_LOCK_POLL_SEC`.
- Operational compile-lane runs should use the wrapper without blame mode. `--blame-hang` is forensic only because the raw `60s` timeout produced false-positive hang kills for the console promotion class.
- Forensic runs must keep `BlameHangTimeoutSec >= 120`; the current recommended floor is `180`.
- Compile-lane stall detection is controlled by `HELPER_CERTIFICATION_COMPILE_MAX_WALL_SEC`, `HELPER_CERTIFICATION_COMPILE_IDLE_TIMEOUT_SEC`, and `HELPER_CERTIFICATION_COMPILE_HEARTBEAT_SEC`.
- `scripts/check_certification_compile_lock_wait.ps1` is the regression runner for this behavior and should report distinct first and second `runId` values.
- Failure diagnostics are preserved under the run root, including `teardown_summary.json`, trace data, and `TestResults`.
- CI entry point: [runtime-test-lanes.yml](../../.github/workflows/runtime-test-lanes.yml).
- `npm run ci:gate:heavy` now generates a fresh parity golden batch before evaluating parity KPIs.
- Same-day heavy runs do not force the strict parity window gate unless the required number of daily snapshots already exists or the operator explicitly passes `-RequireParityWindow` to `scripts/ci_gate_heavy.ps1`.
