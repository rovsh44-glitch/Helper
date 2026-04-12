# HELPER Project And GitHub Remediation Plan

Date: `2026-04-12`
Source audit: `doc/analysis/HELPER_PROJECT_AND_GITHUB_CRITICAL_AUDIT_2026-04-12.md`

## Objective

Bring `Helper` and remote repository `rovsh44-glitch/Helper` to a single, consistent, operationally healthy state where:

1. local remediation is published and merged to `main`
2. scheduled `runtime-test-lanes` is green on `main`
3. compile-lane failures are diagnosable from GitHub Actions
4. `main` has meaningful merge governance
5. GitHub-native security automation is enabled where available

## Root-Cause Order

Work must follow the real causal chain from the audit:

1. local and remote states are split
2. remote `main` is still red on `runtime-test-lanes`
3. remote compile-lane logs are not diagnostically useful
4. governance is too weak to enforce quality
5. security automation is too thin to trust hosted posture

That means the order is:

1. converge local and remote code
2. make the failing lane green and observable
3. enforce quality gates on `main`
4. enable security automation

## Phase 0. Freeze The Current State

### Step 0.1. Preserve the audit baseline

Keep these files as the baseline evidence set:

- `doc/analysis/HELPER_PROJECT_AND_GITHUB_CRITICAL_AUDIT_2026-04-12.md`
- `doc/analysis/HELPER_CERTIFICATION_COMPILE_HANG_CAUSAL_AUDIT_2026-04-12.md`
- `doc/analysis/HELPER_CERTIFICATION_COMPILE_HANG_REMEDIATION_PLAN_2026-04-12.md`
- `doc/analysis/HELPER_CERTIFICATION_COMPILE_HANG_REMEDIATION_CLOSURE_2026-04-12.md`

Reason:

- these documents define the before-state and already-proven local remediation evidence

### Step 0.2. Stop treating the dirty local workspace as equivalent to remote `main`

From this point forward, every status statement must explicitly say one of:

- `local worktree`
- `review branch`
- `remote main`

Reason:

- the audit’s biggest systemic defect is state confusion between local and remote

## Phase 1. Converge Local Remediation Into A Publishable Branch

### Step 1.1. Normalize the branch situation

Current problem:

- local branch is `merge-main`
- upstream is gone
- working tree is dirty

Action:

1. fetch and prune remotes
2. create a fresh remediation branch from current `origin/main`
3. bring only the intended local remediation set into that branch

Recommended branch name:

- `remediation/certification-compile-hang-and-governance`

### Step 1.2. Stage the complete local remediation set coherently

Files that must be reviewed and committed together:

- `scripts/run_certification_compile_tests.ps1`
- `scripts/run_compile_path_tests.ps1`
- `src/Helper.Runtime/Infrastructure/Dotnet/DotnetBuildTargetResolver.cs`
- `src/Helper.Runtime/DotnetService.cs`
- `src/Helper.Runtime/Core/Contracts/OperationsContracts.cs`
- `src/Helper.Runtime/LocalBuildExecutor.cs`
- `src/Helper.Runtime/Generation/CompileGateWorkspacePreparer.cs`
- `src/Helper.Runtime/Generation/GenerationCompileGate.cs`
- `src/Helper.Runtime/Generation/GenerationGuardrailsContracts.cs`
- `src/Helper.Runtime/Generation/TemplatePromotionFeatureProfileService.cs`
- `src/Helper.Runtime/Generation/TemplatePostActivationVerifier.cs`
- `src/Helper.Runtime/Generation/GenerationTemplatePromotionService.cs`
- `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Generation.cs`
- updated tests and docs already touched by the compile-hang remediation

Reason:

- this set is one logical contract
- partial publication would create another split-brain failure

### Step 1.3. Keep audit artifacts separate from production code changes

Commit strategy:

1. code and test remediation commit
2. operator/docs contract commit
3. optional audit-doc commit

Reason:

- if the branch must be cherry-picked or partially reverted later, code and audit evidence should not be coupled

## Phase 2. Make Remote `certification_compile` Green

### Step 2.1. Open a PR with only the compile-hang remediation set

Do not mix in unrelated backlog items.

PR scope must include:

- wrapper hardening
- deterministic dotnet target resolution
- default post-activation verification refactor
- regression tests
- operator docs

Reason:

- the failing remote scheduled lane is already known
- the fastest safe fix is a tightly scoped PR that closes that red lane

### Step 2.2. Run targeted hosted validation before merge

Required hosted checks:

1. `repo-gate` on the remediation PR
2. `runtime-test-lanes` with `run_certification_compile=true`
3. if useful, a manual rerun of full `runtime-test-lanes` on the PR branch

Success criteria:

- `repo-gate` green
- `certification_compile` green
- log output now includes enough wrapper context to diagnose a future failure

### Step 2.3. Merge and immediately validate scheduled-lane equivalence on `main`

After merge:

1. trigger `runtime-test-lanes` manually on `main` with `run_certification_compile=true`
2. verify the same green result on the real default branch
3. do not wait for the next nightly to discover regression

Reason:

- `main` health matters more than PR health

### Step 2.4. If `certification_compile` still fails, force root-cause visibility before any further architecture work

If failure remains:

1. make wrapper print the actual failing test or preserved artifact path
2. upload or preserve run-root diagnostics in a deterministic location
3. re-run the lane

Do not continue to governance/security work until this lane is at least diagnostically readable.

## Phase 3. Make Compile-Lane Failures Observable On GitHub

### Step 3.1. Improve wrapper output contract for hosted runs

The wrapper must emit:

- run id
- run root
- trace path
- explicit failure summary
- teardown result
- preserved diagnostics path when artifacts are kept

If inner `dotnet test` fails, the wrapper should also try to print at least one of:

- failing test name
- filtered `TestResults` path
- last known trace event

### Step 3.2. Add artifact publication for the compile lane

In `runtime-test-lanes.yml`, publish failure artifacts from:

- `test/Helper.Runtime.Certification.Compile.Tests/bin/Debug/runs/*`
- or the equivalent isolated output root used in CI

Only on failure is sufficient.

Reason:

- hosted diagnosis should not depend on reproducing locally

### Step 3.3. Keep wrapper cleanup strict even when artifacts are preserved

Do not relax teardown guarantees in order to keep evidence.

Required invariant:

- preserved diagnostics are acceptable
- surviving orphan root `dotnet` is not

## Phase 4. Strengthen `main` Governance

### Step 4.1. Convert quality signals into enforced merge policy

Current gap:

- ruleset exists
- required checks do not

Required remote policy on `main`:

1. require pull request before merge
2. require at least one review
3. require conversation resolution
4. require successful status checks before merge

Minimum required checks:

- `repo_gate`

Strongly recommended additional required checks once compile-lane is stable:

- `certification_compile`
- `certification`

### Step 4.2. Preserve linear history but stop relying on it as the only protection

Keep:

- `required_linear_history`
- `non_fast_forward`
- `deletion`

Add:

- quality-oriented rules, not just history-oriented rules

### Step 4.3. Document the exact merge contract

Update operator/governance docs so that the repository contract is explicit:

- what checks must pass
- whether merge is allowed on red scheduled lanes
- who can bypass rules

Reason:

- undocumented governance devolves into operator memory

## Phase 5. Enable GitHub-Native Security Automation

### Step 5.1. Enable secret scanning

Goal:

- remote repository should no longer return `404` for secret-scanning alerts

If plan or feature availability limits apply:

1. enable what is available now
2. record the exact feature gap in `doc/security`
3. keep custom `secret_scan.ps1` as defense-in-depth, not as the only layer

### Step 5.2. Enable Dependabot alerts and Dependabot security updates

Goal:

- remote repository should no longer return `403` for Dependabot alerts

Also configure:

- dependency update cadence
- security update automation if supported

### Step 5.3. Enable code scanning or explicitly document why it remains unavailable

Goal:

- either code scanning is active
- or there is a written repo-level decision explaining why it is unavailable and what replaces it

Acceptable fallback only if feature cannot be enabled:

- documented alternative pipeline
- explicit owner
- explicit cadence

## Phase 6. Clean Up Local Operational State

### Step 6.1. Stop using a deleted upstream branch as the working base

After merge:

1. move local work to a current tracked branch
2. delete or archive dead local branch names
3. ensure `git status --branch` no longer shows `[gone]`

### Step 6.2. Separate engineering artifacts from audit artifacts

Rules:

- remediation docs stay in `doc/analysis`
- generated runtime evidence stays in evidence trees, not mixed into canonical docs
- local experimental outputs should not remain as ambiguous untracked residue

### Step 6.3. Re-run local deterministic verification from a clean tree

Required post-merge local proof:

1. clean checkout from updated `main`
2. `dotnet build Helper.sln -m:1`
3. targeted runtime regressions
4. compile-path wrapper checks
5. certification compile wrapper checks

Reason:

- the audit must close with local and remote describing the same code

## Phase 7. Fix The Structural Heavy-Gate Defect

This is not the first blocker, but it should not remain open after compile-lane publication.

### Step 7.1. Make `ci_gate_heavy.ps1` generate fresh parity evidence before evaluating parity KPIs

Add ordering so `ci_gate_heavy.ps1` does not check parity against stale history.

Required change:

1. run fresh parity batch generation first
2. then run parity gate and parity benchmark

### Step 7.2. Decouple same-day heavy closure from multi-day window requirements

`run_generation_parity_window_gate.ps1 -WindowDays 7` should not be treated as a same-day closure criterion unless there is already a valid 7-day evidence window.

Options:

1. keep `window gate` only in scheduled certification
2. or make same-day heavy gate skip it unless the window is already complete

## Phase 8. Resolve License Ambiguity

### Step 8.1. Decide the intended license posture

Current GitHub state:

- `Other`
- `NOASSERTION`

Action:

1. choose the intended license
2. ensure the checked-in `LICENSE` matches that intent clearly enough for GitHub detection
3. verify repository API no longer reports `NOASSERTION` if public distribution is expected

Reason:

- this is a medium issue now
- it becomes high immediately if the repository is opened publicly

## Verification Matrix

### Local verification

Required:

1. `git status --short --branch`
   - expected: clean tracked state on a live upstream branch
2. `dotnet build Helper.sln -m:1`
3. targeted runtime tests for dotnet target resolution and promotion flow
4. wrapper operational check
5. wrapper forensic check
6. controlled failure teardown proof

### Remote verification

Required:

1. `repo-gate` green on PR
2. `repo-gate` green on `main`
3. manual `runtime-test-lanes` compile-lane rerun green on `main`
4. next scheduled `runtime-test-lanes` green on `main`
5. required checks enforced on `main`
6. security endpoints no longer return disabled/not-enabled for enabled features

## Definition Of Done

The remediation is complete only when all of the following are true:

1. Local compile-hang remediation is merged into remote `main`.
2. `runtime-test-lanes` no longer fails on `certification_compile` on `main`.
3. Hosted compile-lane failures are diagnostically readable without local reproduction.
4. `main` enforces required checks and PR-quality rules, not only linear history.
5. GitHub-native security automation is enabled wherever available and any unavoidable gaps are explicitly documented.
6. Local and remote states are converged enough that a fresh checkout of `main` reproduces the claimed health.
7. The heavy parity gate contract is no longer structurally dependent on stale evidence.

## Recommended Execution Order

1. Publish local compile-hang remediation
2. Green and instrument `certification_compile` on hosted Actions
3. Enforce required checks and PR rules on `main`
4. Enable GitHub-native security automation
5. Clean local branch state and re-verify from clean `main`
6. Fix heavy parity gate semantics
7. Resolve license ambiguity
