# Generated Artifact Validation Slice Architecture

This note explains the actual engineering shape of the public `generated-artifact-validation-slice`.

It is meant to answer a narrow question: what is real, what is fixture-backed, and what is intentionally absent from this public slice.

## What This Slice Is

The generated artifact validation slice is a local-first, `.NET-only`, CLI-first validation package composed of:

- minimal extracted validation contracts
- deterministic validation and normalization services
- a small CLI surface
- a checked-in fixture set
- a public xUnit suite

It is not a web demo and it is not the full HELPER generation runtime.

## Directory Map

- [`../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Contracts/`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Contracts/) contains the minimal public contracts and result records used by the slice
- [`../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/) contains the validation implementations
- [`../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Cli/`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Cli/) contains the CLI entry point and command routing
- [`../generated-artifact-validation-slice/test/Helper.GeneratedArtifactValidation.Tests/`](../generated-artifact-validation-slice/test/Helper.GeneratedArtifactValidation.Tests/) contains the public xUnit suite
- [`../generated-artifact-validation-slice/sample_fixtures/`](../generated-artifact-validation-slice/sample_fixtures/) contains the public-safe artifact, blueprint, and compile-gate fixtures
- [`../generated-artifact-validation-slice/scripts/`](../generated-artifact-validation-slice/scripts/) contains the canonical public test and sample-validation scripts
- [`../generated-artifact-validation-slice/GeneratedArtifactValidationSlice.sln`](../generated-artifact-validation-slice/GeneratedArtifactValidationSlice.sln) contains the standalone solution for the slice

## Runtime Topology

At runtime, the slice works like this:

1. [`Program.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Cli/Program.cs) enters the public CLI.
2. [`ValidationCommandRunner.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Cli/ValidationCommandRunner.cs) routes commands for artifact validation, blueprint validation, compile-gate validation, or a full sample sweep.
3. [`ArtifactValidationService.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/ArtifactValidationService.cs) loads a fixture manifest and validates each declared file.
4. [`GenerationPathSanitizer.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/GenerationPathSanitizer.cs) normalizes or blocks unsafe relative paths.
5. [`GeneratedArtifactPlaceholderScanner.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/GeneratedArtifactPlaceholderScanner.cs) flags TODO markers, placeholder patterns, and empty method bodies.
6. [`GeneratedFileAstValidator.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/GeneratedFileAstValidator.cs) performs Roslyn-based C# syntax and top-level type checks.
7. [`BlueprintContractValidator.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/BlueprintContractValidator.cs) normalizes malformed blueprint inputs into deterministic public-safe output.
8. [`CompileGateValidator.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/CompileGateValidator.cs) copies a sample project into a temporary workspace and performs a report-only `dotnet build`.

The slice does not call the private core, does not invoke a model, and does not expose the private repair loop.

## Capability Map

| Validation concern | Main implementation | Backing input |
| --- | --- | --- |
| Relative-path safety | [`GenerationPathSanitizer.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/GenerationPathSanitizer.cs) | blueprint paths and artifact manifest paths |
| Method-signature validation | [`MethodSignatureValidator.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/MethodSignatureValidator.cs) | blueprint method signatures |
| Method-signature normalization | [`MethodSignatureNormalizer.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/MethodSignatureNormalizer.cs) | malformed or property-like blueprint signatures |
| Identifier normalization | [`IdentifierSanitizer.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/IdentifierSanitizer.cs) | project, namespace, type, and method names |
| Blueprint contract normalization | [`BlueprintContractValidator.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/BlueprintContractValidator.cs) | [`malformed-blueprint.json`](../generated-artifact-validation-slice/sample_fixtures/blueprints/malformed-blueprint.json) |
| Placeholder scanning | [`GeneratedArtifactPlaceholderScanner.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/GeneratedArtifactPlaceholderScanner.cs) | artifact fixture file contents |
| Generated-file AST validation | [`GeneratedFileAstValidator.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/GeneratedFileAstValidator.cs) | C# artifact fixture files |
| Artifact fixture orchestration | [`ArtifactValidationService.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/ArtifactValidationService.cs) | [`sample_fixtures/artifacts/`](../generated-artifact-validation-slice/sample_fixtures/artifacts/) |
| Compile-gate validation | [`CompileGateValidator.cs`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/CompileGateValidator.cs) | [`sample_fixtures/compile_gate/`](../generated-artifact-validation-slice/sample_fixtures/compile_gate/) |

## What The Fixtures Represent

The checked-in fixtures are public-safe examples of generated-code validation scenarios.

They are used to prove:

- path-guardrail behavior
- signature normalization behavior
- blueprint normalization behavior
- placeholder detection behavior
- AST validation behavior
- report-only compile-gate behavior

They are not used to prove:

- live generation
- LLM-backed repair quality
- full template-promotion behavior
- the full private-core compile-gate and repair stack

## Boundary Controls

The slice deliberately stays narrow.

- Only minimal contracts were extracted into the public package.
- The compile gate is `report-only`; it does not attempt the private repair loop.
- The CLI only operates on paths supplied by the reviewer or on checked-in fixtures.
- Sample projects are copied into a temporary workspace before build validation.
- The fixture set is checked into the slice and does not require access to private evidence bundles.

These choices keep the slice reviewable without exposing private orchestration or internal auto-fix logic.

## What Is Real In This Public Slice

- the standalone solution
- the contracts and validation implementations
- the CLI command routing
- Roslyn-based AST and signature validation
- fixture-based artifact and blueprint validation
- report-only compile-gate validation
- the xUnit suite
- the public scripts

Representative tests include:

- [`GeneratedArtifactValidationSliceTests.cs`](../generated-artifact-validation-slice/test/Helper.GeneratedArtifactValidation.Tests/GeneratedArtifactValidationSliceTests.cs)

## What Is Intentionally Absent

This slice does not include:

- live generation
- provider calls
- model routing
- LLM-backed repair flows
- `GenerationFixPlanner`
- `FixStrategyRunner`
- template-promotion flows
- operator-only scripts
- the broader private `Helper.Runtime` orchestration surface

Those omissions are deliberate. They keep the slice public-safe and keep the claim boundary narrow.

## Practical Reading Rule

Read this note together with:

- [`../generated-artifact-validation-slice/README.md`](../generated-artifact-validation-slice/README.md)
- [`generated-artifact-validation-slice-verification.md`](generated-artifact-validation-slice-verification.md)
- [`generated-artifact-validation-slice-comparison.md`](generated-artifact-validation-slice-comparison.md)
- [`public-proof-boundary.md`](public-proof-boundary.md)

That combination explains the engineering shape of the slice, how to verify it, and how it differs from the Stage 1 runtime-review slice.

