# HELPER Sanitized Proof Bundle Policy - 2026-05-29

Publication classification: PUBLIC-SAFE POLICY

This policy defines how HELPER may publish a new May/June proof bundle without rewriting older April proof repositories or overstating private evidence.

## Immutable Historical Bundles

The April proof repositories are immutable historical bundles. They may be referenced as prior public evidence, but they must not be silently rewritten to represent later private-core behavior.

If an old bundle needs correction, publish a new correction note or superseding bundle. Do not force-push or replace historical contents without an explicit erratum.

## New Bundle Preconditions

A new public proof bundle can be generated only after the private evidence is sanitized and reviewed. The bundle must not contain private paths, raw logs, local-library copyrighted text, secrets, confidential source excerpts, or internal audit material that has not been cleared for publication.

Required bundle contents:

- `MANIFEST.md` describing scope, generation date, source commit, reviewer and publication classification.
- Checksums for every included artifact.
- Claim boundary that states exactly what the bundle proves and what it does not prove.
- Known limitations, including stale evidence, missing blind-human evidence, missing counted days, or environment constraints.
- Reproduction instructions that use public-safe fixtures only.
- License metadata and explicit reuse limitations for every included artifact.
- A clear statement: "not human parity proof" unless the canonical private 14-day bundle is complete and claim-eligible.

## Claim Boundary

Until the canonical private parity bundle is `COMPLETE` and `claimEligible = true`, every public proof bundle must state:

- "This is not human parity proof."
- "This does not complete the 14-day counted parity window."
- "This bundle is narrower than the private engineering state."

## Review Requirements

Before publication:

- Run the public disclosure review.
- Run repository secret scanning.
- Review generated files manually for private paths and raw evidence leakage.
- Confirm old proof repositories remain unchanged.
- Store the generated manifest and checksums with the new bundle.

## Release Rule

Public proof material must be additive and versioned. Old bundles remain historical; new bundles must be separately named, separately checksummed and no stronger than the sanitized evidence supports.
