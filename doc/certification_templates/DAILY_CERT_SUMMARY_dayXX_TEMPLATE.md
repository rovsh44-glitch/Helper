# DAILY_CERT_SUMMARY_dayXX

Execution date (UTC): `YYYY-MM-DD`
Official window day: `Day XX`
Plan references:
1. `doc/certification/active/HELPER_CERTIFICATION_DOC_INDEX_2026-03-08.md`
2. `doc/certification/active/HELPER_UNIFIED_HUMAN_GOLDEN_CERTIFICATION_MASTER_PLAN_2026-03-08_REWRITTEN.md`

Template scope:
1. Official days always use strict `3.2 = PASS|FAIL`.
2. Pre-cert days `01-13` may record `3.2 = ANCHOR_PENDING` only when:
   - `3.1`, `3.3`, `3.4`, `3.5` are green;
   - the only unmet `3.2` condition is incomplete/global pre-anchor window state;
   - no already closed day inside the active pre-cert cycle is red.
3. Pre-cert Day `14` must still close with strict `3.2 = PASS`, `WindowComplete = true`.

## 1. Mandatory package status (5/5)

1. `3.1 Parity certification snapshot`: `PASS|FAIL`
   Artifact: `doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/HELPER_PARITY_GATE_dayXX.md`
2. `3.2 Parity window gate (14d strict, anchor-backed)`: `PASS|FAIL|ANCHOR_PENDING (pre-cert Day 01-13 only)`
   Artifacts:
   - `doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/HELPER_PARITY_WINDOW_GATE_dayXX.md`
   - `doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/HELPER_PARITY_WINDOW_GATE_dayXX.json`
   - `Anchor reference: precert_<START_DATE_UTC>|official_only`
3. `3.3 Real-task generation pack`: `PASS|FAIL`
   Artifact: `doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/SMOKE_COMPILE_dayXX.md`
4. `3.4 Closed-loop predictability suite`: `PASS|FAIL`
   Artifact: `doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/CLOSED_LOOP_PREDICTABILITY_dayXX.md`
5. `3.5 Dialog quality eval suite`: `PASS|FAIL`
   Artifacts:
   - `doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/EVAL_GATE_dayXX.log`
   - `doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/EVAL_REAL_MODEL_dayXX.md`
   - `doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/HUMAN_PARITY_dayXX.md`

## 2. KPI snapshot (from 3.1)

1. Golden Hit Rate: `__%` (target `>=90%`) -> `PASS|FAIL`
2. Generation Success Rate: `__%` (target `>=95%`) -> `PASS|FAIL`
3. P95 Ready Seconds: `__` (target `<=25`) -> `PASS|FAIL`
4. Unknown Error Rate: `__%` (target `<=5%`) -> `PASS|FAIL`

## 3. Smoke and generation quality (from 3.3)

1. Runs: `__`
2. Compile pass rate: `__` (target `>=0.90`) -> `PASS|FAIL`
3. Smoke p95 duration sec: `__` (target `<=120`) -> `PASS|FAIL`
4. Top error codes: `[...]`
5. Timeout/no-report incidents: `count=__`, status `OPEN|CLOSED`

## 4. Predictability status (from 3.4)

1. TopIncidentClasses: `__` (target `30`)
2. RepeatsPerClass: `__` (target `20`)
3. MaxAllowedVariance: `__%` (target `<=5%`)
4. Result: `PASS|FAIL`

## 5. Dialog quality status (from 3.5)

1. `run_eval_gate.ps1`: `PASS|FAIL`
2. `run_eval_real_model.ps1`:
   - scenarios: `__`
   - runtime errors: `__` (target `0`)
   - quality failures: `__`
   - pass rate: `__%` (target `>=85%`)
   - result: `PASS|FAIL`
3. Human parity:
   - sample size: `__` (required `>=200`)
   - sample status: `SUFFICIENT|INSUFFICIENT_DATA`
   - usefulness/category/gap thresholds: `PASS|FAIL`
   - result: `PASS|FAIL`

## 6. Causal incidents and corrective actions

For each incident:
1. `Incident ID`
2. `Root cause` (technical)
3. `Impact` (which package/KPI failed)
4. `Corrective action` (what changed)
5. `Preventive action` (how recurrence is blocked)
6. `Owner`
7. `ETA`
8. `State: OPEN|MITIGATED|CLOSED`

## 7. Governance checks

1. `HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE` used? `YES|NO` (must be `NO`)
2. Any `NoFailOnThreshold` flags used? `YES|NO` (must be `NO`)
3. Open P0/P1 at EOD: `__`
4. Code freeze respected (official window): `YES|NO`
5. Locked pre-cert anchor reference: `precert_YYYY-MM-DD|N/A`
6. `3.2` interpreted against moving last-14 closed daily snapshots: `YES|NO|PRECERT_ANCHOR_PENDING`

## 8. Day result and counter

1. Day status: `PASSED|FAILED|GREEN_ANCHOR_PENDING (pre-cert Day 01-13 only)`
2. Certification counter:
   - if PASSED: `COUNT = N/14`
   - if FAILED: `RESET TO 0`
   - if GREEN_ANCHOR_PENDING: `PRECERT_CLOSED = N/14`, official counter still `N/A`
3. Release decision for next day: `GO|NO-GO`
4. Sign-off:
   - Run Owner: `__`
   - Incident Commander: `__`
   - Release Approver: `__`
