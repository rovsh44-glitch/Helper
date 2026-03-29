# Runbook: Template Rollback

## Purpose
Execute safe rollback when a promoted template causes activation/certification degradation.

## Triggers
1. `template-certification-gate` failed for active version.
2. Release gate failed on `promotion/certification/parity` after template activation.
3. Alert fired for `activation rollback rate` or `promotion fail spike`.

## Preconditions
1. Identify affected template id.
2. Ensure previous active version exists (`.activation_history.json`).
3. Capture current evidence before rollback:
- latest promotion report
- latest certification report
- latest parity gate report

## Rollback Procedure
1. Inspect versions:
`powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_cli.ps1 template-versions <TemplateId>`
2. Execute rollback:
`powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_cli.ps1 template-rollback <TemplateId>`
3. Verify active version:
`powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_cli.ps1 template-versions <TemplateId>`
4. Re-run certification gate:
`powershell -ExecutionPolicy Bypass -File scripts/run_template_certification_gate.ps1`
5. Re-run release gate (or relevant subset):
`powershell -ExecutionPolicy Bypass -File scripts/run_generation_release_gate.ps1`

## Expected Result
- Rollback command returns success and old version becomes active.
- Certification gate passes for active version.
- Release gate no longer fails due rolled-back template version.

## Failure Handling
1. If rollback fails: activate known-good version manually.
`powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_cli.ps1 template-activate <TemplateId> <Version>`
2. If no known-good version exists: disable runtime promotion temporarily.
- `HELPER_TEMPLATE_PROMOTION_EMERGENCY_DISABLE=true`
- or `HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1=false`
3. Open incident with attached artifacts and block next release.
