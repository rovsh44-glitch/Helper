# Browser Auth Boundary

- Status: accepted
- Date: 2026-03-06
- Canonical ADR: `doc/adr/ADR_BROWSER_AUTH_SESSION_BOOTSTRAP.md`

## Decision

- Browser clients never own durable backend or model secrets.
- Browser flows bootstrap a scoped server-issued session token.
- Secret leakage is prevented by architecture tests and release gates.

## Implementation

- `src/Helper.Api/Hosting/ApiSessionTokenService.cs`
- `src/Helper.Api/Hosting/EndpointRegistrationExtensions.SystemAndGovernance.cs`
- `test/Helper.Runtime.Tests/ArchitectureFitnessTests.cs`
