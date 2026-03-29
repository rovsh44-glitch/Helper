# HELPER New Counted Pre-Cert Day-01 Runbook (2026-03-08)

## Goal

Correctly open a new counted pre-cert `day-01` after the fix in `scripts/run_eval_real_model.ps1`, without reusing the failed counted cycle `precert_2026-03-07`.

## Preconditions

1. Leave `doc/pre_certification/cycles/precert_2026-03-07` read-only as historical evidence.
2. Do not reuse preview artifacts as counted evidence.
3. Do not use `-NoFailOnThreshold*` or incomplete-window bypass.
4. Ensure `HELPER_API_KEY` is available in the environment, or pass `-ApiKey` explicitly.

## Date Rule

1. A new counted `day-01` may be opened on the same real calendar date as the fix verification, as long as it is a brand-new cycle.
2. `scripts/run_precert_counted_day.ps1` enforces `current calendar date = StartDateUtc + (Day - 1)`.
3. For `day-01`, that means the counted run date must equal the new cycle `StartDateUtc`.
4. Pass `-StartDateUtc` explicitly as the actual execution date to avoid UTC/local-date ambiguity.
5. If execution is deferred, replace the example date in all commands with that later actual execution date.

## Step 1. Verify the Fix Directly

Run a direct real-model verification first. This is not a counted day yet.

```powershell
$env:HELPER_API_KEY = "<YOUR_API_KEY>"

powershell -ExecutionPolicy Bypass -File scripts/run_eval_real_model.ps1 `
  -ApiBase "http://127.0.0.1:5000" `
  -ApiKey $env:HELPER_API_KEY `
  -MaxScenarios 200 `
  -MinScenarioCount 200 `
  -RequireApiReady `
  -LaunchLocalApiIfUnavailable `
  -ApiRuntimeDir "doc/pre_certification/verification/eval_runtime" `
  -ReadinessTimeoutSec 600 `
  -ReadinessPollIntervalMs 2000 `
  -OutputReport "doc/pre_certification/verification/EVAL_REAL_MODEL_fix_day01.md" `
  -ErrorLogPath "doc/pre_certification/verification/EVAL_REAL_MODEL_fix_day01.errors.json"
```

Proceed only if:
1. the script exits successfully;
2. the report is written;
3. runtime errors are `0`;
4. no `Exception.Response` null-path failure reappears.

## Step 2. Initialize a New Counted Cycle

Example for same-day start on `2026-03-08`:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/init_precert_cycle.ps1 `
  -WorkspaceRoot "." `
  -StartDateUtc "2026-03-08" `
  -ResetBeforeStart
```

Expected result:
1. a new cycle `precert_2026-03-08` is created;
2. `PRECERT_CYCLE_STATE.json` starts with `closedDays=0`;
3. previous run logs are reset through the standard reset path.

## Step 3. Run the New Counted Day-01

Example for same-day start on `2026-03-08`:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_precert_counted_day.ps1 `
  -CycleId "precert_2026-03-08" `
  -Day 1 `
  -ApiBase "http://127.0.0.1:5000" `
  -ApiKey $env:HELPER_API_KEY
```

This wrapper already executes the counted package sequence:
1. `llm_preflight`
2. `parity_batch`
3. `parity_gate`
4. `parity_window_raw`
5. `smoke_compile`
6. `closed_loop`
7. `eval_gate`
8. `eval_real_model`
9. `human_parity`

## Step 4. Use Safe Finalization Only If Needed

If the full package already ran and artifacts exist, but the wrapper needs to re-materialize summary/state without rerunning the whole pipeline, use:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_precert_counted_day.ps1 `
  -CycleId "precert_2026-03-08" `
  -Day 1 `
  -ReuseExistingArtifacts
```

Use this only for safe finalization, not as a substitute for a real counted run.

## Step 5. Accept or Reject Day-01

`day-01` is acceptable only if:
1. `3.1 = PASS`
2. `3.3 = PASS`
3. `3.4 = PASS`
4. `3.5 = PASS`
5. raw `3.2` is red only because the 14-day window is still incomplete
6. final day status is `GREEN_ANCHOR_PENDING`
7. pre-cert counter becomes `1/14`

If any functional package fails:
1. the day is `FAILED`;
2. the counter resets;
3. do not open counted `day-02`.

## Required Outputs

Verify these files under the new cycle day directory:
1. `DAILY_CERT_SUMMARY_day01.md`
2. `HELPER_PARITY_GATE_day01.md`
3. `HELPER_PARITY_WINDOW_GATE_day01.md`
4. `SMOKE_COMPILE_day01.md`
5. `CLOSED_LOOP_PREDICTABILITY_day01.md`
6. `EVAL_GATE_day01.log`
7. `EVAL_REAL_MODEL_day01.md`
8. `HUMAN_PARITY_day01.md`

## Hard Boundaries

1. Do not continue from `precert_2026-03-07/day-03`.
2. Do not copy prior day artifacts into the new cycle.
3. Do not convert preview runs into counted anchor days.
4. Do not manually rewrite parity snapshots or anchor history.
