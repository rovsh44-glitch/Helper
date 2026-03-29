# Public Showcase Boundary Policy

Date: `2026-03-24`
Status: `active`

## Purpose

This policy defines what is safe to publish in a public GitHub showcase and what must stay private in the current `HELPER` repository model.

The governing assumption is:

- current repository = `private core`
- public GitHub launch = `curated showcase` or `sanitized mirror`

This policy is intentionally `default deny`. A direct public push of the current tracked repository is forbidden unless a disclosure review says otherwise.

## Canonical References

- `doc/GITHUB_SHOWCASE_IMPLEMENTATION_CHECKLIST_2026-03-24.md`
- `doc/security/REPO_HYGIENE_AND_RUNTIME_ARTIFACT_GOVERNANCE.md`
- `scripts/generate_public_launch_disclosure_review.ps1`

## Publication Model Decision

The publication model is frozen as:

1. `private core repo`
   Full source, tooling, tests, internal ops surfaces, and internal evidence remain here.
2. `public showcase repo`
   Only curated trust, narrative, contact, demo, intake, and explicitly approved disclosure surfaces belong here.

Direct publication of the current repository as-is is not an approved launch path.

## Public-Safe Allowlist

These paths are approved for the public showcase by default:

- `README.md`
- `CONTACT.md`
- `SECURITY.md`
- `CONTRIBUTING.md`
- `FAQ.md`
- `.github/ISSUE_TEMPLATE/**`
- `docs/**`
- `media/**`

Reason:

- these files are narrative, trust, contact, media, or intake surfaces;
- they do not expose the private-core runtime or operator-only repository shape by themselves;
- they are intentionally written to avoid parity overclaim and private data disclosure.

## Explicit Private-Only Denylist

These surfaces are private-only by default and are not approved for direct public publication:

- `src/**`
- `components/**`
- `hooks/**`
- `services/**`
- `contexts/**`
- `utils/**`
- `modules/**`
- `scripts/**`
- `test/**`
- `eval/**`
- `mcp_config/**`
- `searxng/**`
- `doc/**`
- `artifacts/**`
- `temp/**`
- `sandbox/**`
- `concepts/**`
- `node_modules/**`
- `dist/**`
- `tools/**`
- `.env*`
- `.gitignore`
- `.ignore`
- `App.tsx`
- `Directory.Build.*`
- `Helper.sln`
- `docker-compose.yml`
- `eval_live_monitor.json`
- `index.*`
- `metadata.json`
- `package*.json`
- `personality.json`
- `postcss.config.cjs`
- `tailwind.config.cjs`
- `tsconfig.json`
- `types.ts`
- `vite-env.d.ts`
- `vite.config.ts`
- `vite.shared.config.mjs`
- `*.ps1`
- `*.bat`

Reason:

- these files are source, build, test, runtime, operator, integration, or internal evidence surfaces;
- some expose internal architecture depth that belongs in the private-core repo;
- some carry machine-specific, deployment-specific, or low-signal operational detail;
- `doc/**` remains private by default because active and historical canonical docs are still mixed and need separate sanitization if selected for public release.

## Default-Deny Rule

If a tracked file is not matched by the public-safe allowlist, it is not approved for public release automatically.

There are only two valid ways to change that:

1. explicitly add the file or surface to the allowlist after review;
2. copy or rewrite the needed content into the public showcase pack.

## Required Review Before GitHub Launch

Before any public launch:

1. run `scripts/generate_public_launch_disclosure_review.ps1`
2. run `scripts/secret_scan.ps1`
3. run `scripts/check_root_layout.ps1`
4. confirm the verdict is not `ALLOW_DIRECT_PUBLISH` for the current repo unless policy is intentionally changed
5. publish only the curated allowlisted showcase surfaces

## Hard Rules

- Do not publish the current repository by reflex just because the first Git commit exists.
- If a local export is materialized inside the private-core workspace, keep `showcase_repo/` ignored and outside core history.
- Do not publish active or historical `doc/**` trees without explicit sanitization.
- Do not publish operator-local config, reviewer submissions, reveal maps, or machine-local runtime paths.
- Do not convert `default deny` into broad pattern-based allowlists just to make a push easier.
