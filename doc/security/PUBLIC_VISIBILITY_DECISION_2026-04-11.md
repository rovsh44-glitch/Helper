# Public Visibility Decision

Date: `2026-04-11`
Status: `active decision`

## Decision

The current `HELPER` repository remains the `private-core workspace`.

Do not switch this repository to direct `Public` publication as the primary launch path.

## Basis

1. `doc/security/PUBLIC_SHOWCASE_BOUNDARY_POLICY_2026-03-24.md` freezes the publication model as `private core repo + public showcase repo`.
2. The latest disclosure review bundle under `doc/security/public_launch_review_2026-04-11/` must continue to be interpreted as a `default deny` review of the tracked candidate set.
3. GitHub automation in this repository is being hardened to protect `main`, but GitHub-hosted status attachment only materializes after the workflow changes are pushed and enforced remotely.
4. The repository now has an explicit license file, but that does not convert the private-core tree into an approved public showcase surface.

## Approved Publication Path

If external publication is needed:

1. create a curated public showcase repo or sanitized mirror
2. export only reviewed public-safe files
3. rerun disclosure review for the launch candidate
4. keep operator-only, source, test, script, and active evidence surfaces in the private-core repo unless explicitly re-reviewed

## Re-open Conditions

Revisit this decision only after all of the following are true:

1. deterministic repo-side gates are green
2. the canonical solution build is fully covered
3. GitHub Actions protects PRs and `main`
4. disclosure review for the launch candidate is current
5. the publication target has its own explicit licensing and security posture
