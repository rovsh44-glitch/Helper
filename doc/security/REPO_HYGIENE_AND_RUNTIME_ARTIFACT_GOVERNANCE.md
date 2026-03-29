# Repo Hygiene And Runtime Artifact Governance

Date: `2026-03-24`

## Purpose

This repository treats the source tree as code, configuration, and human-readable documentation only. Machine-local runtime data must live under `HELPER_DATA_ROOT`, not under the repository root and never under `src/`.

## Canonical Owner

This document is the operational source of truth for git-readiness hygiene. The decision rationale lives in `doc/HELPER_GIT_READINESS_DECISION_MEMO_2026-03-24.md`, but executable policy must stay aligned with:

1. this governance doc
2. `scripts/check_root_layout.ps1`
3. `scripts/migrate_helper_data_root.ps1`
4. `scripts/bootstrap_ghostscript.ps1`

## Authoritative Checks

1. `powershell -ExecutionPolicy Bypass -File scripts\secret_scan.ps1`
2. `powershell -ExecutionPolicy Bypass -File scripts\check_root_layout.ps1`
3. `powershell -ExecutionPolicy Bypass -File scripts\generate_repo_hygiene_report.ps1`

The first two scripts are CI gates. The report script is the operator-friendly wrapper that emits:

- `temp\hygiene\secret_scan_report.json`
- `temp\hygiene\root_layout_report.json`
- `temp\hygiene\REPO_HYGIENE_REPORT.md`

## Policy

1. `HELPER_DATA_ROOT` must be outside `src/`.
2. `HELPER_AUTH_KEYS_PATH` must not point inside `src/`.
3. Runtime auth artifacts such as `auth_keys.json` must not exist under `src/Helper.Api`.
4. Repository-root machine-local directories such as `PROJECTS`, `library`, `LOG`, `logs`, `runtime`, `tmp_pdfepub_smoke`, `.vs`, and similar non-canonical outputs are forbidden.
5. `AGI_TEST_V3` is sandbox/experimental only. If retained locally, it belongs under `sandbox/experimental/`, not as a root product surface.
6. `tools/ghostscript` is a bootstrap-managed local cache, not a normal git-tracked vendor payload. Rehydrate it with `scripts\bootstrap_ghostscript.ps1`.
7. Source-surface runtime debris under `src/Helper.Api` is reported so it can be cleaned, even when it is not treated as a fatal CI violation.

## Expected Runtime Locations

- API port file: `HELPER_LOGS_ROOT\API_PORT.txt`
- auth key store: `HELPER_DATA_ROOT\auth_keys.json`
- logs: `HELPER_LOGS_ROOT\...`
- projects: `HELPER_PROJECTS_ROOT\...`
- library/templates: `HELPER_LIBRARY_ROOT\...`
- operator runtime bundles and queue snapshots: `HELPER_DATA_ROOT\runtime\...`

## Migration And Bootstrap

1. Run `powershell -ExecutionPolicy Bypass -File scripts\migrate_helper_data_root.ps1` if root runtime/data folders still exist.
2. Run `powershell -ExecutionPolicy Bypass -File scripts\bootstrap_ghostscript.ps1` only when PDF vision fallback is required.
3. Do not recreate `runtime/` at the repository root after migration; operator-only outputs belong under `HELPER_DATA_ROOT\runtime`.

## Operator Use

Run the report script after local troubleshooting, template certification work, or migration steps that may generate runtime debris:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\generate_repo_hygiene_report.ps1
```

If `check_root_layout.ps1` prints source-surface warnings, move or delete those machine-local artifacts before claiming the workspace is clean.

