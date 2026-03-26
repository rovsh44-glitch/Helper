# Runtime Review Slice Redaction Workflow

This note explains how the public `runtime-review-slice/sample_data/` tree is produced and reviewed without disclosing the private raw-session tooling or operator-only evidence workflow.

## Goal

The goal of the Stage 1 fixture workflow is simple:

1. derive the public sample data from real HELPER-oriented runtime scenarios;
2. remove private operational details;
3. keep the resulting fixture set stable enough for repeatable public verification.

## Public And Private Boundary

The workflow is intentionally split into two layers.

Private-only:

1. raw runtime capture from local HELPER sessions
2. private export helpers and operator review material
3. any source data that still contains machine-specific paths, identity, or private operational context

Public-safe:

1. the checked-in `runtime-review-slice/sample_data/` tree
2. the placeholder vocabulary used inside that tree
3. the repeatable public validation gate in [`../runtime-review-slice/scripts/validate-sample-data.ps1`](../runtime-review-slice/scripts/validate-sample-data.ps1)
4. runtime guard checks in [`../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/FixtureSecurityGuard.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/FixtureSecurityGuard.cs)

## Public Fixture Provenance

The current Stage 1 fixture set should be read as:

1. derived from real HELPER-oriented runtime scenarios
2. sanitized for public release
3. frozen as a checked-in proof pack for reproducible public verification

That provenance is also summarized in [`../runtime-review-slice/sample_data/README.md`](../runtime-review-slice/sample_data/README.md).

## Placeholder Rules

Public fixtures must replace sensitive material with deterministic placeholders.

Allowed placeholders include:

1. `C:/REDACTED_RUNTIME/...`
2. `USER_REDACTED`
3. `TOKEN_REDACTED`
4. `HOST_REDACTED`

The public fixture set must not contain:

1. real Windows user names or workstation paths
2. `HELPER_DATA_ROOT` locations
3. token-like material
4. non-local URLs
5. operator identity or reviewer identity

## Public Validation Gate

The repeatable public validation gate is:

```powershell
powershell -ExecutionPolicy Bypass -File runtime-review-slice/scripts/validate-sample-data.ps1
```

That script scans the checked-in `sample_data/` tree for:

1. non-redacted Windows paths
2. token-like material
3. non-local URLs

The runtime host applies the same boundary discipline again at read time through [`FixtureSecurityGuard.cs`](../runtime-review-slice/src/Helper.RuntimeSlice.Api/Services/FixtureSecurityGuard.cs).

## Maintainer Review Rule

Before publishing changed Stage 1 fixtures, maintainers should:

1. run `scripts/validate-sample-data.ps1`
2. run the canonical Stage 1 proof path in [`../runtime-review-slice/scripts/test.ps1`](../runtime-review-slice/scripts/test.ps1)
3. manually inspect changed fixture files
4. confirm the placeholders still match the public-safe vocabulary above

## Why The Raw Redaction Tooling Is Not Public

The public repo is meant to prove the public-safe boundary, not to expose raw internal session material or operator-only export helpers.

For that reason, the repo publishes:

1. the sanitized fixture result
2. the placeholder rules
3. the validation gate
4. the proof-boundary explanation

It intentionally does not publish:

1. raw captured runtime sessions
2. operator-only capture helpers
3. private evidence bundles used outside the public showcase

## Practical Reading Rule

Read this note together with:

1. [`../runtime-review-slice/sample_data/README.md`](../runtime-review-slice/sample_data/README.md)
2. [`runtime-review-slice-verification.md`](runtime-review-slice-verification.md)
3. [`public-proof-boundary.md`](public-proof-boundary.md)

That set gives the shortest public explanation of where the Stage 1 fixtures come from, how they are checked, and what remains intentionally private.
