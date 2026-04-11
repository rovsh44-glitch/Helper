# HELPER Unified Remediation Plan

Date: `2026-04-11`

Inputs:

- `doc/analysis/HELPER_CI_GATE_CAUSAL_REMEDIATION_PLAN_2026-04-11.md`
- `doc/analysis/HELPER_GITHUB_AUDIT_REMEDIATION_PLAN_2026-04-11.md`
- local verification and failure analysis performed on `2026-04-11`

## Purpose

This document consolidates the remaining work into one execution plan with explicit causality.

It separates:

1. work that is already materially complete
2. work that is still blocking local deterministic green state
3. work that is still blocking remote GitHub audit closure

## Current state

The following major remediation areas are already implemented locally:

1. wrong lane ownership in `ci:gate` has been corrected
2. `Eval`, `EvalV2`, `EvalOffline`, `Load`, and `ToolBenchmark` now target real owning projects
3. `Helper.Runtime.Eval.Tests` is in `Helper.sln` and is built by the canonical solution path
4. base `ci:gate` and heavy `ci:gate:heavy` are separated
5. hosted deterministic workflow scaffolding exists in `.github/workflows/repo-gate.yml`
6. `LICENSE` and disclosure-review decision artifacts now exist locally

The following major closure criteria are still not satisfied:

1. local deterministic `ci:gate` is not yet green end-to-end
2. heavy reporting still leaks machine-local absolute paths into the repo working tree
3. current local residue under `doc/` conflicts with fast-lane architecture policy
4. remote GitHub status attachment and branch-protection closure are not yet proven on actual `main`

## Remaining root-cause chain

The remaining blockers are not independent.

### Root cause 1: heavy verification writes generated artifacts into `doc/`

Observed files:

1. `doc/HELPER_PARITY_GATE_2026-04-11_11-13-32.json`
2. `doc/HELPER_PARITY_GATE_2026-04-11_11-13-32.md`
3. `doc/load_streaming_chaos_report.md`

These are generated artifacts, not canonical source docs.

### Root cause 2: those generated files contain machine-local absolute paths

Observed examples:

1. `<workspace-root>`
2. `<helper-data-root>`

The fast-lane architecture guard in `test/Helper.Runtime.Tests/ArchitectureFitnessTests.RepoAndContracts.cs` scans `doc/` and fails when blocked tokens appear in working-tree file contents.

### Root cause 3: deleting only one parity artifact pair is insufficient

Even if:

1. `doc/HELPER_PARITY_GATE_2026-04-11_11-13-32.json`
2. `doc/HELPER_PARITY_GATE_2026-04-11_11-13-32.md`

are removed, `doc/load_streaming_chaos_report.md` still contains an absolute machine-local workspace path and can trigger the same fast-lane failure.

### Root cause 4: evidence preservation and repo cleanliness are currently in tension

The causal remediation plan correctly requires preserving broken-state evidence.

But the current implementation preserves it in a location and format that contaminates the public working tree and breaks deterministic local verification.

So the remaining task is not "delete the files".

It is:

1. preserve evidence
2. relocate or sanitize evidence output
3. keep the public/source working tree clean

### Root cause 5: local completion and GitHub completion are different contracts

Even after local `ci:gate` becomes green, the GitHub audit plan is still incomplete until:

1. workflow changes are pushed
2. hosted checks run on PR and `main`
3. stable status contexts attach to real commits
4. branch protection requires those checks

## Unified target state

The remediation is truly complete only when all of the following are true:

1. deterministic `ci:gate` is green on the local repo
2. heavy scripts no longer generate repo-root or `doc/` residue that breaks fast-lane architecture checks
3. broken-state and heavy-run evidence still exists, but outside the canonical public working tree contract
4. hosted GitHub workflows enforce the same deterministic contract as local `ci:gate`
5. actual `main` commits show attached successful status contexts

## Execution order

The remaining work must be done in this order:

1. preserve and relocate current evidence
2. stop heavy scripts from regenerating dirty `doc/` residue
3. clean current residue from the working tree
4. rerun deterministic local gate
5. rerun heavy gate as a separate contract
6. push workflow changes and verify remote GitHub status attachment

If Step 2 is skipped, Step 3 is only temporary and the same defect returns on the next heavy run.

## Phase 1: Preserve evidence without polluting `doc/`

### Goal

Keep the current broken-state artifacts, but move them out of the canonical working-tree surface scanned by fast-lane architecture rules.

### Required changes

1. define one repo-owned location for generated heavy verification evidence
2. move current residue there
3. keep canonical docs for authored documentation only

### Recommended destination

Use one of:

1. `artifacts/verification/`
2. `temp/verification/`
3. `doc/archive/comparative/` only if those files are meant to be governed historical evidence and not operator-local residue

Recommended choice:

- `artifacts/verification/` for generated heavy-run outputs

Reason:

- it preserves evidence
- it avoids pretending generated run outputs are canonical docs
- it aligns with the repo-hygiene principle that machine-generated run products should not live in the main authored-doc surface

### Files affected

1. `doc/HELPER_PARITY_GATE_2026-04-11_11-13-32.json`
2. `doc/HELPER_PARITY_GATE_2026-04-11_11-13-32.md`
3. `doc/load_streaming_chaos_report.md`

### Definition of done

1. current evidence is still present somewhere in the repo or workspace
2. it no longer lives as ad hoc generated residue in `doc/`

## Phase 2: Stop heavy scripts from recreating the same defect

### Goal

Change the report-output contract for heavy scripts so they do not write machine-local run artifacts into `doc/`.

### Required changes

1. `scripts/load_streaming_chaos.ps1`
   - change default `ReportPath` away from `doc/load_streaming_chaos_report.md`
2. `scripts/run_generation_parity_gate.ps1`
   - change default `ReportPath` away from `doc/HELPER_PARITY_GATE_<timestamp>.md`
3. any related heavy scripts that emit markdown/json snapshots into `doc/`
   - reroute them to `artifacts/verification/` or `temp/verification/`

### Secondary hardening

Where report content still needs path references:

1. prefer repo-relative paths
2. avoid absolute machine-local paths in generated public-tree text

This does not replace relocation; it is additional hardening.

### Files likely involved

1. `scripts/load_streaming_chaos.ps1`
2. `scripts/run_generation_parity_gate.ps1`
3. possibly:
   - `scripts/run_generation_parity_benchmark.ps1`
   - `scripts/run_generation_parity_window_gate.ps1`
   - `scripts/baseline_capture.ps1`

### Definition of done

1. a new heavy run does not recreate ad hoc generated reports under `doc/`
2. generated heavy artifacts do not include blocked machine-local tokens in the public working tree

## Phase 3: Clean current residue from the local working tree

### Goal

After Phase 1 and Phase 2 are complete, remove the current local residue from `doc/`.

### Important constraint

Do not do this first.

If current residue is removed before the output contract is fixed, the same files or equivalent files will be regenerated on the next heavy run.

### Required cleanup set

At minimum:

1. `doc/HELPER_PARITY_GATE_2026-04-11_11-13-32.json`
2. `doc/HELPER_PARITY_GATE_2026-04-11_11-13-32.md`
3. `doc/load_streaming_chaos_report.md`

### Verification

Search after cleanup:

```powershell
Get-ChildItem -Path doc -Recurse -File | Select-String -Pattern '<blocked-token-A>','<blocked-token-B>','<blocked-token-C>'
```

Expected result:

- no generated `doc/` residue containing blocked machine-local brand tokens

### Definition of done

1. fast-lane architecture guards no longer fail because of generated `doc/` residue

## Phase 4: Re-run deterministic local contract

### Goal

Prove that the remaining causal remediation work is actually complete locally.

### Required verification order

1. `dotnet build Helper.sln -m:1`
2. direct owning-project checks:
   - `Category=Load`
   - `Category=ToolBenchmark`
   - `Category=Eval`
   - `Category=EvalV2`
   - `Category=EvalOffline`
3. script-level wrappers:
   - `scripts/run_load_chaos_smoke.ps1`
   - `scripts/run_tool_benchmark.ps1`
   - `scripts/run_eval_gate.ps1`
   - `scripts/run_eval_runner_v2.ps1`
   - `scripts/run_offline_eval.ps1`
4. `powershell -ExecutionPolicy Bypass -File scripts/ci_gate.ps1`

### Expected outcome

1. deterministic `ci:gate` is green
2. no `No test matches`
3. no false `Passed` banners
4. no architecture-test failure caused by generated `doc/` residue

### Definition of done

1. the causal remediation plan is closed locally on its own terms

## Phase 5: Re-run heavy gate as a separate contract

### Goal

Verify that heavy verification is still runnable, but isolated from deterministic repo health.

### Required verification

1. `powershell -ExecutionPolicy Bypass -File scripts/ci_gate_heavy.ps1`

### Evaluation rule

Heavy failures may still occur for real parity, UI-runtime, or operator-infrastructure reasons.

That is acceptable if:

1. the failure is in heavy-only scope
2. deterministic `ci:gate` remains green
3. heavy scripts do not repollute the public working tree

### Definition of done

1. heavy gate remains explicit and separate
2. heavy execution no longer corrupts deterministic local health

## Phase 6: Close the GitHub audit plan on actual remote state

### Goal

Convert local remediation into real remote GitHub closure.

### Required work

1. push the workflow changes
2. ensure `.github/workflows/repo-gate.yml` is active on PR and `main`
3. ensure `.github/workflows/runtime-test-lanes.yml` reflects the updated lane model
4. verify stable hosted check names
5. enable or confirm branch protection required checks
6. confirm actual `main` head shows attached successful status contexts

### Important note

This phase cannot be claimed complete from local code inspection alone.

It requires actual remote GitHub execution and policy confirmation.

### Definition of done

1. the GitHub audit remediation plan is closed on real `main`, not only in the local workspace

## Phase 7: Public-visibility decision remains gated

### Current decision

Keep the repo private until Phase 6 is proven complete.

### Why

Even with local fixes, the public-readiness contract is still incomplete until:

1. deterministic gate is green
2. hosted enforcement is proven
3. disclosure review remains current

## Minimal critical path

If the goal is to reach a truthful green local deterministic state as fast as possible, the shortest correct path is:

1. relocate current generated evidence out of `doc/`
2. reroute heavy report defaults out of `doc/`
3. clean `doc/HELPER_PARITY_GATE_2026-04-11_11-13-32.*`
4. clean `doc/load_streaming_chaos_report.md`
5. rerun `scripts/ci_gate.ps1`

If the goal is to close both plans, continue immediately with remote GitHub verification after that.

## Unified definition of done

This unified remediation is complete only when:

1. the causal `ci:gate` plan is closed locally
2. no generated heavy artifact in `doc/` can re-break fast-lane architecture tests
3. current broken-state evidence is preserved without polluting the public working tree
4. the GitHub audit remediation is proven on actual remote `main`
5. the repository visibility decision remains aligned with verified reality
