# Helper Generation Contracts Compatibility

This note defines the public compatibility posture for `helper-generation-contracts/`.

It is intentionally conservative. Public visibility in the repo does not automatically mean stable shared API.

## Stable At First Public Release

At first Stage 3 publish, the stable shared commitments are limited to:

1. package identity `Helper.Generation.Contracts`
2. package purpose: generation-facing shared contracts only
3. namespace family `Helper.Generation.Contracts`
4. these published contract types:
   - `FileRole`
   - `FileRoleJsonConverter`
   - `ArbanMethodTask`
   - `SwarmFileDefinition`
   - `SwarmBlueprint`
   - `GeneratedFile`
   - `BuildError`
5. the published JSON property names and enum spellings for those types

## Experimental Even If Public

The following may be visible later while still remaining experimental:

1. any new type outside the published stable family
2. helper methods and convenience APIs
3. examples, sample payloads, and sample generators
4. extra docs and integration notes

Experimental means external readers should not rely on those items as durable integration contracts.

## Visible But Not Shared API

The public repo contains code that is real but not part of the shared compatibility promise.

That includes:

1. `runtime-review-slice` DTOs
2. Stage 2 validation-result and artifact-report contracts
3. Stage 2 validation-core implementation types
4. slice-local scripts, fixtures, and glue code

Visibility does not upgrade those surfaces into supported shared contracts.

## What Counts As A Breaking Change

For the stable shared contract family, a change is breaking if it:

1. removes or renames a published stable type
2. moves a stable type to a different namespace or package without a compatibility bridge
3. removes, renames, or retypes an existing public property
4. changes the positional-record shape of a published stable contract
5. changes published JSON field names or `FileRole` spellings
6. changes converter behavior so that previously valid payloads stop deserializing the same way

Because the stable shared surface is record-based, even additive field changes are treated as breaking unless a future compatibility note says otherwise.

## Versioning Rule

The package may start in preview versioning, but preview does not mean silent breaking changes are acceptable.

Any breaking change to the stable shared contract family should come with:

1. an explicit compatibility note
2. an updated dependency map if the boundary changed
3. a compatibility bridge for at least one public release cycle when feasible

## External Reliance Rule

External readers may rely on:

1. the existence of `helper-generation-contracts/` as the intended shared public contract surface
2. the stable shared contract family listed above
3. the dependency boundary described in [`helper-generation-contracts-dependency-map.md`](helper-generation-contracts-dependency-map.md)

External readers should not rely on:

1. slice-local DTOs as reusable shared contracts
2. validation-core implementation classes
3. visible private-core contracts that are not exported by this package
