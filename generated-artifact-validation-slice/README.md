# Helper Generated Artifact Validation Slice

`Helper Generated Artifact Validation Slice` is the second public-safe code slice for HELPER.

It proves a narrow validation boundary around generated artifacts:

1. path sanitization
2. method-signature normalization and validation
3. blueprint contract normalization
4. generated C# file AST validation
5. placeholder and incomplete-code scanning
6. report-only compile-gate validation

This slice is:

1. `.NET-only`
2. `CLI-first`
3. fixture-backed
4. local-first
5. intentionally separate from the private runtime and repair loop
6. dependent on the sibling shared contracts package for the narrow reusable generation contract family

## Shared Contracts Dependency

This slice now consumes the shared public package at [`../helper-generation-contracts/README.md`](../helper-generation-contracts/README.md) for:

1. `FileRole`
2. `ArbanMethodTask`
3. `SwarmFileDefinition`
4. `SwarmBlueprint`
5. `GeneratedFile`
6. `BuildError`

The Stage 2 slice still keeps its own validation-result and artifact-report contracts locally because those remain slice-specific rather than shared API.

## Architecture Note

Read the companion note at [`../docs/generated-artifact-validation-slice-architecture.md`](../docs/generated-artifact-validation-slice-architecture.md) for:

1. slice topology
2. capability-to-file mapping
3. fixture-backed validation boundaries
4. what the sample fixtures do and do not represent
5. which product layers are intentionally absent

## Verification Note

Read the companion note at [`../docs/generated-artifact-validation-slice-verification.md`](../docs/generated-artifact-validation-slice-verification.md) for:

1. clean-machine setup
2. canonical build, test, and sample-validation commands
3. expected verification signals
4. likely public failure modes
5. what successful verification does and does not prove

## Comparison Note

Read [`../docs/generated-artifact-validation-slice-comparison.md`](../docs/generated-artifact-validation-slice-comparison.md) for:

1. why Stage 2 exists next to the runtime-review slice
2. what this slice adds to public trust
3. what is shared pattern versus slice-specific
4. how the two public slices should be read together

## Quickstart

From the `generated-artifact-validation-slice` directory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
```

`scripts/test.ps1` is the canonical Stage 2 proof path. It restores public .NET dependencies, builds the slice tests, runs the xUnit suite, and then runs the checked-in sample-validation sweep.

To run the sample validation sweep directly:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/validate-samples.ps1
```

## Boundary

Excluded from this slice:

1. live generation
2. model routing
3. provider calls
4. LLM-backed repair flows
5. template promotion
6. operator-only evidence and automation

## Fixtures

Public-safe fixtures live under:

`sample_fixtures/`

They are intended to prove validation workflows, not full private-core generation behavior.
