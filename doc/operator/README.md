# HELPER Operator Guide

Status: `active`
Updated: `2026-04-04`

## Purpose

This is the operator-facing entry point for running, checking, and certifying HELPER.

## Start Here

1. [Root README](../../README.md)
2. [Repo Hygiene And Runtime Artifact Governance](../security/REPO_HYGIENE_AND_RUNTIME_ARTIFACT_GOVERNANCE.md)
3. [Extensions](../extensions/README.md)
4. [Certification Hub](../certification/README.md)
5. [Remediation Closure Checklist](HELPER_REMEDIATION_CLOSURE_CHECKLIST_2026-03-28.md)

## Common Operator Tasks

### Run Locally

1. configure `.env.local`
2. ensure `HELPER_DATA_ROOT` is outside the repository
3. start API and UI from the launcher described in [README.md](../../README.md)

### Validate Repo Hygiene

1. `powershell -ExecutionPolicy Bypass -File scripts\secret_scan.ps1`
2. `powershell -ExecutionPolicy Bypass -File scripts\check_root_layout.ps1`
3. `powershell -ExecutionPolicy Bypass -File scripts\generate_repo_hygiene_report.ps1`

### Validate Release Claims

1. `powershell -ExecutionPolicy Bypass -File scripts\validate_gate_claims.ps1`
2. [CURRENT_STATE.md](../CURRENT_STATE.md)
3. [CURRENT_CERT_STATE.md](../certification/active/CURRENT_CERT_STATE.md)
4. [CURRENT_RELEASE_BASELINE.md](../certification/active/CURRENT_RELEASE_BASELINE.md)
5. `powershell -ExecutionPolicy Bypass -File scripts\baseline_capture.ps1`

### Run UI Workflow Smoke

1. ensure `HELPER_RUNTIME_SMOKE_API_BASE` points at the local API
2. run `powershell -ExecutionPolicy Bypass -File scripts\run_ui_workflow_smoke.ps1`
3. inspect `temp\verification\ui_workflow_smoke.md`

### Run Runtime Test Lanes

1. fast lane:
   - `powershell -ExecutionPolicy Bypass -File scripts\run_fast_tests.ps1 -Configuration Debug`
2. integration lane:
   - `powershell -ExecutionPolicy Bypass -File scripts\run_integration_tests.ps1 -Configuration Debug`
3. eval lane:
   - `powershell -ExecutionPolicy Bypass -File scripts\run_eval_harness_tests.ps1 -Configuration Debug`
   - owns `Eval`, `EvalOffline`, `EvalV2`, and eval-package preparation coverage
4. certification lane:
   - `powershell -ExecutionPolicy Bypass -File scripts\run_certification_tests.ps1 -Configuration Debug`
   - owns load/chaos, tool benchmark, lifecycle, routing, and diagnostics coverage
5. certification compile lane:
   - `powershell -ExecutionPolicy Bypass -File scripts\run_certification_compile_tests.ps1 -Configuration Debug`
   - owns promotion/certification smoke plus compile-gate integration coverage
   - default mode is operational and should be used for CI/local validation
   - forensic rerun: `powershell -ExecutionPolicy Bypass -File scripts\run_certification_compile_tests.ps1 -Configuration Debug -EnableBlameHang -BlameHangTimeoutSec 180`
6. do not put compile-path or eval benchmark coverage back into `Helper.Runtime.Tests`

### Inspect Extension Registry

1. review [Extensions](../extensions/README.md)
2. run `powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_cli.ps1 extension-registry`
3. keep checked-in providers disabled by default unless the operator environment explicitly enables them

### Work With Certification

1. [Certification Hub](../certification/README.md)
2. [Current pre-cert cycle](../pre_certification/cycles/precert_2026-03-16/README.md)

## Evidence Rule

Generated counted and certification artifacts remain in the evidence trees. Do not move them into canonical docs.
