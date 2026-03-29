# GitHub Showcase Implementation Checklist

Date: `2026-03-24`
Status: `implemented snapshot`

Purpose: convert `doc/HELPER_GITHUB_SHOWCASE_STRATEGY_2026-03-24.md` from strategy prose into an execution-ready packaging plan after the first repository commit.

References:

- `doc/HELPER_GITHUB_SHOWCASE_STRATEGY_2026-03-24.md`
- `doc/parity_evidence/WEB_RESEARCH_POST_GITHUB_30_DAY_COLLECTION_PLAN.md`
- `doc/CURRENT_STATE.md`

## Baseline

- First repository commit exists: `1fd8461` (`Initial lean repository baseline`).
- Root `README.md` has been rewritten to expose honest product status and curated public-pack references.
- Core architecture, governance, and certification documents already exist and can supply public proof material.
- Root public-pack source surfaces now exist: `.github/`, `CONTACT.md`, `SECURITY.md`, `CONTRIBUTING.md`, `FAQ.md`, `media/`, `docs/`.
- Large historical `doc/` bulk remains outside the first commit and must not be pushed blindly into a public showcase.
- Current parity blockers are operational, not policy-only: blind reviewer corpus is still non-authoritative and the counted `14-day` parity window has not started.

## Execution Note

This checklist has since been executed. The curated showcase was exported into a separate repo workspace, committed, and published as a public GitHub repository. Keep this document as the packaging record for the private-core side of that split.

## Recommended Decision

Use a two-contour model:

1. `private core repo`
   Keep the full source tree, internal ops material, sensitive implementation detail, and due-diligence-only artifacts here.
2. `public showcase repo`
   Publish only the narrative, proof, demo, intake, and sanitized evidence surfaces needed for trust and external collection.

Do not assume the current repository should become public without a boundary review first.

## Workstreams

| Workstream | Status | Concrete action | Exit criteria |
| --- | --- | --- | --- |
| Repository baseline | `done` | keep the first commit as the private-core anchor | stable Git history exists |
| Public/private boundary decision | `done` | publication model frozen as `private core repo + curated public showcase repo` | direct publish of the current repo as-is is not an approved path |
| Public README rewrite | `done` | root `README.md` now presents product scope, honest status, local run path, and public-pack references | root `README.md` reads as a public entry point |
| Public docs pack | `done` | `docs/one-pager.md`, `docs/architecture-overview.md`, `docs/product-roadmap.md`, and `docs/use-cases.md` were added | public docs answer what the product is, for whom, and why it matters |
| Trust files | `done` | `CONTACT.md`, `SECURITY.md`, `CONTRIBUTING.md`, and `FAQ.md` were added | external visitors have a clean communication and policy surface |
| Media pack | `done` | real screenshots and curated media were prepared for the separate showcase repo while the private-core repo keeps only the manifest surface | public repo has visual proof without source diving |
| Disclosure review | `done` | allowlist/denylist policy and candidate-set review now exist under `doc/security/*` and `scripts/generate_public_launch_disclosure_review.ps1` | direct publish is explicitly blocked and only the curated showcase allowlist is approved |
| GitHub interaction surfaces | `done` | `.github/ISSUE_TEMPLATE` forms were added for prompt intake, reviewer application, and partnership/demo contact | public intake paths exist once the repo is published |
| Discussion and labels setup | `deferred` | discussions remain intentionally disabled in the public showcase repo; intake is handled through issue forms only | intake can be moderated without ad hoc discussion surfaces |
| Web-research program surface | `done` | public intake forms are live in the separate showcase repo and route prompt/reviewer/contact requests into curated public entry points | public repo explains how challenge submission and review work |
| Showcase export | `done` | allowlisted public pack exported into `showcase_repo/` as a standalone repo workspace on branch `main` | public-safe pack exists outside private-core history |
| Release/demo surface | `done` | media pack and public runtime-review slice now provide a publishable proof path with known boundaries | a visitor can verify product reality quickly |
| Launch hygiene sweep | `done` | secret scan, root-layout hygiene, and public-launch disclosure review have been executed for the current candidate set | no first-party secret or root-layout blockers remain, but direct publish is still intentionally blocked by policy |

## Immediate File-Level Deliverables

Implemented in this repository:

- `README.md` rewrite for public entry
- `CONTACT.md`
- `SECURITY.md`
- `CONTRIBUTING.md`
- `FAQ.md`
- `docs/one-pager.md`
- `docs/architecture-overview.md`
- `docs/product-roadmap.md`
- `docs/use-cases.md`
- `media/README.md`
- `.github/ISSUE_TEMPLATE/submit-web-research-prompt.yml`
- `.github/ISSUE_TEMPLATE/apply-as-blind-reviewer.yml`
- `.github/ISSUE_TEMPLATE/request-demo-or-contact.yml`

## Recommended Execution Order

1. Decide publication topology.
2. Freeze a public-safe allowlist and a private-only denylist.
3. Add real media assets to `media/`.
4. Add GitHub-side label provisioning and discussion categories.
5. Add a final sanitized public program note for post-launch web-research collection.
6. Export only the allowlisted public pack into a separate showcase repo or sanitized mirror branch.
7. Only then open the public repo.

## Hard Rules

- Do not publish reviewer CSV submissions, reveal maps, or live blind score sheets during an active round.
- Do not publish secrets, machine-local config, or machine-specific host paths.
- Do not dump the full historical `doc/` archive into the public showcase.
- Do not let the public README read like an operator bootstrap note only.

## Next Practical Step

Maintain the split model: keep the private-core repository broad, keep the public showcase curated, and only export additional surfaces after explicit disclosure review.
