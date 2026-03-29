# HELPER Certification Doc Index (2026-03-08)

## Scope

This index consolidates the current certification-transition document set created and reviewed on `2026-03-08`.

It covers:
1. the active operational certification pack;
2. older certification documents that were analyzed and intentionally retained;
3. temporary transition notes that can be archived safely.

## Active Operational Pack

Use these documents in this order:

1. `doc/certification/active/HELPER_UNIFIED_HUMAN_GOLDEN_CERTIFICATION_MASTER_PLAN_2026-03-08_REWRITTEN.md`
   Role: normative operational certification plan.
2. `doc/certification/active/HELPER_UNIFIED_PLAN_TRANSITION_MATRIX_2026-03-08.md`
   Role: explains what carries forward from earlier plans and what does not.
3. `doc/certification/operations/HELPER_NEW_COUNTED_PRECERT_DAY01_RUNBOOK_2026-03-08.md`
   Role: execution runbook for opening a new counted pre-cert `day-01` after the `run_eval_real_model.ps1` fix.
4. `doc/certification/operations/HELPER_COUNTED_PRECERT_DAY01_REVIEW_CHECKLIST_2026-03-08.md`
   Role: post-run acceptance checklist for counted pre-cert `day-01`.

## Retention Analysis

| Document | Decision | Reason |
|---|---|---|
| `doc/certification/active/HELPER_UNIFIED_HUMAN_GOLDEN_CERTIFICATION_MASTER_PLAN_2026-03-08_REWRITTEN.md` | KEEP ACTIVE | This is now the main operational source of truth. |
| `doc/certification/active/HELPER_UNIFIED_PLAN_TRANSITION_MATRIX_2026-03-08.md` | KEEP ACTIVE | Needed to justify carry-forward boundaries and reset logic. |
| `doc/certification/operations/HELPER_NEW_COUNTED_PRECERT_DAY01_RUNBOOK_2026-03-08.md` | KEEP ACTIVE | Needed for the next counted execution. |
| `doc/certification/operations/HELPER_COUNTED_PRECERT_DAY01_REVIEW_CHECKLIST_2026-03-08.md` | KEEP ACTIVE | Needed for `GO/NO-GO` review after counted `day-01`. |
| `doc/certification/history/HELPER_UNIFIED_HUMAN_GOLDEN_CERTIFICATION_MASTER_PLAN_2026-03-03.md` | KEEP IN CERTIFICATION/HISTORY | Superseded operationally, but still needed for traceability, source comparison, and references from existing docs. |
| `doc/certification/history/HELPER_14_DAY_CERTIFICATION_PLAN_2026-03-01.md` | KEEP IN CERTIFICATION/HISTORY | Historical certification source still referenced by existing evidence artifacts. |
| `doc/certification/history/HELPER_UNIFIED_14_DAY_EXECUTION_CALENDAR_2026-03-03.md` | KEEP IN CERTIFICATION/HISTORY | Historical planning calendar; not normative now, but still referenced by related analysis docs. |
| `doc/HELPER_MASTER_REMEDIATION_PLAN_2026-03-01.md` | KEEP IN ROOT | Separate remediation workstream, not a duplicate of the current certification pack. |
| `doc/HELPER_REMEDIATION_MASTER_PLAN_2026-03-06.md` | KEEP IN ROOT | Separate audit-remediation workstream, still relevant for implementation scope. |
| `doc/HELPER_DETAILED_REMEDIATION_PLAN_2026-03-07.md` | KEEP IN ROOT | Separate audit-follow-up plan, still relevant for corrective actions. |
| `doc/HELPER_BACKEND_FULL_REBUILD_PLAN_2026-03-06.md` | KEEP IN ROOT | Backend architecture/refactor record, not a disposable duplicate. |
| `doc/• На дату 2026-03-08 цикл precert_2.txt` | ARCHIVE | Temporary transition note. Its working conclusions are now captured in the transition matrix and runbook. |

## Practical Rule

1. For new execution, use only the active operational pack.
2. Use retained older documents only for source comparison, audit traceability, or historical evidence review.
3. Do not base new counted execution on the archived transition note.
4. A new counted pre-cert `day-01` may start on the same real calendar date as cycle initialization if `StartDateUtc` is set to that execution date.

## Archive Location

Archived transition notes for this document bundle live under:

1. `doc/archive/certification_transition_2026-03-08/`

## Non-Archive Boundary

The following files were deliberately not archived:
1. old certification source plans that are still referenced by existing documents;
2. remediation plans that belong to separate execution streams;
3. historical evidence artifacts under `doc/pre_certification/` and `doc/certification_*`.

This avoids breaking traceability while still cleaning out disposable transition notes.
