# HELPER Counted Pre-Cert Day-01 Review Checklist (2026-03-08)

## Purpose

This checklist is used after a new counted pre-cert `day-01` run finishes, to decide whether the day is accepted as `GREEN_ANCHOR_PENDING` or rejected as `FAILED`.

## Review Target

Replace placeholders before review:
1. `CycleId = <NEW_CYCLE_ID>`
2. `DayDir = doc/pre_certification/cycles/<NEW_CYCLE_ID>/day-01`
3. `ExecutionDate = <YYYY-MM-DD>`

## Required Inputs

The review is incomplete if any of these are missing:
1. `DAILY_CERT_SUMMARY_day01.md`
2. `HELPER_PARITY_GATE_day01.md`
3. `HELPER_PARITY_WINDOW_GATE_day01.md`
4. `HELPER_PARITY_WINDOW_GATE_day01.json`
5. `SMOKE_COMPILE_day01.md`
6. `CLOSED_LOOP_PREDICTABILITY_day01.md`
7. `EVAL_GATE_day01.log`
8. `EVAL_REAL_MODEL_day01.md`
9. `EVAL_REAL_MODEL_day01.errors.json`
10. `HUMAN_PARITY_day01.md`

## Identity And Guard Checks

Mark each item `PASS` or `FAIL`:

| Check | Expected result | Status | Notes |
|---|---|---|---|
| New cycle ID is used | Not `precert_2026-03-07` |  |  |
| Reviewed directory is counted | Not `preview/*` |  |  |
| Day is `day-01` | Exact match |  |  |
| Real date matches cycle start date | `ExecutionDate = StartDateUtc` |  |  |
| Strict mode preserved | `HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE=false` |  |  |
| No soft bypass flags | No `-NoFailOnThreshold*`, no incomplete-window bypass |  |  |

## Package Review

| Package | Acceptable result for counted day-01 | Status | Notes |
|---|---|---|---|
| `3.1` parity certification snapshot | `PASS` |  |  |
| `3.2` parity window gate | Raw strict gate may be red only because window is incomplete |  |  |
| `3.3` real-task smoke | `PASS` |  |  |
| `3.4` closed-loop predictability | `PASS` |  |  |
| `3.5` dialog quality eval suite | `PASS` |  |  |

## `3.2` Boundary Check

`3.2` is acceptable for counted `day-01` only if all are true:
1. `WindowComplete=False` because the anchor is not yet built.
2. The red result is not caused by a fresh product-quality regression.
3. `3.1`, `3.3`, `3.4`, and `3.5` are all green.
4. The interpretation is `ANCHOR_PENDING`, not a waived official strict pass.

If any of the points above is false, mark the day `FAILED`.

## Summary Review

Read `DAILY_CERT_SUMMARY_day01.md` and confirm:

| Summary field | Expected value | Status | Notes |
|---|---|---|---|
| Day status | `GREEN_ANCHOR_PENDING` |  |  |
| Pre-cert counter | `1/14` |  |  |
| Official counter | `N/A` |  |  |
| Release decision for next day | `GO` |  |  |
| Next executable profile | `<NEW_CYCLE_ID>/day-02` |  |  |

## Automatic NO-GO Conditions

Return `NO-GO` immediately if any of the following is true:
1. `EVAL_REAL_MODEL_day01.md` is missing.
2. `EVAL_REAL_MODEL_day01` reports runtime errors greater than `0`.
3. `run_eval_real_model.ps1` failed before report generation.
4. Any functional package among `3.1`, `3.3`, `3.4`, `3.5` is red.
5. The run used preview artifacts or reused older counted artifacts as substitutes.
6. The summary does not end in `GREEN_ANCHOR_PENDING`.

## Final Decision

| Decision | Rule | Status |
|---|---|---|
| `GO` | All guard checks pass, all functional packages pass, `3.2` is only `ANCHOR_PENDING`, summary closes as `GREEN_ANCHOR_PENDING` with `1/14` |  |
| `NO-GO` | Any guard fails, any functional package fails, or summary/counter state is inconsistent |  |

## Reviewer Note

If the verdict is `GO`, the next allowed counted profile is:
1. `<NEW_CYCLE_ID>/day-02` on the next real calendar date.

If the verdict is `NO-GO`:
1. open an incident;
2. preserve all artifacts;
3. do not open counted `day-02`;
4. restart from a new counted `day-01` only after the blocker is fixed and re-verified.
