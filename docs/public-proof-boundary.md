# Public Proof Boundary

This note explains what the public HELPER showcase does and does not prove on its own.

## What The Public Repository Shows Today

- a real public-facing documentation and trust surface
- a narrow runnable `runtime-review-slice` with explicit boundaries
- a narrow source-complete `generated-artifact-validation-slice` with explicit boundaries
- a narrow shared `helper-generation-contracts` package with explicit compatibility boundaries
- a public GitHub Actions workflow that reruns the canonical Stage 1-3 proof paths on GitHub-hosted Windows runners
- screenshots and generated artifact examples presented as captured evidence from local HELPER sessions
- intake surfaces for demos, reviewer applications, and related contact

## What It Does Not Prove On Its Own

- full private-core behavior
- human-level parity
- completed certification
- end-to-end independent reproduction of every screenshot or generated artifact shown in `media/`
- a full hosted product or general-availability release

## Why The Public Code Slices Matter

The public code surfaces exist to provide public-safe technical proof paths.

The runtime-review slice demonstrates:

- a real runnable slice
- a documented slice architecture
- a documented public verification path with a deterministic Stage 1 test surface
- fixture-backed runtime review flows
- a bounded API and UI surface
- a reviewable public boundary

The generated-artifact-validation slice demonstrates:

- a second source-complete public slice
- deterministic generated-artifact validation workflows
- blueprint normalization guardrails
- placeholder and AST validation guardrails
- a documented public verification path with a deterministic Stage 2 test surface
- a report-only compile-gate path over checked-in sample projects

The helper-generation-contracts package demonstrates:

- one intentional shared developer-facing public surface
- a reusable generation-contract family that no longer lives only inside the Stage 2 slice
- a documented compatibility boundary for that shared surface
- a deterministic Stage 3 test path over the shared package itself

None of these public surfaces replaces the private core or proves the full product.

The public CI workflow helps by continuously rerunning the same narrow proof paths that the docs expose, but it still proves only those narrow public-safe slices and the shared public contract surface.

For the concrete component maps and verification paths behind the public slices, read:

- [runtime-review-slice-architecture.md](runtime-review-slice-architecture.md)
- [runtime-review-slice-verification.md](runtime-review-slice-verification.md)
- [generated-artifact-validation-slice-architecture.md](generated-artifact-validation-slice-architecture.md)
- [generated-artifact-validation-slice-verification.md](generated-artifact-validation-slice-verification.md)
- [generated-artifact-validation-slice-comparison.md](generated-artifact-validation-slice-comparison.md)
- [helper-generation-contracts-dependency-map.md](helper-generation-contracts-dependency-map.md)
- [helper-generation-contracts-compatibility.md](helper-generation-contracts-compatibility.md)
- [`../runtime-review-slice/README.md`](../runtime-review-slice/README.md)
- [`../generated-artifact-validation-slice/README.md`](../generated-artifact-validation-slice/README.md)
- [`../helper-generation-contracts/README.md`](../helper-generation-contracts/README.md)

## How To Read The Screenshots And Generated Artifacts

The screenshots and generated artifact images are presented as captured local demo evidence from HELPER sessions.

They should be interpreted as:

- evidence of product shape and operator workflow
- evidence that the project has real demo surfaces
- a starting point for diligence conversations

They should not be interpreted as:

- a complete standalone proof bundle
- a substitute for private-core review
- a claim that every shown artifact can be reproduced from this public repo alone

## Practical Reading Rule

Use the public repository for:

- first-pass diligence
- product-shape review
- trust-boundary review
- two narrow public-safe technical proof paths plus one narrow shared public contract surface

Use private review, if appropriate, for:

- deeper evidence inspection
- source-complete technical review
- ownership diligence
- certification and parity evidence beyond the public boundary
