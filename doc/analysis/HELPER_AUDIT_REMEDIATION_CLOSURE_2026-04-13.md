# HELPER Audit Remediation Closure Update

Date: `2026-04-13`
Supersedes: `doc/analysis/HELPER_AUDIT_REMEDIATION_CLOSURE_2026-04-12.md`
Source audit: `doc/analysis/HELPER_AUDIT_2026-04-12.md`
Source plan: `doc/analysis/HELPER_AUDIT_REMEDIATION_PLAN_2026-04-12.md`

## Scope Of This Update

This update closes the post-closure follow-up around local NuGet audit behavior, required-status governance, and regression coverage for the OpenAPI contract gate.

## Added After The 2026-04-12 Closure

### 1. Local NuGet audit now short-circuits before noisy restore attempts

- `scripts/nuget_security_gate.ps1` now inspects configured proxy endpoints before restore/list execution
- when `ExecutionMode=best-effort-local` and every configured proxy resolves to an unreachable loopback endpoint, the gate returns:
  - `audit_skipped_local_offline_proxy_unavailable`
- when `ExecutionMode=strict-online` and every configured proxy resolves to an unreachable loopback endpoint, the gate returns:
  - `audit_failed_proxy_misconfigured`

Operational effect:

- local offline or dead-proxy sessions no longer pay the full restore round-trip before the gate reaches an honest degraded result
- connected strict-online runs fail fast when proxy configuration is broken

### 2. NuGet audit warning classification is more precise

- `NU1900` remains mapped to infrastructure-unavailable statuses
- `NU1905` is now tracked separately as audit-source-unavailable:
  - `audit_failed_audit_source_unavailable`
  - `audit_degraded_audit_source_unavailable`
- JSON reports now include `auditWarningCodes`, `preflightStatus`, and `preflightMessage`

### 3. Required status contexts are now declared and enforced in-repo

- manifest added:
  - `.github/branch-protection.required-status-checks.json`
- checker added:
  - `scripts/check_required_status_contract.ps1`
- `scripts/ci_gate.ps1` and `.github/workflows/repo-gate.yml` now validate that the declared required contexts still map to real workflow job ids

Declared required contexts:

- `repo_gate`
- `connected_nuget_audit`

### 4. Connected NuGet audit now publishes a human-readable workflow summary

- `.github/workflows/nuget-security-audit.yml` now writes a step summary containing:
  - final status
  - execution mode
  - required status context name
  - protected branch target
  - audit warning codes

### 5. OpenAPI gate now has direct regression coverage

- `test/Helper.Runtime.Tests/OpenApiGateScriptTests.cs` now covers:
  - no-match filtered runs
  - missing test-source runs
  - clean pass-through runs
- `scripts/openapi_gate.ps1` supports simulated output injection for script-level regression coverage without reintroducing the old false-green path

### 6. The repository now contains an operator runbook for the remaining GitHub admin step

- `doc/operator/GITHUB_BRANCH_PROTECTION_REQUIRED_STATUS_CHECKS_2026-04-13.md` records the exact server-side attachment procedure for:
  - `repo_gate`
  - `connected_nuget_audit`
- `doc/security/GITHUB_PRIVATE_REPO_SECURITY_AUTOMATION_STATUS_2026-04-12.md` now distinguishes in-repo declaration from real remote enforcement

## Verification

The following checks passed for this update:

```powershell
dotnet test .\test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj -m:1 --filter "FullyQualifiedName~NugetSecurityGateScriptTests|FullyQualifiedName~OpenApiGateScriptTests|FullyQualifiedName~ArchitectureFitnessTests" -v:m
dotnet nuget list source --configfile .\NuGet.Config
npm run ci:gate
```

Observed results:

- targeted runtime/fitness verification: `60/60`
- repo-owned `NuGet.Config` resolves `nuget.org` as the sole configured source under `--configfile`
- local `ci:gate` passes after the follow-up update, while the NuGet lane now supports fast offline short-circuit semantics when a dead loopback proxy is detected

## Remaining Boundary

The repository now declares and checks the intended required status contexts locally, but attaching those contexts to the remote GitHub branch protection or ruleset still requires repository-admin action on GitHub itself.
