# ADR: Target Framework Split

Status: `active`
Updated: `2026-03-28`

## Decision

The repository intentionally keeps a split target-framework policy:

- main backend/runtime projects stay on `net8.0`
- runtime slice and verification-facing slice tests stay on `net9.0`

## Rationale

`Helper.Api`, `Helper.Runtime`, `Helper.Runtime.Knowledge`, and `Helper.Runtime.Evolution` are the production-weight runtime graph and should remain on the more conservative baseline used by the main host.

`Helper.RuntimeSlice.*` and selected test surfaces are isolated verification/public-review slices where newer framework features and tooling can be exercised without forcing the main host forward at the same time.

## Guardrails

1. The split is allowed only while the slice remains isolated from the main runtime host.
2. Cross-project references must continue to flow from `net9.0` tests/slices into `net8.0` runtime code, not the other way around.
3. Any future unification to `net9.0` must be a deliberate repo-wide change, not accidental drift.

## Solution Shape

`Helper.sln` must explicitly include:

- `Helper.Runtime.Knowledge`
- `Helper.Runtime.Evolution`

This keeps the solution view aligned with the real build graph.
