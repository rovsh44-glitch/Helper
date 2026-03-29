# Public Showcase Export Note

Date: `2026-03-24`
Status: `active snapshot`

## Result

The allowlisted public showcase pack was exported into:

- `showcase_repo/`

The export is a standalone repository workspace, separate from the private-core repository.

## Exported Surface

Exported public-safe files: `14`

- `README.md`
- `CONTACT.md`
- `SECURITY.md`
- `CONTRIBUTING.md`
- `FAQ.md`
- `.github/ISSUE_TEMPLATE/apply-as-blind-reviewer.yml`
- `.github/ISSUE_TEMPLATE/config.yml`
- `.github/ISSUE_TEMPLATE/request-demo-or-contact.yml`
- `.github/ISSUE_TEMPLATE/submit-web-research-prompt.yml`
- `docs/one-pager.md`
- `docs/architecture-overview.md`
- `docs/product-roadmap.md`
- `docs/use-cases.md`
- `media/README.md`

## Git State

- standalone Git repository initialized: `YES`
- initial branch: `main`
- commits created: `YES`
- public remote push completed: `YES`

Current state is no longer review-only: the exported showcase repo exists, has commits, and has been pushed as the curated public showcase.

## Core Repo Safety

To avoid accidental backflow into the private-core history:

- `showcase_repo/` is ignored in the private-core `.gitignore`

## Next Step

Keep the export boundary narrow and update the public showcase only through curated follow-up exports, not by widening the private-core publication surface.
