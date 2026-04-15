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

## 2026-04-15 Revalidation

Status: `revalidated`

The full `HELPER` repository is again `Private`. This matches the decision above.

Current conclusion:

1. Do not make the private-core repository public just to unlock GitHub code scanning.
2. GitHub does not provide a mode where the same repository is public while most source, tests, scripts, and active evidence remain hidden.
3. The public path remains a separate curated showcase/proof-bundle repository.
4. The public repository should contain only reviewed surfaces: narrative docs, sanitized proof-bundle inputs, analyzer summaries, reproducibility instructions, and selected public-safe evidence.
5. Source code, tests, internal scripts, local runtime artifacts, and active operator docs remain private unless separately reviewed and explicitly exported.

The intended public claim is therefore narrow:

`Helper provides an operator-grade research and analysis workflow with evidence discipline, demonstrated by a reproducible LFL20 proof bundle.`

It is not:

`The full Helper private-core source tree is public/open-source.`

## 2026-04-15 Publication Closure

Status: `implemented`

The approved publication path has now been executed for the first narrow public proof surface:

1. Private-core `Helper` remains `Private`.
2. Baseline tag `helper-private-core-2026-04-15-green` was created on the green private-core `main`.
3. A separate public proof repository was created: `https://github.com/rovsh44-glitch/helper-proof-bundle-lfl20`.
4. The public repository contains sanitized LFL20 artifacts only:
   - selected `LFL20` corpus JSONL
   - sanitized per-case results
   - analyzer summaries
   - evidence-fusion audit
   - case-level metrics
   - manifest/checksums
   - reproducibility and limitation notes
5. The public repository does not contain private-core source, tests, internal scripts, runtime logs, auth files, local paths, or raw request envelopes.

This closes the immediate public/private alignment gap. Future public updates should extend the proof-bundle repository or create additional sanitized showcase repositories, not widen the private-core repository visibility.
