# HELPER Audit Remediation Baseline Ledger

Date: `2026-04-13`
Source audit: `doc/analysis/HELPER_AUDIT_2026-04-12.md`
Source plan: `doc/analysis/HELPER_AUDIT_REMEDIATION_PLAN_2026-04-12.md`
Related closures:

- `doc/analysis/HELPER_AUDIT_REMEDIATION_CLOSURE_2026-04-12.md`
- `doc/analysis/HELPER_AUDIT_REMEDIATION_CLOSURE_2026-04-13.md`

## Purpose

`Step 0.2` in the remediation plan asked for the exact remediation branch baseline to be recorded before changes began.

That did not happen literally during the original remediation turn. The work started in the already-open local worktree on `main`, and the exact baseline was only captured in the 2026-04-12 closure after the fact.

This ledger is the canonical retrospective traceability artifact that closes `Step 0.2` honestly without rewriting Git history or creating a synthetic backdated branch.

## Canonical Baseline And Remediation Chain

| Stage | Reference | Exact identity | Meaning |
| --- | --- | --- | --- |
| Audited baseline observed during remediation | 2026-04-12 closure baseline section | `main` @ `36aa39ccbc9ed3feb485085911580e90059d7106` | Last known audited baseline before the remediation chain began. |
| Core remediation merge | GitHub PR `#33` | merge commit `7f7de0a6833b16077e9e4838f7310c5997c4db10` at `2026-04-13T08:27:20Z` | Merged the critical remediation package: gate truthfulness, solution perimeter closure, planner truthfulness, DI hardening, shell retirement, NuGet lane split, and required-status governance. |
| Phase 5 structural cleanup merge | GitHub PR `#38` | merge commit `e86d0aa2667e9b9416ebeeae4e1fbb78cd49a1d9` at `2026-04-13T14:45:00Z` | Merged the structural cleanup follow-up: hotspot file decomposition, narrower hosting nullability suppression, closure refresh, and fitness coverage for the cleanup. |
| Active protected branch governance | GitHub ruleset | `Protect main` (`id 14308867`) with required checks `repo_gate` and `connected_nuget_audit` | Records the server-side governance state that now protects the remediated `main` branch. |

## How To Use This Ledger

1. Treat `36aa39ccbc9ed3feb485085911580e90059d7106` as the canonical audited baseline for remediation traceability.
2. Treat PR `#33` and PR `#38` together as the complete merged remediation chain for the 2026-04-12 audit package.
3. Treat the 2026-04-12 and 2026-04-13 closure documents as narrative summaries, and this ledger as the exact branch/commit mapping.
4. Do not create synthetic historical branches or rewrite existing history to make `Step 0.2` appear to have happened literally.

## Evidence Sources

- `doc/analysis/HELPER_AUDIT_REMEDIATION_CLOSURE_2026-04-12.md`
- GitHub PR `#33`
- GitHub PR `#38`
- GitHub ruleset `Protect main` (`id 14308867`)
