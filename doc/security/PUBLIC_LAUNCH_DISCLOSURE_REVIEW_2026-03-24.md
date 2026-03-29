# Public Launch Disclosure Review

Date: `2026-03-24`
Status: `active snapshot`

## Purpose

This document records the current disclosure-review verdict before any GitHub launch.

It is a point-in-time snapshot. Re-run the script for current numbers:

- `powershell -ExecutionPolicy Bypass -File scripts\generate_public_launch_disclosure_review.ps1`

## Reviewed Candidate Set

Review basis:

- tracked files
- untracked non-ignored working-tree files
- current publication model from `doc/security/PUBLIC_SHOWCASE_BOUNDARY_POLICY_2026-03-24.md`

## Result

- Verdict: `BLOCK_DIRECT_PUBLISH`
- Direct publish allowed: `NO`
- Candidate files reviewed: `2261`
- Public-safe files: `14`
- Private-only files: `2247`
- Review-required files: `0`

## Hygiene Gates

- Secret scan: `PASS`
- Secret hits: `0`
- Root layout: `PASS`
- Root violations: `0`
- Source fatal violations: `0`
- Source warnings: `0`

## Public-Safe Candidate Surface Present In Working Tree

- `README.md`
- `CONTACT.md`
- `SECURITY.md`
- `CONTRIBUTING.md`
- `FAQ.md`
- `.github/ISSUE_TEMPLATE/**`
- `docs/**`
- `media/README.md`

## Why Direct Publish Is Blocked

The current repository is still a private-core workspace, not a public showcase repository.

The disclosure review found large private-only surfaces still present in the launch candidate:

- `doc/**`: `1163`
- `src/**`: `591`
- `scripts/**`: `162`
- `test/**`: `117`
- `eval/**`: `67`
- `components/**`: `45`

These are not treated as public by default, and the policy intentionally blocks broad publication of internal source, tooling, tests, active docs, and operational evidence.

## Honest Interpretation

This is a good outcome, not a failure:

1. hygiene gates are green;
2. no first-party secret hits were found;
3. the policy correctly prevents an unsafe public push of the private-core repository;
4. the current public pack exists, but it must be exported into a curated showcase repo or sanitized mirror instead of publishing this repository as-is.

## Next Action

If a GitHub launch is needed, publish only the allowlisted showcase surfaces and keep the rest of the repository private.
