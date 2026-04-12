# HELPER Project And GitHub Critical Audit

Date: `2026-04-12`
Repository: `https://github.com/rovsh44-glitch/Helper`
Audit scope:

- local project state in the current workspace root
- remote GitHub repository `rovsh44-glitch/Helper`
- GitHub Actions, branch governance, and security automation posture

## Snapshot

### Local workspace

- branch: `merge-main`
- upstream: `origin/merge-main [gone]`
- working tree: dirty
- local remediation gap against remote `main`: `22` tracked modified files plus new remediation artifacts and runtime files

### Remote repository

- visibility: `private`
- default branch: `main`
- remote `main` HEAD: `b4ec4183885d8047c5584ce403bdd3e36e6bc687`
- latest successful `repo-gate` push run: `24285458028`
- latest scheduled `runtime-test-lanes` run: `24298891660`
- latest scheduled `runtime-test-lanes` conclusion: `failure`

### Remote workflows

- active workflows: `repo-gate`, `runtime-test-lanes`
- open PRs: none
- most recent merged PR: `#30`

## What Is Healthy

1. `repo-gate` on `main` is green at run `24285458028`, so the deterministic gate currently enforced by the main push workflow is passing on remote `main`.
2. The repository has an active default-branch ruleset and linear-history enforcement, so `main` is not fully ungoverned.
3. Remote `main` head commit `b4ec4183885d8047c5584ce403bdd3e36e6bc687` is verified and signed by GitHub.
4. Local compile-hang remediation work now builds and passes focused verification locally, but it is not yet published to remote `main`.

## Critical Findings

### 1. `main` is operationally red on scheduled Actions because `runtime-test-lanes` still fails on `certification_compile`

Severity: `high`

Facts:

- scheduled run `24298891660` on `main` failed on `2026-04-12`
- failed job: `certification_compile`
- failed job id: `70948609620`
- sibling `certification` job in the same run passed

Impact:

- `main` is not nightly-healthy
- heavy compile-lane regressions are currently unresolved on the remote branch users actually consume
- remote operational confidence is materially lower than the green `repo-gate` signal suggests

Evidence:

- `runtime-test-lanes` workflow history shows consecutive scheduled failures on `main`
- run log for `24298891660` ends with:
  - wrapper startup lines
  - cleanup lines
  - generic `Process completed with exit code 1`

Root cause chain:

1. `repo-gate` validates only the deterministic subset.
2. `runtime-test-lanes` scheduled run still exercises `certification_compile`.
3. Remote `main` does not yet contain the newer compile-hang remediation currently present only in the dirty local worktree.
4. Therefore the repository shows a split-brain condition: deterministic gate green, scheduled compile lane red.

### 2. The failing remote compile lane is still diagnostically opaque

Severity: `high`

Facts:

- the failed log for run `24298891660` does not expose the inner test failure
- the visible failure contract remains a generic wrapper-level `exit code 1`
- the log does not surface a precise failing test, a preserved root cause, or an uploaded diagnostic artifact

Impact:

- GitHub Actions is still not an effective primary diagnostic surface for compile-lane failures
- when scheduled failures occur, operators must reproduce locally to understand them
- MTTR stays high even when the failure is deterministic

Why this matters:

- this exact observability defect was already identified earlier in the project’s own remediation planning
- the latest remote scheduled failure proves the defect still exists on published `main`

### 3. Branch governance is incomplete: `main` has linear-history protection, but no required checks or PR-review enforcement

Severity: `high`

Facts:

- active ruleset: `Protect main` (`id 14308867`)
- enforced rules:
  - `deletion`
  - `non_fast_forward`
  - `required_linear_history`
- classic branch protection endpoint for `main` returns:
  - `protected=true`
  - `protection.enabled=false`
  - `required_status_checks=[]`

Impact:

- `main` is protected against destructive history operations, but not against low-quality merges
- GitHub is not enforcing required status checks on `main`
- GitHub is not enforcing PR review as part of the observed ruleset

Operational consequence:

- a future red `repo-gate` or `runtime-test-lanes` run is not itself a merge blocker unless someone manually enforces that process

### 4. Security automation coverage is materially insufficient on the remote repository

Severity: `high`

Facts from GitHub API:

- code scanning alerts endpoint returns `403`: code scanning is not enabled
- Dependabot alerts endpoint returns `403`: Dependabot alerts are disabled
- secret scanning alerts endpoint returns `404`: secret scanning is disabled

Impact:

- there is no active GitHub-native scanning evidence for code vulnerabilities, dependency risk, or leaked secrets
- the repo currently depends on custom scripts and human review rather than layered platform coverage
- for a private repo, this creates a blind spot between local gates and hosted security posture

Audit note:

- this finding is about actual current coverage, not about whether a specific GitHub plan technically permits each feature
- the practical state today is simple: these protections are not active

### 5. Local and remote states are materially divergent, so “project health” and “remote health” are not the same thing

Severity: `high`

Facts:

- local branch tracks a deleted upstream: `merge-main...origin/merge-main [gone]`
- local worktree contains a nontrivial unpublished remediation set
- current local diff includes:
  - wrapper hardening
  - deterministic `DotnetService` target resolution
  - promotion/certification flow changes
  - updated tests and docs

Impact:

- local validation results cannot be treated as evidence about remote `main`
- remote failures may already be locally fixed but remain unresolved for all actual GitHub users and workflows
- any audit or status statement that does not explicitly separate local vs remote is unreliable

## Major Findings

### 6. `ci_gate_heavy.ps1` still has a structural contract defect around parity evidence freshness

Severity: `medium`

Facts from script inspection:

- `scripts/ci_gate_heavy.ps1` runs:
  - `run_generation_parity_gate.ps1`
  - `run_generation_parity_benchmark.ps1`
  - `run_generation_parity_window_gate.ps1`
- it does not first generate fresh parity workload evidence via `run_parity_golden_batch.ps1`

Impact:

- the heavy lane still mixes deterministic orchestration with temporally dependent parity KPIs
- same-day honest success remains structurally difficult or impossible when the parity gate expects fresh evidence in the active window
- the heavy gate still encodes a known semantics problem, even if that problem is outside the green deterministic `repo-gate`

### 7. Actions runtime migration debt is reduced, but not closed

Severity: `medium`

Facts:

- workflow env sets `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true`
- failed job log still emits GitHub’s warning that `actions/checkout@v4` and `actions/setup-dotnet@v4` target Node 20 and are being forced onto Node 24

Impact:

- this is not a current red blocker
- it is still operational drift that should be monitored and eventually removed by updating action versions when maintainers provide fully aligned releases

### 8. License metadata remains ambiguous on GitHub

Severity: `medium`

Facts:

- repository API reports license `Other`
- SPDX identifier is `NOASSERTION`

Impact:

- internal usage may tolerate this
- external redistribution, public mirroring, or contributor expectations remain ambiguous
- this becomes a more serious problem immediately if the repository is later made public

## Local Project Findings

### 9. The local workspace is not in an audit-ready publishable state

Severity: `medium`

Facts:

- multiple modified tracked files
- multiple untracked audit and remediation documents
- new runtime source files not committed

Impact:

- the workspace is suitable for active engineering, not for claiming repository closure
- if left as-is, operator confusion is likely: local fixes exist, remote users do not have them

### 10. The compile-hang remediation is locally credible but still local-only

Severity: `medium`

Facts from local verification:

- `dotnet build Helper.sln -m:1` passed
- focused runtime regression set passed
- compile-path regression set passed
- operational and forensic wrapper checks passed locally
- controlled failure preserved `teardown_summary.json` and cleaned the root process

Impact:

- the project is closer to resolution than the remote repository state suggests
- the key remaining failure is delivery/governance, not only code correctness

## Causal Summary

The project is currently split into two truths:

1. `remote main`
   - deterministic gate green
   - scheduled compile lane red
   - weak hosted diagnostics
   - incomplete governance and security automation

2. `local workspace`
   - contains substantial unpublished remediation
   - proves several failures are already addressed in code
   - is dirty and detached from a live upstream branch, so that remediation is not yet operationally real

This split is the central audit conclusion. The most important defect is no longer a single code bug. It is the mismatch between:

- what the local project already fixes
- what the GitHub repository actually runs and enforces

## Highest-Priority Next Steps

1. Publish the current compile-hang remediation to a reviewable branch and merge it into `main`.
2. Re-run `runtime-test-lanes` on the updated branch and verify that `certification_compile` is both green and diagnostically legible on failure.
3. Strengthen `main` governance so required status checks are enforced, not just observed.
4. Enable the maximum available GitHub-native security automation for this repository and plan any remaining gaps that require higher-tier features.
5. Normalize the local branch state so audits, local verification, and remote status all describe the same codebase.

## Source Evidence Used

- local `git status --short --branch`
- local `git diff --stat`
- local workflow and gate script inspection
- GitHub repository API for `rovsh44-glitch/Helper`
- GitHub Actions workflow inventory
- GitHub Actions runs for `repo-gate` and `runtime-test-lanes`
- GitHub Actions job/log inspection for run `24298891660`
- GitHub ruleset and branch protection endpoints
- GitHub code scanning / Dependabot / secret scanning endpoints
