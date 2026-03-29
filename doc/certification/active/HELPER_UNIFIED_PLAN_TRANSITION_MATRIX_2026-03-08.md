# HELPER Unified Plan Transition Matrix (2026-03-08)

## Scope

This matrix compares:
1. `doc/certification/history/HELPER_UNIFIED_HUMAN_GOLDEN_CERTIFICATION_MASTER_PLAN_2026-03-03.md`
2. `doc/certification/active/HELPER_UNIFIED_HUMAN_GOLDEN_CERTIFICATION_MASTER_PLAN_2026-03-08_REWRITTEN.md`

It also maps both plans to the factual pre-cert state recorded on:
1. `2026-03-07` for counted pre-cert day-01
2. `2026-03-08` for counted pre-cert day-02

## Decision Summary

1. The rewritten `2026-03-08` plan can be accepted as the base operational certification plan.
2. Already completed work can be carried forward only where it cleanly matches the rewritten status model.
3. Counted pre-cert `day-01` is carry-forward compatible.
4. Counted pre-cert `day-02` is not carry-forward compatible as an anchor continuation because it failed and reset the cycle.
5. Preview artifacts remain evidence-only and cannot be converted into counted anchor days.

## Transition Matrix

| Topic | 2026-03-03 original plan | 2026-03-08 rewritten plan | Factual evidence as of 2026-03-08 | Carry forward status | Operational rule |
|---|---|---|---|---|---|
| Program objective | Unified human parity + golden lifecycle + official 14-day certification | Same objective, restated more clearly | No contradiction with recorded execution | YES | Use rewritten plan as the normative base document |
| KPI thresholds | Golden Hit >= 90%, Success >= 95%, P95 <= 25s, Smoke >= 0.90, Eval >= 85%, runtime errors = 0 | Same KPI contract | Day-01 rerun and day-02 met `3.1`, `3.3`, `3.4`; day-02 failed `3.5` | YES | No KPI migration needed |
| Daily command set | Same build/test/parity/window/smoke/eval/human commands | Same command set | Counted runner already uses the same package structure | YES | No command rewrite required |
| Governance | No incomplete-window bypass, no `-NoFailOnThreshold*`, no manual snapshot rewrite | Same governance, stated more explicitly | Day-01 rerun preserved original failed evidence instead of overwriting it | YES | Keep strict mode and preserve raw evidence |
| `3.2` meaning | Ambiguous: says `3.2` must be closed first, but also allows `GREEN_ANCHOR_PENDING` on `R-Day-01..13` | Clarified: `ANCHOR_PENDING` for early pre-cert days, `FULLY_GREEN` for day-14, strict pass only for complete window | Day-01 already matches the clarified model exactly | YES | Interpret counted pre-cert day-01 under the rewritten status model |
| Day status vocabulary | Mixed use of `all KPI green` and `GREEN_ANCHOR_PENDING` | Formal split between functional pass, `GREEN_ANCHOR_PENDING`, `FULLY_GREEN`, and officially valid day | Day-01 rerun explicitly closed as `GREEN_ANCHOR_PENDING` and not as official day-01 | YES | Re-label historical interpretation using rewritten vocabulary, without editing raw artifacts |
| Counted pre-cert day-01 (`2026-03-07`) | Allowed in practice but not fully formalized | Fully supported by the new model | `GREEN_ANCHOR_PENDING`, pre-cert counter `1/14`, not official day-01 | YES | Treat day-01 as valid carry-forward evidence inside the historical record |
| Counted pre-cert day-02 (`2026-03-08`) | Failure resets official/pre-cert continuation | Same reset logic in practice | `3.5` failed because `run_eval_real_model.ps1` crashed; day result `FAILED`; pre-cert counter `0/14 (reset)`; cycle state `day02_failed` | NO for active counter | Do not continue to day-03; restart from a new counted pre-cert day-01 after the fix |
| Preview runs | Not a counted substitute for official or counted pre-cert days | Rewritten plan still requires real counted daily closure | Preview day-14 is explicitly `NOT_COUNTED_PREVIEW` and isolated from counted `3.1`/`3.2` sources of truth | NO for counter | Keep preview only as diagnostic evidence |
| Historical snapshot handling | No manual editing of history | Same rule, clearer wording | Day-01 raw failure plus remediation rerun are both preserved | YES | Keep both raw fail evidence and green rerun evidence; do not rewrite history |
| Official cycle readiness | Requires completed strict 14-day anchor before official day-01 | Same, with cleaner causal model | Earliest official day-01 remains blocked; current counted cycle is not anchor-complete | NO | Official cycle must not start yet |

## What Is Counted Right Now

| Evidence item | Current status | Can it count toward the active anchor? | Notes |
|---|---|---|---|
| `precert_2026-03-07/day-01` raw strict execution | Preserved historical fail evidence | NO | Audit evidence only |
| `precert_2026-03-07/day-01` remediation rerun | `GREEN_ANCHOR_PENDING` | YES as historical carry-forward evidence | Compatible with rewritten pre-cert day-01 semantics |
| `precert_2026-03-07/day-02` counted run | `FAILED` | NO | Failed `3.5`; counter reset |
| `precert_2026-03-07/preview/day-14` diagnostic run | `NOT_COUNTED_PREVIEW` | NO | Explicitly isolated from counted truth sources |
| Official certification days | None opened | NO | Official day-01 is not yet eligible |

## Exact Carry-Forward Boundary

1. Accept the rewritten `2026-03-08` document as the main operational plan.
2. Preserve `precert_2026-03-07/day-01` as a valid historical `GREEN_ANCHOR_PENDING` closure.
3. Preserve `precert_2026-03-07/day-02` as failure evidence and as the reason the counted cycle was reset.
4. Do not reinterpret preview runs as counted anchor days.
5. Do not continue the current counted cycle from `day-03`.

## Required Next Step

1. Fix `scripts/run_eval_real_model.ps1` for the `Exception.Response` null-path/runtime failure seen on counted day-02.
2. After the fix is verified, open a new counted pre-cert `day-01` in a brand-new cycle dated with the actual execution calendar date.
3. Same-day restart is allowed for `day-01` if `StartDateUtc` equals that execution date and no old counted artifacts are reused.
4. Keep `precert_2026-03-07` archived as evidence, not as an active continuing counted cycle.
