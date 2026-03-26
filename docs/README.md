# HELPER Docs Hub

This folder is the public reading path for the HELPER showcase repository.

## Start Here

- [One-pager](one-pager.md)
- [Executive summary](executive-summary.md)
- [Status definitions](status-definitions.md)
- [Public parity methodology](parity-methodology-public.md)
- [Product overview](product-overview.md)
- [Architecture overview](architecture-overview.md)
- [Demo guide](demo-guide.md)
- [Runtime review slice](../runtime-review-slice/README.md)
- [Runtime review slice architecture](runtime-review-slice-architecture.md)
- [Runtime review slice verification](runtime-review-slice-verification.md)
- [Runtime review slice redaction workflow](runtime-review-slice-redaction-workflow.md)
- [Generated artifact validation slice](../generated-artifact-validation-slice/README.md)
- [Generated artifact validation slice architecture](generated-artifact-validation-slice-architecture.md)
- [Generated artifact validation slice verification](generated-artifact-validation-slice-verification.md)
- [Generated artifact validation slice comparison](generated-artifact-validation-slice-comparison.md)
- [Helper generation contracts](../helper-generation-contracts/README.md)
- [Helper generation contracts dependency map](helper-generation-contracts-dependency-map.md)
- [Helper generation contracts compatibility](helper-generation-contracts-compatibility.md)
- [Risk disclosure](risk-disclosure.md)

## Product Narrative

- [Problem](problem.md)
- [Solution](solution.md)
- [Differentiation](differentiation.md)
- [Use cases](use-cases.md)
- [External roadmap](external-roadmap.md)
- [Roadmap](product-roadmap.md)

## Market And Deal Readiness

- [Asset thesis](asset-thesis.md)
- [Market thesis](market-thesis.md)
- [Business model](business-model.md)
- [Moat](moat.md)
- [Traction](traction.md)
- [IP and ownership](ip-and-ownership.md)
- [Due diligence readiness](due-diligence-readiness.md)
- [Public proof boundary](public-proof-boundary.md)

## Notes

This repository is a curated public showcase. It intentionally does not include the private-core implementation, internal scripts, or sensitive operational evidence bundle.

The runtime-review slice verification note is the canonical deterministic test/build path for the public Stage 1 slice.

The runtime-review slice redaction workflow note explains how the checked-in Stage 1 fixtures are sanitized, validated, and kept inside the public proof boundary.

The generated-artifact-validation slice verification note is the canonical deterministic test/build path for the public Stage 2 slice.

The helper-generation-contracts package README is the canonical entrypoint for the public Stage 3 shared surface.

Canonical public proof-path commands:

- Stage 1 test path: `powershell -ExecutionPolicy Bypass -File runtime-review-slice/scripts/test.ps1`
- Stage 2 test path: `powershell -ExecutionPolicy Bypass -File generated-artifact-validation-slice/scripts/test.ps1`
- Stage 2 sample-validation path: `powershell -ExecutionPolicy Bypass -File generated-artifact-validation-slice/scripts/validate-samples.ps1`
- Stage 3 test path: `powershell -ExecutionPolicy Bypass -File helper-generation-contracts/scripts/test.ps1`

- [Public release checklist](public-release-checklist.md)
