# HELPER Project And GitHub Remediation Backlog

Date: `2026-04-12`
Source plan: `doc/analysis/HELPER_PROJECT_AND_GITHUB_REMEDIATION_PLAN_2026-04-12.md`
Source audit: `doc/analysis/HELPER_PROJECT_AND_GITHUB_CRITICAL_AUDIT_2026-04-12.md`

## Execution Model

This backlog is ordered by dependency, not by convenience.

You should not start:

- `Wave 3` before `Wave 1` is merged
- `Wave 4` before `Wave 2` is green
- `Wave 6` before `Wave 4` is finished

Legend:

- `P0`: immediate blocker
- `P1`: high-priority follow-up
- `P2`: important but non-blocking after core health is restored

## Wave 0. Baseline Freeze

### Task W0-T1

- Priority: `P0`
- Goal: freeze the current audit evidence set
- Files:
  - `doc/analysis/HELPER_PROJECT_AND_GITHUB_CRITICAL_AUDIT_2026-04-12.md`
  - `doc/analysis/HELPER_CERTIFICATION_COMPILE_HANG_CAUSAL_AUDIT_2026-04-12.md`
  - `doc/analysis/HELPER_CERTIFICATION_COMPILE_HANG_REMEDIATION_PLAN_2026-04-12.md`
  - `doc/analysis/HELPER_CERTIFICATION_COMPILE_HANG_REMEDIATION_CLOSURE_2026-04-12.md`
- Actions:
  1. keep these files unchanged as the pre-remediation evidence baseline
  2. do not overwrite them during branch cleanup
- Done when:
  - baseline evidence remains readable and unchanged

## Wave 1. Publish Local Compile-Hang Remediation

### Task W1-T1

- Priority: `P0`
- Goal: normalize local git state
- Current issue:
  - local branch `merge-main` tracks `origin/merge-main [gone]`
- Commands:
```powershell
git fetch origin --prune
git checkout -b remediation/certification-compile-hang-and-governance origin/main
```
- Done when:
  - work continues on a live branch based on current `origin/main`

### Task W1-T2

- Priority: `P0`
- Goal: stage the full compile-hang remediation set as one coherent code change
- Files:
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
  - `test/Helper.Runtime.Tests/DotnetServiceTraceBehaviorTests.cs`
  - `test/Helper.Runtime.Tests/TemplatePromotionPipelineTests.cs`
  - `test/Helper.Runtime.Tests/TemplatePromotionEndToEndAndChaosTests.cs`
  - `test/Helper.Runtime.Tests/ArchitectureFitnessTests.RuntimeLanes.cs`
  - `test/Helper.Runtime.Tests/ArchitectureFitnessTests.KnowledgeAndGeneration.cs`
  - `test/Helper.Runtime.CompilePath.Tests/TemplatePromotionCompileSmokeTests.cs`
  - `doc/operator/HELPER_RUNTIME_TEST_LANES_2026-04-01.md`
  - `doc/operator/README.md`
  - `doc/certification/reference/certification_protocol_golden_template.md`
  - `doc/certification/reference/runbook_golden_promotion.md`
- Done when:
  - the complete code/test/doc contract is staged together

### Task W1-T3

- Priority: `P0`
- Goal: keep audit markdown separate from production code commits
- Files:
  - `doc/analysis/HELPER_PROJECT_AND_GITHUB_CRITICAL_AUDIT_2026-04-12.md`
  - `doc/analysis/HELPER_PROJECT_AND_GITHUB_REMEDIATION_PLAN_2026-04-12.md`
  - `doc/analysis/HELPER_PROJECT_AND_GITHUB_REMEDIATION_BACKLOG_2026-04-12.md`
  - `doc/analysis/HELPER_CERTIFICATION_COMPILE_HANG_*.md`
- Action:
  - either commit audit docs separately or keep them out of the main remediation PR
- Done when:
  - code review can focus on runtime and CI changes without audit-noise coupling

### Task W1-T4

- Priority: `P0`
- Goal: prove the branch locally before publish
- Commands:
```powershell
dotnet build Helper.sln -m:1
dotnet build test/Helper.Runtime.CompilePath.Tests/Helper.Runtime.CompilePath.Tests.csproj -m:1
dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj -c Debug -m:1 --no-build --filter "FullyQualifiedName~DotnetServiceTraceBehaviorTests|FullyQualifiedName~TemplatePromotionPipelineTests|FullyQualifiedName~ArchitectureFitnessTests.Runtime_Test_Lanes_Have_Dedicated_Projects_And_Entry_Points|FullyQualifiedName~ArchitectureFitnessTests.Certification_Compile_Dotnet_Tracing_And_Timeout_Policy_Are_Explicit|FullyQualifiedName~ArchitectureFitnessTests.RuntimeAndGenerationCoordinators_StayBehindBoundedCollaborators"
dotnet test test/Helper.Runtime.CompilePath.Tests/Helper.Runtime.CompilePath.Tests.csproj -c Debug -m:1 --no-build --filter "FullyQualifiedName~TemplatePromotionCompileSmokeTests|FullyQualifiedName~TemplateCertificationCompileSmokeTests"
powershell -ExecutionPolicy Bypass -File scripts/run_compile_path_tests.ps1 -Configuration Debug -NoBuild -NoRestore -Filter "FullyQualifiedName~TemplatePromotionCompileSmokeTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"
powershell -ExecutionPolicy Bypass -File scripts/run_certification_compile_tests.ps1 -Configuration Debug -NoBuild -NoRestore -Filter "FullyQualifiedName~TemplatePromotionEndToEndAndChaosTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"
powershell -ExecutionPolicy Bypass -File scripts/run_certification_compile_tests.ps1 -Configuration Debug -NoBuild -NoRestore -EnableBlameHang -BlameHangTimeoutSec 180 -Filter "FullyQualifiedName~TemplatePromotionEndToEndAndChaosTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"
powershell -ExecutionPolicy Bypass -File scripts/check_certification_compile_lock_wait.ps1 -Configuration Debug
```
- Done when:
  - all commands pass

### Task W1-T5

- Priority: `P0`
- Goal: prove controlled-failure teardown behavior before publish
- Command:
```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_certification_compile_tests.ps1 -Configuration Missing -NoBuild -NoRestore -Filter "FullyQualifiedName~TemplatePromotionEndToEndAndChaosTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"
```
- Check:
  - wrapper exits non-zero
  - preserved run root exists
  - `teardown_summary.json` exists
  - root process does not survive
- Done when:
  - teardown contract is demonstrated on failure, not only on success

### Task W1-T6

- Priority: `P0`
- Goal: publish the branch
- Commands:
```powershell
git add <remediation-files>
git commit -m "Fix certification compile hang contract and publish diagnostics"
git push origin remediation/certification-compile-hang-and-governance
```
- Done when:
  - a reviewable remote branch exists with only the intended remediation scope

## Wave 2. Make Remote `certification_compile` Green

### Task W2-T1

- Priority: `P0`
- Goal: open a PR with only the compile-hang remediation
- Target:
  - base: `main`
  - head: `remediation/certification-compile-hang-and-governance`
- Done when:
  - PR is open and reviewable

### Task W2-T2

- Priority: `P0`
- Goal: validate deterministic hosted checks on the PR
- Required checks:
  - `repo-gate`
- Commands:
```powershell
gh pr checks <pr-number> --watch
gh run list --repo rovsh44-glitch/Helper --limit 10
```
- Done when:
  - `repo-gate` is green on the PR

### Task W2-T3

- Priority: `P0`
- Goal: validate hosted compile lane directly on the remediation PR branch
- Actions:
  1. trigger `runtime-test-lanes` with `run_certification_compile=true`
  2. watch the run to completion
- Commands:
```powershell
gh workflow run runtime-test-lanes --repo rovsh44-glitch/Helper -f run_certification_compile=true
gh run list --repo rovsh44-glitch/Helper --limit 10
gh run watch <run-id> --repo rovsh44-glitch/Helper --exit-status
```
- Done when:
  - `certification_compile` is green on the PR branch

### Task W2-T4

- Priority: `P0`
- Goal: merge and prove the fix on real `main`
- Actions:
  1. merge the PR
  2. manually run `runtime-test-lanes` on `main` with `run_certification_compile=true`
  3. verify that `main` is green, not only the PR branch
- Commands:
```powershell
gh pr merge <pr-number> --squash --delete-branch
gh workflow run runtime-test-lanes --repo rovsh44-glitch/Helper -f run_certification_compile=true
gh run watch <run-id> --repo rovsh44-glitch/Helper --exit-status
```
- Done when:
  - `main` passes the compile lane on a post-merge run

### Task W2-T5

- Priority: `P0`
- Goal: verify next scheduled `runtime-test-lanes` is green
- Check:
  - next nightly scheduled run on `main`
- Done when:
  - a real scheduled run, not just a manual rerun, completes successfully

## Wave 3. Make Hosted Failures Diagnostically Readable

### Task W3-T1

- Priority: `P1`
- Goal: make wrapper logs self-sufficient on GitHub
- Files:
  - `scripts/run_certification_compile_tests.ps1`
- Required output on failure:
  - run id
  - run root
  - trace path
  - explicit failure message
  - teardown result
  - preserved diagnostic location
  - if possible, failing test or last relevant trace event
- Done when:
  - GitHub logs show enough context to start diagnosis without reproducing locally

### Task W3-T2

- Priority: `P1`
- Goal: upload compile-lane artifacts on workflow failure
- File:
  - `.github/workflows/runtime-test-lanes.yml`
- Add:
  - artifact upload step conditioned on compile-lane failure
- Candidate paths:
  - `test/Helper.Runtime.Certification.Compile.Tests/bin/Debug/runs/**`
  - or isolated CI output root if changed
- Done when:
  - failed workflow runs expose diagnostic artifacts in Actions UI

### Task W3-T3

- Priority: `P1`
- Goal: validate the new hosted diagnostic contract
- Method:
  - use a controlled failing branch or temporary workflow dispatch condition
- Done when:
  - failure artifacts and failure summary are both visible in GitHub Actions

## Wave 4. Enforce Quality On `main`

### Task W4-T1

- Priority: `P1`
- Goal: add required checks enforcement to `main`
- Current gap:
  - ruleset exists, but required checks do not
- Minimum required check:
  - `repo_gate`
- Recommended later additions:
  - `certification_compile`
  - `certification`
- Done when:
  - GitHub blocks merge if required checks are red or missing

### Task W4-T2

- Priority: `P1`
- Goal: require PR review and resolved conversations
- Remote settings:
  - ruleset or classic protection, depending on available feature surface
- Required:
  - PR before merge
  - at least one review
  - conversation resolution
- Done when:
  - direct quality bypass on `main` is no longer possible through normal UI flow

### Task W4-T3

- Priority: `P1`
- Goal: document the remote merge contract
- Files:
  - `README.md`
  - `doc/operator/README.md`
  - `doc/security/README.md`
- Must document:
  - required checks
  - merge expectations
  - bypass policy
  - meaning of scheduled red vs deterministic green
- Done when:
  - governance is explicit, not tribal knowledge

## Wave 5. Enable GitHub-Native Security Automation

### Task W5-T1

- Priority: `P1`
- Goal: enable secret scanning if available for the current repo/plan
- Remote target:
  - secret scanning endpoint must no longer return disabled
- Done when:
  - GitHub secret scanning is active or a documented platform limitation is recorded

### Task W5-T2

- Priority: `P1`
- Goal: enable Dependabot alerts and security updates
- Remote target:
  - Dependabot alerts endpoint must no longer return disabled
- Also configure:
  - update cadence
  - supported ecosystems
- Done when:
  - hosted dependency risk signals are active

### Task W5-T3

- Priority: `P1`
- Goal: enable code scanning or explicitly document why it cannot be enabled
- Files if fallback required:
  - `doc/security/*`
- Must include:
  - feature status
  - owner
  - compensating controls
- Done when:
  - code scanning is active or the missing coverage is explicitly governed

## Wave 6. Local And Remote Convergence

### Task W6-T1

- Priority: `P1`
- Goal: clean up dead branch tracking and converge local state with merged `main`
- Commands:
```powershell
git fetch origin --prune
git checkout main
git pull --ff-only origin main
```
- Done when:
  - local working branch tracks a live upstream

### Task W6-T2

- Priority: `P1`
- Goal: verify the project from a clean checkout of updated `main`
- Commands:
```powershell
git status --short --branch
dotnet build Helper.sln -m:1
dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj -c Debug -m:1 --no-build --filter "FullyQualifiedName~DotnetServiceTraceBehaviorTests|FullyQualifiedName~TemplatePromotionPipelineTests"
dotnet test test/Helper.Runtime.CompilePath.Tests/Helper.Runtime.CompilePath.Tests.csproj -c Debug -m:1 --no-build --filter "FullyQualifiedName~TemplatePromotionCompileSmokeTests|FullyQualifiedName~TemplateCertificationCompileSmokeTests"
powershell -ExecutionPolicy Bypass -File scripts/run_compile_path_tests.ps1 -Configuration Debug -NoBuild -NoRestore -Filter "FullyQualifiedName~TemplatePromotionCompileSmokeTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"
powershell -ExecutionPolicy Bypass -File scripts/run_certification_compile_tests.ps1 -Configuration Debug -NoBuild -NoRestore -Filter "FullyQualifiedName~TemplatePromotionEndToEndAndChaosTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"
```
- Done when:
  - fresh local checkout reproduces the claimed healthy state

### Task W6-T3

- Priority: `P1`
- Goal: separate engineering evidence from ambiguous untracked residue
- Rules:
  - audit docs stay under `doc/analysis`
  - runtime/generated evidence stays in its evidence trees
  - no stray local residue should distort repo hygiene or audits
- Done when:
  - `git status` reflects intentional files only

## Wave 7. Heavy Gate Structural Fix

### Task W7-T1

- Priority: `P2`
- Goal: make `ci_gate_heavy.ps1` generate fresh parity evidence first
- Files:
  - `scripts/ci_gate_heavy.ps1`
  - `scripts/run_parity_golden_batch.ps1`
  - related parity scripts if needed
- Required order:
  1. generate fresh parity workload evidence
  2. run parity gate
  3. run parity benchmark
- Done when:
  - heavy gate no longer evaluates stale parity history by default

### Task W7-T2

- Priority: `P2`
- Goal: decouple same-day heavy runs from 7-day window requirements
- Files:
  - `scripts/ci_gate_heavy.ps1`
  - `scripts/run_generation_parity_window_gate.ps1`
  - docs for heavy certification behavior
- Options:
  - run window gate only in scheduled mode
  - or skip it unless a valid window already exists
- Done when:
  - same-day heavy closure is not structurally impossible

## Wave 8. License Metadata Normalization

### Task W8-T1

- Priority: `P2`
- Goal: make repository license intent explicit and detectable
- Current issue:
  - GitHub reports `Other` / `NOASSERTION`
- Files:
  - `LICENSE`
  - `README.md`
  - any legal/distribution doc if needed
- Done when:
  - repository API reflects the intended license posture clearly enough for future distribution decisions

## Cross-Wave Verification Commands

### GitHub checks

```powershell
gh run list --repo rovsh44-glitch/Helper --limit 20
gh run view <run-id> --repo rovsh44-glitch/Helper
gh run view <run-id> --repo rovsh44-glitch/Helper --log-failed
gh pr list --repo rovsh44-glitch/Helper --state all --limit 20
gh api repos/rovsh44-glitch/Helper/branches/main
gh api repos/rovsh44-glitch/Helper/rulesets
```

### Security endpoints

```powershell
gh api repos/rovsh44-glitch/Helper/secret-scanning/alerts?per_page=1
gh api repos/rovsh44-glitch/Helper/dependabot/alerts?per_page=1
gh api repos/rovsh44-glitch/Helper/code-scanning/alerts?per_page=1
```

## Final Closure Criteria

This backlog is complete only when all of the following are true:

1. The compile-hang remediation is merged into `remote main`.
2. `runtime-test-lanes` is green for both manual and scheduled `main` runs.
3. GitHub Actions exposes enough diagnostics to understand compile-lane failures without local reproduction.
4. `main` enforces required checks and review quality, not only linear history.
5. Security automation is active where available, and explicit fallback governance exists where not.
6. A fresh clean checkout of `main` reproduces the claimed healthy state locally.
7. `ci_gate_heavy.ps1` no longer depends on stale parity evidence semantics.
