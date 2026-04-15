# GitHub Private Repo Security Automation Status

Status: `active`
Date: `2026-04-13`
Repository: `rovsh44-glitch/Helper`

## Purpose

This note records which GitHub-native security features are enabled for the current private repository and which features are blocked by the current platform tier.

## Enabled

1. Dependabot vulnerability alerts are enabled.
   Verification:
   - `GET /repos/rovsh44-glitch/Helper/vulnerability-alerts` -> `204 No Content`
   - `GET /repos/rovsh44-glitch/Helper/dependabot/alerts?per_page=1` now returns live alerts
2. Automated security fixes are enabled.
   Verification:
   - `GET /repos/rovsh44-glitch/Helper/automated-security-fixes` -> `{"enabled":true,"paused":false}`

## Current Signal

1. Dependabot is already reporting a live dependency advisory for `vite` from `package-lock.json`.
2. Dependency risk detection is therefore active on the GitHub side and no longer depends only on local scripts.

## Not Available On The Current Repository Tier

1. Secret scanning is not available for this private repository.
   Evidence:
   - `GET /repos/rovsh44-glitch/Helper/secret-scanning/alerts?per_page=1` -> `404 Secret scanning is disabled on this repository`
   - `PATCH /repos/rovsh44-glitch/Helper` with `security_and_analysis.secret_scanning=status=enabled` -> `422 Secret scanning is not available for this repository`
2. Advanced Security is not purchased for this repository.
   Evidence:
   - `PATCH /repos/rovsh44-glitch/Helper` with `security_and_analysis.advanced_security=status=enabled` -> `422 Advanced security has not been purchased`
3. Code scanning is not enabled and cannot be treated as an already-available control in the current private-repo posture.
   Evidence:
   - `GET /repos/rovsh44-glitch/Helper/code-scanning/default-setup` -> `403 Code scanning is not enabled for this repository`
   - private-repo `advanced_security` activation is rejected, so code-scanning enablement is governed as a platform limitation until the security tier changes
4. Private vulnerability reporting is not available in the current repository mode.
   Evidence:
   - `GET /repos/rovsh44-glitch/Helper/private-vulnerability-reporting` -> `404 Not Found`

## Compensating Controls

1. The repository declares the required status contexts for `main` in `.github/branch-protection.required-status-checks.json` and validates the declaration with `scripts/check_required_status_contract.ps1`.
2. Those contexts are attached server-side to the active GitHub `Protect main` ruleset.
3. The enforced required contexts are:
   - `repo_gate`
   - `connected_nuget_audit`
4. `scripts/nuget_security_gate.ps1` runs locally through `scripts/ci_gate.ps1` in `best-effort-local` mode and remotely through a dedicated connected GitHub Actions workflow in `strict-online` mode.
5. The connected NuGet audit workflow uploads its JSON report as a CI artifact instead of coupling network-only vulnerability metadata retrieval to the offline-oriented main repo gate.
6. The operator runbook for reattachment or ruleset recovery remains at `doc/operator/GITHUB_BRANCH_PROTECTION_REQUIRED_STATUS_CHECKS_2026-04-13.md`.
7. `scripts/secret_scan.ps1` remains the repository-owned secret hygiene control.
8. `scripts/check_ui_api_usage.ps1`, frontend architecture checks, and deterministic runtime lanes remain part of the CI boundary.
9. The repository keeps public/private disclosure controls under `doc/security/`.

## Owner

The repository admin for this posture is `rovsh44-glitch`.

## Operational Meaning

1. GitHub-native dependency risk signals are active.
2. Secret scanning and code scanning for this private repository require a different GitHub security tier or a repository visibility change before they can be treated as enabled controls.
3. Until that tier changes, repo-owned security gates remain mandatory and are not optional hygiene extras.

## 2026-04-15 Revalidation

Status: `still current with caveats`

The repository has been returned to `Private`, so this document again matches the actual repository visibility.

Current platform limitation remains:

1. Code scanning alerts are disabled for this user-owned private repository.
2. Advanced Security is not available on the current account/tier.
3. Code scanning should not be represented as an active security control for the private-core repository.

Do not solve that limitation by making the full private-core `Helper` repository public. Direct publication of this source tree conflicts with the active public-showcase boundary policy.

Correct publication/security split:

1. Keep `rovsh44-glitch/Helper` as the private-core repository.
2. Keep Dependabot alerts, automated security fixes, `repo_gate`, `connected_nuget_audit`, and repo-owned scans as the private-core controls.
3. Create a separate public showcase/proof-bundle repository for reviewed public-safe material, especially the reproducible LFL20 proof bundle and narrative docs.
4. If code scanning is required for private-core source, move to an eligible GitHub security tier or eligible organization/enterprise setup instead of widening source visibility.

## 2026-04-15 Implementation Update

Status: `implemented for LFL20 public proof`

The separate public proof repository has been created:

- `https://github.com/rovsh44-glitch/helper-proof-bundle-lfl20`

Current private-core security baseline:

1. `repo_gate` on private `main` commit `8cc34c2`: `success`.
2. `connected_nuget_audit` on private `main` commit `8cc34c2`: `success`.
3. Dependabot dynamic jobs on private `main` commit `8cc34c2`: `success`.
4. `npm audit --json` on current local `main`: `0` vulnerabilities.
5. Magick.NET runtime dependency was removed from the private-core security boundary.

This means the absence of GitHub code scanning on the private repository remains a known tier limitation, but it is no longer blocking the current dependency-security or public-proof publication path.
