# GO/NO-GO Checklist: `precert_2026-03-15/day-01`

## Scope

This checklist is the operator review sheet for the clean restart-cycle counted pre-cert launch on `2026-03-15`.

## Identity

1. CycleId: `precert_2026-03-15`
2. Day: `day-01`
3. ExecutionDate: `2026-03-15`
4. Review root: `doc/pre_certification/cycles/precert_2026-03-15/day-01`

## Pre-Run Gates

| Check | Expected | Status | Notes |
|---|---|---|---|
| Certification docs centralized | `doc/certification/` is the operator entry point | `PASS` | `doc/certification/README.md` added; active/history/reference/operations centralized. |
| Direct eval verification prepared | `run_eval_real_model.ps1` can run with real key | `PASS` | Restart verification: `doc/pre_certification/verification/EVAL_REAL_MODEL_precert_2026-03-15_day01_restart_direct.md`: `200/200`, runtime errors `0`, quality failures `0`. |
| New cycle date is current execution date | `2026-03-15` | `PASS` | `precert_2026-03-15` initialized through `scripts/init_precert_cycle.ps1`. |
| No reuse mode planned | `ReuseExistingArtifacts = false` | `PASS` | Full counted run used; no safe-finalization reuse path. |
| Strict mode planned | `HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE=false` | `PASS` | Reflected in cycle state and day summary. |

## Counted Day Acceptance

| Package / gate | Expected result | Status | Notes |
|---|---|---|---|
| `3.1` parity certification snapshot | `PASS` | `FAIL` | Final restart-run artifact is honest now: `TotalRuns=24`, `GoldenHit=100%`, `Success=66.67%`, `P95=3.76s`. Persistence/sample blocker is fixed; remaining fail is real generation success. |
| `3.2` parity window gate | `ANCHOR_PENDING` only because window incomplete | `FAIL` | Counted summary marks `CurrentCycleFailures=2026-03-15`, so this is not a pure anchor-pending day. |
| `3.3` smoke compile | `PASS` | `PASS` | `50/50`, `PassRate=1.00`, `NoReport=0`, `P95=5.28s`. |
| `3.4` closed-loop predictability | `PASS` | `PASS` | Passed with `TopIncidentClasses=30`, `RepeatsPerClass=20`. |
| `3.5` dialog quality eval suite | `PASS` | `PASS` | `200/200`, runtime errors `0`, quality failures `0`, human sample `200/SUFFICIENT`. |
| Day summary | `GREEN_ANCHOR_PENDING` and `1/14` | `FAIL` | Actual day result is `FAILED`, pre-cert counter `0/14 (reset)`, next profile `RESET_REQUIRED`. |

## Final Decision

| Decision | Rule | Status | Notes |
|---|---|---|---|
| `GO` | All functional packages pass, `3.2` is only anchor-pending, summary closes as `GREEN_ANCHOR_PENDING` | `FAIL` | `3.1` and `3.2` are not acceptable for counted day-01. |
| `NO-GO` | Any functional package fails, eval runtime errors > 0, or summary/counter state is inconsistent | `PASS` | Counted day closed `FAILED`; cycle reset is required before any next counted attempt. |

## Post-Run Diagnostic

1. The first same-day attempt was archived under `doc/pre_certification/archive/cycles/precert_2026-03-15_failed_attempt_*` before the clean restart-cycle was opened.
2. The shared tooling defect was fixed in `src/Helper.Runtime/Infrastructure/HelperWorkspacePathResolver.cs`, `src/Helper.Runtime/Generation/GenerationValidationReportWriter.cs`, `src/Helper.Runtime/Generation/GenerationArtifactLocator.cs`, and `scripts/reset_precert_runs.ps1`.
3. On the restart-run, parity gate now sees the full persisted set: `LoadedRunEntries=24`, `run_history_success_total=16`, `run_history_failed_total=8`.
4. The remaining blocker is no longer sample/persistence; it is actual parity generation quality: `GenerationSuccessRate 66.67% < 95%`.
5. The counted verdict for `precert_2026-03-15/day-01` remains `NO-GO` and must not be rewritten.
