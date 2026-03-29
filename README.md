<div align="center">
<img width="1200" height="475" alt="GHBanner" src="https://github.com/user-attachments/assets/0aa67016-6eaf-458a-adb2-6e31a0763ed6" />
</div>

# HELPER

HELPER is a local-first product shell for research, planning, generation, and operator-guided execution. It combines a React UI, the `Helper.Api` backend boundary, and the `Helper.Runtime` runtime.

## Honest Status

- Release baseline: `PASS`
- Current certification state: `GREEN_ANCHOR_PENDING`
- Human-level parity: `not proven`
- Blind human evaluation: `implemented, but the current corpus is still non-authoritative`
- `14-day` parity window: `not started`

Canonical truth lives in:

- `doc/CURRENT_STATE.md`
- `doc/certification/active/CURRENT_CERT_STATE.md`
- `doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_BUNDLE.md`
- `doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_STATE.md`

Do not claim `human-level parity achieved` from this repository unless the canonical parity evidence bundle becomes complete and claim-eligible.

## What Exists Today

- Local-first product shell with a React UI plus backend-hosted session bootstrap.
- A shared runtime stack for research, planning, generation, and operator workflows.
- Release-baseline tooling, certification docs, parity evidence scaffolding, and evaluation automation.
- A source-driven API boundary with governed env/config, tracked OpenAPI snapshot, and repo hygiene gates.

## What HELPER Does Not Claim

- It does not claim human-level parity today.
- It does not claim a completed authoritative blind human-eval round today.
- It does not claim a completed counted `14-day` parity window today.
- It does not promise that every internal artifact should be made public.
- It does not promise a public hosted SaaS deployment from this repository.

## Public-Facing Pack

Prepared showcase surfaces:

- [One-pager](docs/one-pager.md)
- [Architecture overview](docs/architecture-overview.md)
- [Product roadmap](docs/product-roadmap.md)
- [Use cases](docs/use-cases.md)
- [Contact](CONTACT.md)
- [Security](SECURITY.md)
- [Contributing](CONTRIBUTING.md)
- [FAQ](FAQ.md)

These files are the curated export source for the public showcase. The private-core repository remains broader than the public-safe surface and should not be published as-is.

Supporting technical truth:

- `doc/README.md`
- `doc/architecture/README.md`
- `doc/config/README.md`
- `doc/operator/README.md`
- `doc/security/README.md`
- `doc/research/README.md`
- `doc/extensions/README.md`

## Run Locally

Prerequisites:

- Node.js
- .NET SDK

Quick start:

1. Copy `.env.local.example` to `.env.local`.
2. Set `HELPER_API_KEY` and `HELPER_SESSION_SIGNING_KEY` in `.env.local`.
3. Set `HELPER_DATA_ROOT` outside the repository, for example `%USERPROFILE%\\HELPER_DATA`.
4. Run `powershell -ExecutionPolicy Bypass -File scripts\migrate_helper_data_root.ps1` if runtime/data folders still live under the repository root.
5. If you need PDF-to-image vision fallback, run `powershell -ExecutionPolicy Bypass -File scripts\bootstrap_ghostscript.ps1`.
6. Install frontend dependencies with `npm install`.
7. Start backend and UI with `Run_Helper.bat fast` or `Run_Helper.bat warm`.
8. Wait until `/api/readiness` reports `readyForChat=true`.

Runtime-only paths:

- `HELPER_PROJECTS_ROOT`
- `HELPER_LIBRARY_ROOT`
- `HELPER_LOGS_ROOT`
- `HELPER_TEMPLATES_ROOT`

## Operator Notes

- The backend writes its selected port to `HELPER_LOGS_ROOT\\API_PORT.txt`.
- Repo hygiene gates live in `scripts\secret_scan.ps1` and `scripts\check_root_layout.ps1`.
- Refresh governed env docs with `powershell -ExecutionPolicy Bypass -File scripts\generate_env_reference.ps1`.
- Validate env governance with `powershell -ExecutionPolicy Bypass -File scripts\check_env_governance.ps1`.
- Browser transport is allowed only in `services/httpClient.ts`, `services/generatedApiClient.ts`, and the `/api/auth/session` bootstrap path in `services/apiConfig.ts`.
- Refresh the committed OpenAPI snapshot with `npm run api:refresh-openapi-snapshot`.
- Refresh active release truth with `powershell -ExecutionPolicy Bypass -File scripts\refresh_active_gate_snapshot.ps1`.
- Refresh the release baseline with `npm run baseline:capture`.
- Run the full repo sweep with `npm run ci:gate`.
- `tools\ghostscript\` is a bootstrap-managed local cache and is intentionally excluded from normal git tracking.
- `searxng\settings.yml.new` is the tracked template; keep `searxng\settings.yml` as an operator-local copy with local secret and host overrides.

## Repository Model

The current recommended model is:

1. keep this repository as the private-core baseline;
2. keep the curated public showcase separate from the core repo, not as a blind dump of every internal artifact;
3. use the included `.github/ISSUE_TEMPLATE/` forms as the export source for the public showcase.

## Contact And Next Step

Commercial, partnership, and demo intent should go through [CONTACT.md](CONTACT.md). Sensitive vulnerability reporting should follow [SECURITY.md](SECURITY.md).

The next honest milestone after the packaging and public-showcase export is expanding public-safe proof surfaces, especially the runnable runtime-review slice, without widening the private-core disclosure boundary.
