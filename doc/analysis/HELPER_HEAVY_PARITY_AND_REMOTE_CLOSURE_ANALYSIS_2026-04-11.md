# HELPER Heavy Parity And Remote Closure Analysis

Date: `2026-04-11`

## Scope

This document answers two questions:

1. what must change so `powershell -ExecutionPolicy Bypass -File scripts/ci_gate_heavy.ps1` does not fail on the current parity KPI blocker
2. what is still required to move from local closure to remote GitHub closure

## Current heavy-gate failure

Observed heavy-gate failure:

1. `Generation parity gate`
2. `insufficient_golden_sample`
3. `GenerationSuccessRate 0.00% < 95.00%`

Latest heavy artifact:

1. `temp/verification/heavy/HELPER_PARITY_GATE_2026-04-11_18-53-05.md`

Reported values:

1. `LoadedRunEntries: 176`
2. `WindowRunEntriesBeforeWorkloadFilter: 0`
3. `WindowRunEntries: 0`
4. `Total Runs: 0`
5. `GoldenSource: runtime_fallback`

## Root cause chain

### Root cause 1: the gate has history, but not in the active time window

The gate is not failing because the parser is broken.

It is failing because the discovered run history does not contain any entries inside the current `24h` parity window.

Evidence:

1. the analyzer loaded `176` run entries overall
2. after the time-window cut, the count became `0`
3. after workload filtering, the count remained `0`

So `GenerationSuccessRate` becomes `0` because `TotalRuns == 0`, not because recent runs were evaluated and failed.

### Root cause 2: the current heavy lane does not create fresh parity runs before evaluating the gate

`scripts/ci_gate_heavy.ps1` currently executes:

1. `run_load_chaos_smoke.ps1`
2. `run_generation_parity_gate.ps1`
3. `run_generation_parity_benchmark.ps1`
4. `run_generation_parity_window_gate.ps1`

But the repo already contains an ordered parity flow that says the opposite sequence is required:

1. `scripts/run_parity_golden_batch.ps1`
2. `scripts/run_generation_parity_gate.ps1`

That ordering is documented directly in `scripts/run_day1_ordered_certification.ps1`.

### Root cause 3: the real run-history source is stale for parity

Observed run-history files:

1. `D:\HELPER_DATA\generation_runs.jsonl`
   - last write: `2026-03-15 12:24:34Z`
   - effectively empty
2. `D:\HELPER_DATA\PROJECTS\generation_runs.jsonl`
   - last write: `2026-03-29 15:01:37Z`
   - contains real parity records

Distinct `parity` dates in the discovered canonical run history:

1. `2026-03-24` -> `2` entries
2. `2026-03-29` -> `1` entry

So there are not enough recent parity runs to satisfy the gate.

### Root cause 4: the gate thresholds are strict and currently correct

The current parity gate requires:

1. at least `20` golden attempts
2. `GenerationSuccessRate >= 95%`
3. `GoldenHitRate >= 90%`

Those are not misconfigured relative to the implementation.

The problem is missing fresh evidence, not a bad threshold read.

## What is required for the parity gate to pass honestly

To make `run_generation_parity_gate.ps1` pass on real KPI data, the system must produce fresh parity workload entries first.

Minimum required actions:

1. run `scripts/run_parity_golden_batch.ps1` before `scripts/run_generation_parity_gate.ps1`
2. keep `HELPER_GENERATION_WORKLOAD_CLASS=parity` on those runs
3. produce at least `20` `GoldenTemplateEligible=true` attempts inside the active lookback window
4. ensure at least `95%` of those runs are counted as clean success
5. ensure at least `90%` of eligible attempts are `GoldenTemplateMatched=true`

Practical implementation:

1. add a new heavy-lane step before `Generation parity gate`
2. use `run_parity_golden_batch.ps1 -Runs 24 -FailOnThresholds`
3. then run `run_generation_parity_gate.ps1`

Without that sequencing change, `ci_gate_heavy.ps1` will continue reading stale history and continue producing `0` window runs.

## What will fail next even after that fix

The next likely blocker is `Generation parity window gate`.

Current daily snapshot state under `doc/parity_nightly/daily`:

1. available day files: `5`
2. several latest day snapshots already contain `TotalRuns: 0` and the same parity alerts

The current window gate requires:

1. `7` available days by default
2. each day to satisfy the same parity KPI contract

Current real parity dates in run history are only:

1. `2026-03-24`
2. `2026-03-29`

So even with one fresh successful parity batch today, the strict `7-day` window still will not pass honestly.

## Honest paths to a green heavy parity contract

There are only three technically coherent options.

### Option A: keep the current strict contract

Required:

1. add the parity batch step before the parity gate
2. collect real passing parity workload data across at least `7` distinct days
3. backfill or regenerate daily snapshots from real `generation_runs.jsonl`
4. rerun the window gate only after the window is actually complete

This is the only option that keeps the current heavy lane as a real certification gate.

### Option B: split heavy into two contracts

Required:

1. keep `parity gate` as same-day evidence after a fresh batch
2. move `parity window gate` into a scheduled certification workflow
3. do not require the window gate in ad hoc heavy runs

This is the most practical operational model if heavy is expected to run on demand.

### Option C: weaken policy

Possible knobs:

1. lower `HELPER_PARITY_MIN_GOLDEN_ATTEMPTS`
2. lower `HELPER_PARITY_GATE_MIN_GENERATION_SUCCESS_RATE`
3. set `HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE=true`

This would make the heavy gate easier to pass, but it would no longer represent the same certification claim.

For a production-facing certification signal, this option is not recommended.

## Recommended parity remediation

Recommended path:

1. patch `scripts/ci_gate_heavy.ps1` to run `scripts/run_parity_golden_batch.ps1 -Runs 24 -FailOnThresholds` before `scripts/run_generation_parity_gate.ps1`
2. add a backfill step after the batch when needed, using `scripts/run_generation_parity_backfill.ps1`
3. remove `parity window gate` from same-day heavy closure and move it to scheduled certification, unless the team is prepared to wait for a real 7-day window

Reason:

1. it fixes the current stale-history failure honestly
2. it preserves strict parity thresholds
3. it avoids claiming a `7-day` certification state from one same-day execution

## Remote-closure state

Current local git state:

1. current branch: `merge-main`
2. `HEAD == origin/main == 0539e7e8573ab9213325f82f7eec4f6b3c71354d`
3. working tree is dirty with many modified and untracked remediation files

This means no local remediation has been committed or pushed yet.

## Remote-closure blockers

### Blocker 1: no commit exists for the new closure state

Remote closure cannot happen until the current remediation set is committed.

### Blocker 2: GitHub verification is not currently available from this environment

Observed:

1. the GitHub connector returned `401 token_expired`
2. `gh` CLI is not installed in this environment

So hosted status-context verification cannot currently be completed from this session.

### Blocker 3: the new workflow still exists only locally

`repo-gate.yml` is present locally, but it is still untracked.

Until it is committed and pushed, the remote repository cannot attach the new deterministic status context.

## What remote closure requires next

1. refresh GitHub authentication for either the connector or `gh`
2. create at least one commit for the remediation set
3. push the branch containing:
   - `.github/workflows/repo-gate.yml`
   - `.github/workflows/runtime-test-lanes.yml`
   - the deterministic/heavy gate split
   - the parity-output rerouting changes
4. open a PR or push to the intended protected branch
5. verify the hosted deterministic workflow passes
6. attach required status checks in branch protection

## Recommended remote-closure sequence

1. do not block remote deterministic closure on the current parity KPI issue
2. push the deterministic repo-gate contract first
3. keep heavy parity as manual or scheduled until real parity evidence exists
4. after GitHub auth is restored, verify that the new deterministic workflow reports status on the remote commit

## Bottom line

`ci_gate_heavy.ps1` is not failing because the parity evaluator is wrong.

It is failing because:

1. the heavy lane does not generate fresh parity workload evidence before evaluating the gate
2. the currently discovered parity history is stale
3. the strict `7-day` window cannot pass honestly from a same-day run anyway

Remote closure is not yet executable from this session because:

1. the changes are still uncommitted
2. GitHub verification auth is currently unavailable
