# Golden Template Certification Protocol

## Objective
Define deterministic certification criteria before template activation.

## Mandatory Gates
1. Metadata/schema validation (`template.json`).
2. Compile gate pass.
3. Artifact validation (`bin` output checks).
4. Smoke scenarios pass.
5. Safety scan pass (secret/policy patterns).

## Output Artifacts
- `certification_report.md`
- `certification_report.json`
- `<templateVersion>/certification_status.json`

## CLI
- Single version certification:
`powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_cli.ps1 template-certify <TemplateId> <Version> [ReportPath]`
- Fleet gate:
`powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_cli.ps1 template-certification-gate [ReportPath]`

## API
- `POST /api/templates/{templateId}/certify/{version}`
- `POST /api/templates/certification-gate`

## Activation Rule
- Auto-activate is allowed only when `Passed=true`.
- Default post-activation contract is verification, not a second heavy rebuild:
  - active pointer must move to the promoted version
  - published template root must exist
  - `certification_status.json` must remain `Passed`
  - published tree must match the certified candidate tree
- Optional full post-activation re-certification is controlled by `HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY`.
- On activation failure, post-activation verification failure, or explicit post-activation re-certification failure, rollback is mandatory.
