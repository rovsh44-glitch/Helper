# HELPER GitHub Audit

Date: `2026-04-11`

Target repository: `https://github.com/rovsh44-glitch/Helper`

Audited remote state: `origin/main` at commit `0539e7e8573ab9213325f82f7eec4f6b3c71354d`

Repository metadata confirmed via GitHub API:

- visibility: `private`
- default branch: `main`
- archived: `false`

## Scope and method

This audit was performed against the remote GitHub repository, not against the current dirty local worktree.

To avoid contamination from local uncommitted changes, the reviewed source was exported from `origin/main` into an isolated snapshot:

- `C:\Users\rovsh\.codex\memories\remote_audit_origin`

Primary evidence sources:

- GitHub repository metadata API
- GitHub commit status API for `origin/main`
- repository workflow contents under `.github/workflows/`
- direct execution of repo gates and build commands against the isolated `origin/main` export

Actions-tab limitation:

- the repository is private
- the available tools in this environment did not expose the live GitHub Actions run history page
- because of that, the Actions part of this audit is based on repository metadata, workflow YAML, and current commit-status evidence
- this is enough to audit workflow coverage and branch protection exposure, but not enough to enumerate hidden historical runs from the UI

## Executive summary

The remote `main` branch currently has several structural reliability defects:

1. `npm run ci:gate` is red on a clean checkout because `scripts/check_env_governance.ps1` crashes when `.env.local` is absent.
2. `dotnet build Helper.sln` is not a trustworthy solution-wide verification path because five solution-listed test projects are excluded from the build graph.
3. The GitHub Actions surface materially under-covers the repository's own declared gate contract. PRs only run the fast lane, there is no `push` trigger for `main`, and the current `main` head has no attached status contexts.
4. Governance closure on `origin/main` is structurally broken: required archive/comparative artifacts are missing, so both R&D governance and execution-closure checks fail.
5. One governance check also contains a false-negative string test: it rejects a valid relative link in `doc/README.md`.

## Findings

### 1. Critical: `check_env_governance` crashes on a clean repository

Severity: `critical`

Evidence:

- `scripts/check_env_governance.ps1:70-72` reads `.env.local` and immediately evaluates `$localEnvNames.Count`
- direct repro on the isolated `origin/main` export:
  - `powershell -ExecutionPolicy Bypass -File scripts/check_env_governance.ps1`
  - result: `The property 'Count' cannot be found on this object.`
  - failing location: `scripts/check_env_governance.ps1:72`

Relevant code:

- `scripts/check_env_governance.ps1:70-72`
- `scripts/env_inventory_common.ps1:115-138`

Impact:

- a clean clone or fresh CI agent can fail before meaningful governance validation starts
- `npm run ci:gate` is therefore red on `origin/main` even before later gate stages are reached
- this makes the documented full-repo gate non-reproducible in the most common bootstrap scenario

Why this matters:

- a repository-level gate must degrade cleanly when optional local files are absent
- `.env.local` is operator-local state; its absence on CI or on first checkout is normal, not exceptional

### 2. High: `Helper.sln` silently excludes five solution-listed test projects from `dotnet build`

Severity: `high`

Evidence:

- `dotnet sln Helper.sln list` returns `15` project files in the solution
- `dotnet build Helper.sln -m:1 -v:minimal` only emits `11` built projects
- the build output includes transitive `Helper.RuntimeLogSemantics`, but it does not build these solution-listed test projects:
  - `test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj`
  - `test/Helper.Runtime.Integration.Tests/Helper.Runtime.Integration.Tests.csproj`
  - `test/Helper.Runtime.Browser.Tests/Helper.Runtime.Browser.Tests.csproj`
  - `test/Helper.Runtime.Certification.Tests/Helper.Runtime.Certification.Tests.csproj`
  - `test/Helper.Runtime.Certification.Compile.Tests/Helper.Runtime.Certification.Compile.Tests.csproj`

Root cause evidence in solution config:

- `Helper.sln:170-199` contains `ActiveCfg` entries for all five projects, but no `Build.0` entries for them
- by contrast, surrounding projects do have `Build.0` mappings, for example `Helper.sln:159-169`

Impact:

- `dotnet build Helper.sln` gives a false green signal
- local verification and any automation that trusts the solution build are missing five test assemblies
- this weakens both developer feedback and any downstream CI lane that assumes the solution file is canonical

Why this matters:

- the solution file advertises those projects as part of the buildable surface
- omitting them through configuration drift is worse than removing them explicitly, because the failure mode is silent

### 3. High: GitHub Actions does not enforce the repository's own gate contract

Severity: `high`

Evidence from workflow surface:

- there is only one workflow file in the repository: `.github/workflows/runtime-test-lanes.yml`
- triggers in `.github/workflows/runtime-test-lanes.yml:3-23` are limited to:
  - `pull_request`
  - `workflow_dispatch`
  - `schedule`
- there is no `push` trigger for `main`
- PR execution is limited to the `fast` job in `.github/workflows/runtime-test-lanes.yml:26-39`
- `integration` only runs on manual dispatch with `run_integration=true` in `.github/workflows/runtime-test-lanes.yml:41-55`
- `certification` and `certification_compile` only run on schedule or manual dispatch in `.github/workflows/runtime-test-lanes.yml:57-85`

Evidence from the repo's own declared gate:

- `scripts/ci_gate.ps1:18-145` defines a much broader quality contract than the workflow actually executes
- that contract includes, among other things:
  - secret scan
  - config governance
  - R&D governance
  - execution step closure
  - docs entrypoints
  - UI API consistency
  - NuGet security gate
  - `dotnet build Helper.sln`
  - eval gate
  - OpenAPI gate
  - generated client diff gate
  - monitoring gate
  - generation parity gates
  - frontend build
  - bundle budget
  - UI smoke and perf checks
  - release baseline capture

Live status evidence:

- GitHub commit-status lookup for `0539e7e8573ab9213325f82f7eec4f6b3c71354d` returned no attached statuses

Impact:

- the Actions tab is not a trustworthy proxy for repository health
- direct updates to `main` are not validated by workflow triggers
- even PR validation covers only a narrow subset of the repository's own declared release criteria
- a green PR lane, if present, would still not prove the gate contract defined by `scripts/ci_gate.ps1`

Why this matters:

- the repository documents `npm run ci:gate` as the full repo sweep
- the hosted automation does not execute that declared contract
- this creates a governance gap between "what the repo says is required" and "what GitHub actually enforces"

### 4. High: R&D governance and execution-closure gates are structurally red on `origin/main`

Severity: `high`

Evidence:

- `powershell -ExecutionPolicy Bypass -File scripts/check_rd_governance.ps1` fails on `origin/main`
- `powershell -ExecutionPolicy Bypass -File scripts/check_execution_step_closure.ps1` also fails on `origin/main`
- the path `doc/archive/comparative` does not exist in the isolated remote snapshot

Required artifacts hard-coded by the scripts:

- `scripts/check_rd_governance.ps1:26-35` requires:
  - `doc/archive/comparative/HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md`
  - `doc/archive/comparative/HELPER_EXECUTION_DASHBOARD_LFL300_2026-03-22.md`
- `scripts/check_execution_step_closure.ps1:26-30` additionally requires:
  - `doc/archive/comparative/HELPER_EXECUTION_STEP_CLOSURE_LFL300_2026-03-23.json`
  - `doc/archive/comparative/HELPER_EXECUTION_STEP_CLOSURE_LFL300_2026-03-23.md`

Observed failures:

- `check_rd_governance` reported the missing comparative files and a docs-link failure
- `check_execution_step_closure` reported all four missing artifacts and the missing JSON registry

Impact:

- current `origin/main` cannot satisfy its own governance closure checks
- anyone executing the documented full-repo gate will hit deterministic failures unrelated to their local changes
- this weakens trust in the repository's "current green state" claims

Why this matters:

- governance scripts should reflect the artifacts that actually ship with `main`
- if those artifacts are mandatory, they must be versioned with the branch
- if they are historical or external-only, the gates are over-constrained

### 5. Medium: `check_rd_governance` contains a false-negative docs link assertion

Severity: `medium`

Evidence:

- `scripts/check_rd_governance.ps1:107-112` requires `doc/README.md` to contain the literal string `doc/research/README.md`
- `doc/README.md:21-32` already contains a valid relative link:
  - `[Research Governance](research/README.md)`

Impact:

- the check can fail even when the docs index is semantically correct
- this couples governance validity to one exact string form instead of actual link correctness

Why this matters:

- relative links inside `doc/README.md` are the correct repository-local form
- a content validator that rejects valid relative links creates noisy failures and unnecessary churn

### 6. Medium: workflow hardening is minimal

Severity: `medium`

Evidence:

- `.github/workflows/runtime-test-lanes.yml` contains no explicit `permissions:` block
- `.github/workflows/runtime-test-lanes.yml` contains no `concurrency:` block
- the workflow sets up only `.NET`, not Node, even though the repo defines a frontend build and frontend gates in `scripts/ci_gate.ps1:114-145`

Impact:

- the workflow surface is less explicit than it should be for a private repo with release gates
- redundant or stale runs are not actively cancelled
- frontend regressions can remain invisible to GitHub-hosted automation

## Actions-focused assessment

The repository's Actions surface is currently too narrow to represent repository health.

What is actually enforced from GitHub:

- PRs: only the fast runtime lane
- manual runs: optional integration and optional certification lanes
- nightly schedule: certification lanes

What is not enforced from GitHub, despite being part of the repo's own gate contract:

- config governance
- R&D governance
- execution closure
- docs entrypoints
- OpenAPI contract drift
- generated client drift
- monitoring config validation
- parity gates
- frontend build
- bundle budget
- UI smoke and perf regression checks

Practical conclusion:

- even if the Actions tab shows green for visible workflows, that is not sufficient evidence that `main` satisfies the repository's documented release and governance contract

## Reproduction notes

Commands executed against the isolated `origin/main` snapshot:

```powershell
git rev-parse origin/main
dotnet sln Helper.sln list
dotnet build Helper.sln -m:1 -v:minimal
powershell -ExecutionPolicy Bypass -File scripts/check_env_governance.ps1
powershell -ExecutionPolicy Bypass -File scripts/check_rd_governance.ps1
powershell -ExecutionPolicy Bypass -File scripts/check_execution_step_closure.ps1
powershell -ExecutionPolicy Bypass -File scripts/check_root_layout.ps1
powershell -ExecutionPolicy Bypass -File scripts/check_docs_entrypoints.ps1
powershell -ExecutionPolicy Bypass -File scripts/check_ui_api_usage.ps1
```

Observed pass/fail summary:

- passed:
  - `check_root_layout.ps1`
  - `check_docs_entrypoints.ps1`
  - `check_ui_api_usage.ps1`
  - `dotnet build Helper.sln -m:1 -v:minimal` as a command, but with incomplete project coverage
- failed:
  - `check_env_governance.ps1`
  - `check_rd_governance.ps1`
  - `check_execution_step_closure.ps1`

## Limitations

- live GitHub Actions run-history pages were not directly enumerable in this environment because the repository is private and the available tools did not expose the UI run list
- the GitHub commit-status API did return useful evidence: current `main` head had no attached statuses
- `scripts/nuget_security_gate.ps1` failed in this environment because external NuGet vulnerability data was unreachable; that result was treated as environment-limited and is not counted here as a repository defect

## Bottom line

`origin/main` is not presently in a state where the GitHub surface, the documented repo gate, and the actual branch contents agree with each other.

The most important repair order is:

1. make `check_env_governance` null-safe on clean clones
2. restore missing `Build.0` mappings for all solution-listed test projects
3. reconcile Actions with the real gate contract, starting with `push` coverage for `main` and a hosted `ci:gate` equivalent
4. either restore the required governance artifacts to `main` or relax the hard-coded closure scripts
5. replace brittle substring checks with path-aware docs validation
