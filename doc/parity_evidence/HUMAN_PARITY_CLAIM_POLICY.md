# Human Parity Claim Policy

Date: `2026-03-19`
Status: `active`

## Purpose

This policy defines when `Helper` is allowed to claim `human-level parity achieved`.

The claim is forbidden unless the canonical parity evidence bundle proves it.

## Authoritative claim requirements

The claim `human-level parity achieved` is allowed only if all conditions below are true at the same time:

1. `doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_BUNDLE.json` exists.
2. Bundle `status = COMPLETE`.
3. Bundle `claimEligible = true`.
4. Blind human-eval is authoritative.
5. Blind human-eval integrity has passed.
6. Blind human-eval provenance has passed.
7. Reviewer diversity requirements have passed.
8. Live real-model eval is authoritative.
9. `14-day` certification report is `GO`.
10. Latest counted snapshot has `open_p0_p1 = 0`.
11. Latest counted snapshot keeps release baseline in a passing state.

If any one of these conditions is false, the only allowed conclusion is one of:

- `parity not proven`
- `evidence incomplete`
- `authoritative evidence pending`

## Explicitly forbidden claim inputs

The following cannot be used as parity proof on their own:

- template reports
- sample snapshots
- synthetic-only scoring corpora
- dry-run real-model reports
- non-authoritative blind human-eval reports
- incomplete 14-day windows
- scattered supporting files without the canonical bundle

## Operational rule

Tooling existence is not proof.

The presence of scripts such as:

- `generate_human_parity_report.ps1`
- `run_eval_real_model.ps1`
- `certify_parity_14d.ps1`
- `build_parity_evidence_bundle.ps1`

does not authorize any parity claim unless the canonical bundle is complete and claim-eligible.

## Claim language policy

Allowed language when the bundle is incomplete:

- `human-level parity is not yet proven`
- `evidence collection is in progress`
- `current evidence is non-authoritative`

Allowed language only when the bundle is complete and claim-eligible:

- `human-level parity achieved`
- `human-level parity proven by authoritative bundle`

## Audit rule

All parity claims must cite the canonical bundle first:

- `doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_BUNDLE.json`
- `doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_BUNDLE.md`

If the claim cannot be justified from those two files, the claim must not be made.
