# Generated Artifact Validation Slice Verification

This note defines the canonical public verification path for the `generated-artifact-validation-slice`.

Use it to answer a narrow question: can an external reviewer build, test, and inspect the public Stage 2 validation slice without needing the private core.

## Verification Target

Successful public verification means all of the following are true:

1. the standalone Stage 2 solution builds locally
2. the public xUnit suite passes
3. the CLI can run the checked-in sample-validation sweep
4. the good and bad fixtures produce the documented PASS and expected-fail outcomes
5. the observed behavior stays within the documented public proof boundary

It does not mean that the full HELPER private core has been verified.

## Prerequisites

Use a machine with:

- PowerShell
- .NET 9 SDK
- NuGet access for the initial restore on a clean machine

The slice targets `net9.0`, as defined in:

- [`../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Contracts/Helper.GeneratedArtifactValidation.Contracts.csproj`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Contracts/Helper.GeneratedArtifactValidation.Contracts.csproj)
- [`../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/Helper.GeneratedArtifactValidation.Core.csproj`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Core/Helper.GeneratedArtifactValidation.Core.csproj)
- [`../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Cli/Helper.GeneratedArtifactValidation.Cli.csproj`](../generated-artifact-validation-slice/src/Helper.GeneratedArtifactValidation.Cli/Helper.GeneratedArtifactValidation.Cli.csproj)
- [`../generated-artifact-validation-slice/test/Helper.GeneratedArtifactValidation.Tests/Helper.GeneratedArtifactValidation.Tests.csproj`](../generated-artifact-validation-slice/test/Helper.GeneratedArtifactValidation.Tests/Helper.GeneratedArtifactValidation.Tests.csproj)

## Clean-Machine Setup

From the `generated-artifact-validation-slice` directory, the canonical Stage 2 setup is already built into the public test script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
```

That script performs:

1. `dotnet restore` for the public xUnit test project
2. `dotnet build` for the public Stage 2 test project
3. `dotnet test` for the public xUnit suite
4. a scripted sample-validation sweep through the public CLI

If you want to prefetch dependencies manually before running the script, use:

```powershell
dotnet restore test\Helper.GeneratedArtifactValidation.Tests\Helper.GeneratedArtifactValidation.Tests.csproj
```

## Canonical Build And Test Path

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
```

That script performs the public test path in this order:

1. `dotnet restore test\Helper.GeneratedArtifactValidation.Tests\Helper.GeneratedArtifactValidation.Tests.csproj`
2. `dotnet build test\Helper.GeneratedArtifactValidation.Tests\Helper.GeneratedArtifactValidation.Tests.csproj -c Debug -m:1 --no-restore`
3. `dotnet test test\Helper.GeneratedArtifactValidation.Tests\Helper.GeneratedArtifactValidation.Tests.csproj -c Debug --no-build`
4. `powershell -ExecutionPolicy Bypass -File scripts/validate-samples.ps1`

Expected result:

- the Stage 2 solution dependencies restore successfully
- the test project builds successfully
- the xUnit suite passes
- the sample-validation sweep reports:
  - good artifacts `PASS`
  - bad artifacts expected failure
  - malformed blueprint normalization `PASS`
  - good compile-gate fixture `PASS`
  - bad compile-gate fixture expected failure

Representative coverage lives in:

- [`GeneratedArtifactValidationSliceTests.cs`](../generated-artifact-validation-slice/test/Helper.GeneratedArtifactValidation.Tests/GeneratedArtifactValidationSliceTests.cs)

## Fixture Assumptions

The canonical public test path is intentionally fixture-backed.

It assumes:

1. the checked-in `sample_fixtures/` tree is present
2. the artifact manifests remain valid JSON
3. the good and bad fixture sets remain public-safe and intentionally differentiated
4. tests run against those checked-in fixtures rather than against private generation outputs

If a reviewer swaps in different artifacts or projects for manual experiments, that is outside the canonical Stage 2 public test contract.

## Canonical Sample-Validation Path

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/validate-samples.ps1
```

That script:

1. builds the public CLI project
2. runs the CLI with `samples --root <slice-root>`
3. validates the checked-in artifact, blueprint, and compile-gate fixture families

If a reviewer wants to invoke the CLI directly after a build, the equivalent command is:

```powershell
dotnet run --project src\Helper.GeneratedArtifactValidation.Cli\Helper.GeneratedArtifactValidation.Cli.csproj -c Debug --no-build -- samples --root .
```

## Manual Verification Checklist

After the canonical script path succeeds, a reviewer should confirm:

1. the solution file exists at [`GeneratedArtifactValidationSlice.sln`](../generated-artifact-validation-slice/GeneratedArtifactValidationSlice.sln)
2. the CLI project exists and builds
3. the xUnit suite passes
4. the good artifact fixture set reports clean validation
5. the bad artifact fixture set reports blocked placeholders, invalid syntax, or unsafe path conditions
6. the malformed blueprint fixture normalizes into deterministic public-safe output
7. the good sample project passes compile gate
8. the bad sample project fails compile gate

## What Counts As A Successful Public Verification

A public reviewer can treat verification as successful if:

1. setup completes with public dependencies only
2. the canonical test script passes
3. the canonical sample-validation script passes
4. the observed results match the scope described in:
   - [`generated-artifact-validation-slice-architecture.md`](generated-artifact-validation-slice-architecture.md)
   - [`generated-artifact-validation-slice-comparison.md`](generated-artifact-validation-slice-comparison.md)
   - [`public-proof-boundary.md`](public-proof-boundary.md)

## Expected Failure Modes

The most likely public verification failures are:

### `dotnet restore` failure

Likely meaning:

- the .NET SDK is missing
- the local NuGet configuration is broken
- the machine cannot reach the package source on a clean restore

### `dotnet build` or `dotnet test` failure

Likely meaning:

- the local .NET toolchain is broken
- a checked-in source file no longer compiles
- a fixture contract and test expectation diverged

### Fixture preflight failure

Likely meaning:

- `sample_fixtures/` is incomplete
- a required manifest is missing
- the reviewer is running from the wrong working directory

### `Artifact validation FAIL`

Likely meaning:

- a supposedly good fixture now contains placeholder markers
- a supposedly good fixture no longer matches its manifest expectations
- a path or AST validation contract changed

### `Compile gate FAIL` on the good sample project

Likely meaning:

- the checked-in good sample project no longer builds
- the local SDK cannot compile the sample target

### `Compile gate PASS` on the bad sample project

Likely meaning:

- the intentionally bad fixture was accidentally repaired or replaced
- the failure fixture no longer exercises the intended compiler error path

These are slice-boundary failures, not proof that the full private core is broken.

## What This Verification Path Does Not Prove

This verification path does not prove:

- live generation
- model quality
- template promotion
- the private repair loop
- full private-core orchestration
- certification completion
- human-level parity

It proves only the public-safe Stage 2 validation slice and the narrow engineering boundary around it.

## Not Required For Basic Public Verification

The following are not required for successful public verification:

- the private core
- external model keys
- provider accounts
- operator-only scripts
- any internal evidence bundle

## Practical Reading Rule

Read this note together with:

- [`../generated-artifact-validation-slice/README.md`](../generated-artifact-validation-slice/README.md)
- [`generated-artifact-validation-slice-architecture.md`](generated-artifact-validation-slice-architecture.md)
- [`generated-artifact-validation-slice-comparison.md`](generated-artifact-validation-slice-comparison.md)
- [`public-proof-boundary.md`](public-proof-boundary.md)

That set gives the shortest complete picture of how to verify the slice, what it is made of, and what its successful verification does and does not prove.

