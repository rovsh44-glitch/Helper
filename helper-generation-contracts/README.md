# Helper Generation Contracts

`Helper Generation Contracts` is the first shared developer-facing public package in the HELPER showcase.

It exposes one narrow generation-facing contract family:

1. file-role taxonomy
2. blueprint file definitions
3. blueprint root shape
4. generated-file shape
5. build-error shape

This package is intentionally:

1. contract-only
2. low-dependency
3. public-safe
4. narrower than the full private runtime

## What This Package Is For

Use this package when you want the public shared contract surface behind HELPER's generated-artifact workflows without pulling in the validation implementation layer.

The package is currently used by:

1. the public `generated-artifact-validation-slice`
2. future public-safe integration and payload examples

## Companion Notes

Read the companion notes at:

1. [`../docs/helper-generation-contracts-dependency-map.md`](../docs/helper-generation-contracts-dependency-map.md)
2. [`../docs/helper-generation-contracts-compatibility.md`](../docs/helper-generation-contracts-compatibility.md)

## Quickstart

From the `helper-generation-contracts` directory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
```

That script is the canonical Stage 3 proof path. It restores, builds, and tests the shared public package.

## Boundary

This package does not include:

1. validation implementations
2. Roslyn-based compile or AST analysis
3. runtime telemetry contracts
4. provider calls
5. orchestration services
6. slice-local DTOs from `runtime-review-slice`

## Stability Posture

This package is a shared public surface, but it is not a promise that every visible public type in the repo is stable.

For the exact stability and breaking-change rules, read [`../docs/helper-generation-contracts-compatibility.md`](../docs/helper-generation-contracts-compatibility.md).
