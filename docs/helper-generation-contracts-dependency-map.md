# Helper Generation Contracts Dependency Map

This note explains how the public `helper-generation-contracts` package fits into the current HELPER showcase repo.

## Package Purpose

`helper-generation-contracts/` is the first shared developer-facing public package in the repo.

It exists to hold one narrow generation-facing contract family that can be reused without exposing the private runtime or the validation implementation layer.

## Package Contents

The package contains only:

1. `FileRole`
2. `FileRoleJsonConverter`
3. `ArbanMethodTask`
4. `SwarmFileDefinition`
5. `SwarmBlueprint`
6. `GeneratedFile`
7. `BuildError`

It does not contain validators, CLI commands, Roslyn analysis, runtime DTOs, or provider/orchestration logic.

## Direct Dependency Shape

At the code level, `Helper.Generation.Contracts` depends only on:

1. the .NET base class library
2. `System.Text.Json`

It intentionally avoids:

1. Roslyn packages
2. ASP.NET Core
3. React or frontend tooling
4. runtime services
5. provider integrations
6. private-core orchestration packages

## Repo-Level Relationship Map

Read the current public dependency contour like this:

1. `runtime-review-slice/`
   - independent public Stage 1 slice
   - does not depend on `helper-generation-contracts/`
2. `generated-artifact-validation-slice/`
   - public Stage 2 slice
   - now depends on `helper-generation-contracts/` for the shared generation contract family
   - keeps slice-local validation result and report types inside `Helper.GeneratedArtifactValidation.Contracts`
3. `helper-generation-contracts/`
   - public Stage 3 shared package
   - exposes the reusable generation contract family without the validator implementation layer

## What Stayed Local To Stage 2

The following remain local to the validation slice and were intentionally not moved into the shared package:

1. path sanitization results
2. method-signature validation and normalization results
3. blueprint validation results
4. generated-file validation results
5. placeholder findings
6. artifact fixture manifests and per-file validation reports
7. compile-gate results

Those types are real and public, but they are slice-local validation contracts rather than the shared developer-facing boundary.

## What Remains Outside The Public Shared Surface

The shared package still does not expose:

1. runtime telemetry contracts
2. runtime-review DTOs
3. validation-core implementation classes
4. provider or orchestration interfaces
5. the private repair loop

## Practical Reading Rule

Use:

1. [`../helper-generation-contracts/README.md`](../helper-generation-contracts/README.md) for the package entrypoint
2. [`helper-generation-contracts-compatibility.md`](helper-generation-contracts-compatibility.md) for reliance and breaking-change rules
3. [`generated-artifact-validation-slice-architecture.md`](generated-artifact-validation-slice-architecture.md) if you want to see how the Stage 2 slice consumes the shared package
