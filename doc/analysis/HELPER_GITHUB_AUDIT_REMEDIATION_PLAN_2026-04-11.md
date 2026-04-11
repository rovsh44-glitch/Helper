# HELPER GitHub Audit Remediation Plan

Date: `2026-04-11`

Source audit: `doc/analysis/HELPER_GITHUB_AUDIT_2026-04-11.md`

Target repository: `rovsh44-glitch/Helper`

Remote baseline: `origin/main` at `0539e7e8573ab9213325f82f7eec4f6b3c71354d`

## Bottom-line decision

### Should this repository be switched to `Public` now?

Short answer: `no`.

Current state is not public-ready because:

1. the repository does not currently satisfy its own documented gate contract
2. the GitHub Actions surface is too narrow and does not protect `main`
3. governance closure is red on `origin/main`
4. the canonical solution build is giving a false green signal
5. there is no visible `LICENSE` file in the audited remote snapshot

The repository can be made public later, but not as-is.

## Recommended visibility strategy

Preferred strategy:

1. keep the current repository private until the blocking defects below are closed
2. if public exposure is needed sooner, publish a reviewed public-safe mirror or curated export instead of flipping the operational repo immediately
3. only switch the main repository to `Public` after the public-readiness checklist is green

Why this is the safer path:

- the root README already describes the intended model as a curated public-safe baseline with broader private-core work kept outside the public repo
- that model is reasonable, but the audited `main` branch is still inconsistent with its own CI and governance claims
- making it public before those inconsistencies are fixed would expose a branch that looks more mature than it currently verifies

## Success criteria

The remediation is complete only when all of the following are true on `main`:

1. `npm run ci:gate` passes on a clean checkout
2. `dotnet build Helper.sln` builds every solution-listed project
3. GitHub Actions covers the required PR and `main` validation paths
4. the current `main` head has attached required status contexts
5. governance scripts are green without requiring missing off-branch artifacts
6. the repo has an explicit licensing decision
7. a manual public-safe disclosure review confirms that no operator-only or private-core content is being exposed unintentionally

## Execution order

The order matters. Fix the deterministic blockers first, then repair trust in build and CI, then decide visibility.

1. Restore deterministic local and CI gates
2. Repair the canonical solution build
3. Align GitHub Actions with the actual repo contract
4. Run a public-readiness sweep
5. Decide between `Public main repo` and `public mirror`

## Phase 1: Restore deterministic gates

### 1.1 Fix `check_env_governance` crash on clean clones

Problem:

- `scripts/check_env_governance.ps1:70-72` assumes `$localEnvNames.Count` exists even when `.env.local` is absent
- on `origin/main`, this crashes under `Set-StrictMode -Version Latest`

Implementation:

1. update `scripts/check_env_governance.ps1` to treat `.env.local` as optional
2. normalize `$localEnvNames` to an array before counting
3. make the branch explicitly handle three cases:
   - `.env.local` missing
   - `.env.local` present but empty
   - `.env.local` present with governed variables
4. add a regression test path for this script behavior

Files:

- `scripts/check_env_governance.ps1`
- optionally a new regression harness under `test/` or `scripts/tests/`

Verification:

```powershell
Remove-Item .env.local -ErrorAction SilentlyContinue
powershell -ExecutionPolicy Bypass -File scripts/check_env_governance.ps1
npm run ci:gate
```

Definition of done:

- missing `.env.local` no longer causes a crash
- the script emits either success or actionable findings, never a property-access exception

### 1.2 Fix `check_rd_governance` false-negative link validation

Problem:

- `scripts/check_rd_governance.ps1:107-112` requires the literal substring `doc/research/README.md`
- `doc/README.md` already links correctly using the relative form `research/README.md`

Implementation:

1. replace literal substring validation with a link-aware rule
2. accept either:
   - `research/README.md` inside `doc/README.md`
   - `doc/research/README.md` inside root `README.md`
3. keep the rule semantic:
   - verify that the docs index points to the research governance entry
   - do not enforce one exact textual path form where relative navigation is valid

Files:

- `scripts/check_rd_governance.ps1`

Verification:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/check_rd_governance.ps1
```

Definition of done:

- `doc/README.md` is accepted when it contains the valid relative link already used in the repository

### 1.3 Reconcile governance scripts with the actual branch contents

Problem:

- `scripts/check_rd_governance.ps1`
- `scripts/check_execution_step_closure.ps1`

currently require files under `doc/archive/comparative/...`, but that tree is missing on `origin/main`.

Decision required:

Choose one model and apply it consistently:

1. `Artifacts are mandatory on main`
2. `Artifacts are historical and should not gate main`

Recommended choice:

- if these artifacts are required to prove current release/governance state, commit them to `main`
- if they are historical evidence or private-only closure material, remove them from hard gate requirements for `main`

Implementation if artifacts are mandatory:

1. restore `doc/archive/comparative/`
2. add the four required files
3. ensure the JSON and markdown formats match the script expectations

Implementation if artifacts are not mandatory:

1. relax both scripts to distinguish:
   - required active governance artifacts
   - optional archived comparative evidence
2. move the archive checks into a separate non-blocking historical validator or an archival CI lane

Files:

- `scripts/check_rd_governance.ps1`
- `scripts/check_execution_step_closure.ps1`
- optionally `doc/archive/comparative/...`

Verification:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/check_rd_governance.ps1
powershell -ExecutionPolicy Bypass -File scripts/check_execution_step_closure.ps1
```

Definition of done:

- both scripts are green on `main`
- neither script depends on artifacts that are absent from the audited branch by design

## Phase 2: Repair the canonical solution build

### 2.1 Restore missing `Build.0` mappings in `Helper.sln`

Problem:

- the following projects are listed in `Helper.sln`, but `Helper.sln:170-199` only gives them `ActiveCfg` entries, not `Build.0`
- affected projects:
  - `test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj`
  - `test/Helper.Runtime.Integration.Tests/Helper.Runtime.Integration.Tests.csproj`
  - `test/Helper.Runtime.Browser.Tests/Helper.Runtime.Browser.Tests.csproj`
  - `test/Helper.Runtime.Certification.Tests/Helper.Runtime.Certification.Tests.csproj`
  - `test/Helper.Runtime.Certification.Compile.Tests/Helper.Runtime.Certification.Compile.Tests.csproj`

Implementation:

1. add `Build.0` entries for `Debug|Any CPU`
2. add `Build.0` entries for `Release|Any CPU`
3. keep the configuration pattern consistent with the surrounding solution entries
4. verify that `dotnet build Helper.sln` now emits those projects

Files:

- `Helper.sln`

Verification:

```powershell
dotnet sln Helper.sln list
dotnet build Helper.sln -m:1 -v:minimal
```

Definition of done:

- every solution-listed project participates in the canonical solution build

### 2.2 Add a guard against future solution drift

Problem:

- the original defect is silent; nothing prevents future `ActiveCfg-without-Build.0` drift

Implementation:

1. add a lightweight validation script that parses `Helper.sln`
2. fail if a solution-listed buildable project lacks expected `Build.0` mappings
3. wire that script into the repo gate and GitHub Actions

Files:

- `scripts/check_solution_build_coverage.ps1` or equivalent
- `scripts/ci_gate.ps1`
- `.github/workflows/...`

Verification:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/check_solution_build_coverage.ps1
```

Definition of done:

- the solution-build coverage defect becomes mechanically detectable

## Phase 3: Align GitHub Actions with the actual repo contract

### 3.1 Add `push` coverage for `main`

Problem:

- `.github/workflows/runtime-test-lanes.yml:3-23` has no `push` trigger

Implementation:

1. add a `push` trigger for `main`
2. ensure at least the required mainline validation runs there

Files:

- `.github/workflows/runtime-test-lanes.yml`
- or split into multiple workflow files if the current file becomes overloaded

Definition of done:

- direct updates to `main` are validated by hosted automation

### 3.2 Separate lane workflows by purpose

Recommended workflow model:

1. `pr-fast.yml`
2. `main-gate.yml`
3. `nightly-certification.yml`
4. optional `manual-heavy-lanes.yml`

Rationale:

- PR validation should be fast and deterministic
- mainline validation should enforce the repo's declared quality contract
- nightly and heavy lanes should not be confused with mandatory branch protection

### 3.3 Introduce a hosted `repo-gate` job

Minimum hosted gate for PRs:

1. root layout
2. secret scan
3. env governance
4. docs entrypoints
5. UI API consistency
6. solution build coverage check
7. canonical `dotnet build Helper.sln`
8. targeted test lanes that are cheap enough for PRs

Minimum hosted gate for `main`:

1. everything from the PR gate
2. `check_rd_governance`
3. `check_execution_step_closure`
4. frontend build
5. OpenAPI and generated-client drift checks
6. any other deterministic gates that do not require external operator infrastructure

Heavy or environment-bound jobs:

1. parity gates
2. load/chaos smoke
3. UI smoke/perf
4. release baseline capture

These can stay in scheduled or manual workflows if they depend on configured infrastructure.

### 3.4 Add workflow hardening

Implementation:

1. add explicit `permissions:` blocks
2. add `concurrency:` to cancel superseded runs
3. add `actions/setup-node` where frontend checks are required
4. pin toolchain versions deliberately

Definition of done:

- workflow behavior is explicit, reproducible, and separated by enforcement role

### 3.5 Make status checks visible and required

Problem:

- current `main` head had no attached status contexts

Implementation:

1. ensure each required workflow publishes stable check names
2. configure branch protection to require them
3. verify that new `main` commits show status contexts in GitHub

Definition of done:

- a commit to `main` has visible, reviewable CI status attached to it

## Phase 4: Public-readiness sweep

### 4.1 Decide the license before going public

Problem:

- no `LICENSE` file was visible in the audited remote snapshot

Impact:

- a public repository without an explicit license is visible but legally ambiguous for reuse

Implementation:

1. choose the intended license
2. add the `LICENSE` file
3. ensure README and contributing docs are consistent with that licensing choice

Definition of done:

- the repository has an explicit licensing posture suitable for public visibility

### 4.2 Re-run disclosure and secrets review

Current evidence:

- `scripts/secret_scan.ps1` passed on the audited snapshot

Still required before public:

1. manual review of `.env.local.example`
2. manual review of `doc/operator/`, `doc/security/`, `doc/research/`, and archived trees
3. review for private-core leakage, internal hostnames, operator-only instructions, and non-public evidence references
4. verify that files described as operator-local truly remain untracked and template-only

Definition of done:

- there is a written sign-off that the public branch is public-safe, not just secret-scan-clean

### 4.3 Verify public-facing narrative against actual repo state

Current README posture:

- `README.md:41-54` says there is a prepared public-facing pack
- `README.md:107-111` says the recommended model is a curated public-safe baseline

Implementation:

1. verify every public-facing link and doc
2. remove or rewrite claims that imply stronger verification than the branch currently proves
3. ensure the public README does not overstate CI maturity or parity state

Definition of done:

- the public narrative matches what the branch can actually demonstrate

### 4.4 Validate community surfaces

Before making the repo public, verify:

1. `SECURITY.md`
2. `CONTRIBUTING.md`
3. `CONTACT.md`
4. issue and PR templates
5. support expectations and response model

Definition of done:

- public users know how to report bugs, security issues, and contribution proposals

## Phase 5: Go / No-Go decision

### Go public only if all of these are true

1. `npm run ci:gate` passes on clean checkout, or the repo has an explicitly redefined hosted gate that is fully green
2. `dotnet build Helper.sln` covers all solution-listed projects
3. required GitHub Actions checks run on PRs and on `main`
4. `main` shows attached successful status contexts
5. governance scripts are green without private-only artifacts
6. licensing is explicit
7. disclosure review is signed off

### If any of these remain false

Do not switch the operational repo to `Public`.

Use one of these alternatives instead:

1. keep the repo private and continue remediation
2. publish a reviewed public mirror
3. publish a release snapshot branch with a reduced, public-safe surface

## Immediate action list

This is the shortest path to a materially better state:

1. fix `scripts/check_env_governance.ps1`
2. fix `scripts/check_rd_governance.ps1` link validation
3. decide whether `doc/archive/comparative/*` is required on `main`
4. repair `Helper.sln` `Build.0` coverage for the five missing test projects
5. add a solution-coverage guard script
6. add `push` validation for `main`
7. add a hosted deterministic `repo-gate` workflow
8. add a `LICENSE`
9. run a public-safe disclosure review
10. only then revisit the `Public` switch

## Recommended decision today

Today the correct decision is:

- `keep the repository private for now`

If public exposure is needed before all remediation is complete:

- `publish a curated public mirror, not the current operational main repo`
