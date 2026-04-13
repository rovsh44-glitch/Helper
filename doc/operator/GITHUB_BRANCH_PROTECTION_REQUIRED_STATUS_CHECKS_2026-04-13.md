# GitHub Branch Protection Required Status Checks

Status: `active`
Date: `2026-04-13`
Repository: `rovsh44-glitch/Helper`
Protected branch: `main`

## Purpose

This runbook describes the repository-admin action required to attach the declared required status contexts to the real GitHub `main` ruleset or branch protection configuration.

Declared source of truth:

- `.github/branch-protection.required-status-checks.json`

Declared required contexts:

- `repo_gate`
- `connected_nuget_audit`

## Preconditions

1. The workflow files are already pushed to GitHub:
   - `.github/workflows/repo-gate.yml`
   - `.github/workflows/nuget-security-audit.yml`
2. Both workflows have completed successfully on a recent commit in the repository.
3. The visible GitHub checks on that commit include:
   - `repo_gate`
   - `connected_nuget_audit`

## Preferred Path: Repository Ruleset

1. Open `Settings` -> `Rules` -> `Rulesets`.
2. Edit the active ruleset that protects `main`.
3. Ensure the ruleset targets the `main` branch or the default branch.
4. Enable:
   - `Require a pull request before merging`
   - `Require status checks to pass before merging`
5. Add the required status checks:
   - `repo_gate`
   - `connected_nuget_audit`
6. If the repository expects strict merge freshness, also enable:
   - `Require branches to be up to date before merging`
7. Save the ruleset.

## Fallback Path: Classic Branch Protection

Use this only if rulesets are unavailable for the repository.

1. Open `Settings` -> `Branches`.
2. Edit the `main` branch protection rule.
3. Enable:
   - `Require a pull request before merging`
   - `Require status checks to pass before merging`
4. Add:
   - `repo_gate`
   - `connected_nuget_audit`
5. Save the rule.

## Verification

1. Open a test pull request into `main`.
2. Confirm that GitHub shows both contexts as required checks.
3. Confirm that merge is blocked while either of these checks is missing or failing:
   - `repo_gate`
   - `connected_nuget_audit`
4. Confirm that merge becomes available only after both required checks pass.

## Notes

1. The in-repo manifest and checker validate the intended contract only; they do not mutate GitHub server-side rules.
2. If the checks do not appear in the GitHub UI, rerun both workflows on a fresh commit before editing the ruleset.
