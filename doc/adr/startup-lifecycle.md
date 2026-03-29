# Startup Lifecycle

- Status: accepted
- Date: 2026-03-06
- Canonical ADR: `doc/adr/ADR_STARTUP_LIFECYCLE_READINESS.md`

## Decision

- `Listening` and `ReadyForChat` are separate lifecycle states.
- Warmup may continue after minimal readiness is reached.
- Startup timing is observable through `/api/readiness` and `/api/control-plane`.

## Implementation

- `src/Helper.Api/Hosting/StartupReadinessService.cs`
- `src/Helper.Api/Hosting/ModelWarmupService.cs`
- `src/Helper.Api/Hosting/EndpointRegistrationExtensions.SystemAndGovernance.cs`
