# Generated Artifact Validation Slice Compared To Runtime Review Slice

This note explains why the public `generated-artifact-validation-slice` exists alongside the already-published `runtime-review-slice`.

The short answer is: the two slices prove different things.

## Why A Second Slice Exists

The Stage 1 `runtime-review-slice` proves a narrow operator-facing runtime surface.

The Stage 2 `generated-artifact-validation-slice` proves a narrow code-facing validation surface around generated artifacts.

Publishing both matters because a reviewer can now inspect:

1. one public slice centered on runtime review and operator-facing state
2. one public slice centered on generated-code validation guardrails

That is a stronger public engineering story than a single runnable slice alone.

## Comparison At A Glance

| Question | Runtime review slice | Generated artifact validation slice |
| --- | --- | --- |
| Primary proof surface | read-only runtime review UI and API | CLI-first validation and compile-gate workflows |
| Main inputs | sanitized runtime logs, readiness, indexing, telemetry fixtures | generated artifact files, malformed blueprint fixtures, sample projects |
| Main output | rendered runtime state and API responses | validation reports, normalization results, compile-gate pass/fail outcomes |
| Tech shape | ASP.NET Core + React + DTO contracts | `.NET-only` contracts + core validators + CLI + tests |
| Canonical proof path | frontend build, API build, xUnit, local host start | solution build, xUnit, sample-validation sweep |
| What it does not claim | full private runtime or live model execution | live generation or the private repair loop |

## What Stage 2 Adds To Public Trust

The Stage 2 slice adds three things the Stage 1 slice does not try to prove directly:

1. deterministic validation of generated-code fixtures
2. deterministic normalization of malformed blueprint inputs
3. a report-only compile-gate path over public sample projects

That does not replace Stage 1. It complements it.

## Shared Pattern Vs Slice-Specific Pattern

Shared pattern across both slices:

- public-safe boundary
- checked-in fixtures
- local-first verification
- narrow claims
- explicit omission of the private core

Slice-specific to Stage 1:

- operator-facing UI
- API surface
- runtime logs, telemetry, and indexing snapshots

Slice-specific to Stage 2:

- Roslyn-backed code validation
- blueprint normalization
- placeholder scanning
- report-only compile-gate validation

## Combined Reading Rule

Read the Stage 1 slice when you want to inspect the public runtime-review proof surface.

Read the Stage 2 slice when you want to inspect the public generated-artifact validation proof surface.

Read both when you want the current strongest public technical picture of HELPER without crossing into the private core.

Read the shared `helper-generation-contracts` package when you want the narrow reusable contract layer that now sits underneath the Stage 2 slice.

## Practical Reading Rule

Use this note together with:

- [`../runtime-review-slice/README.md`](../runtime-review-slice/README.md)
- [`../generated-artifact-validation-slice/README.md`](../generated-artifact-validation-slice/README.md)
- [`../helper-generation-contracts/README.md`](../helper-generation-contracts/README.md)
- [`runtime-review-slice-architecture.md`](runtime-review-slice-architecture.md)
- [`generated-artifact-validation-slice-architecture.md`](generated-artifact-validation-slice-architecture.md)
- [`helper-generation-contracts-dependency-map.md`](helper-generation-contracts-dependency-map.md)
- [`public-proof-boundary.md`](public-proof-boundary.md)

That set explains why two public slices now exist, what each one proves, and why neither should be mistaken for the full private-core product.
