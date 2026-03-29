# Model Gateway

- Status: accepted
- Date: 2026-03-06

## Decision

- All model access goes through a dedicated gateway.
- Model classes and execution pools are first-class runtime concepts.
- Warmup, timeout, fallback and pool telemetry are centralized.

## Implementation

- `src/Helper.Api/Backend/ModelGateway/HelperModelGateway.cs`
- `src/Helper.Api/Backend/ModelGateway/ModelGatewayContracts.cs`
- `src/Helper.Api/Backend/ModelGateway/ModelGatewayTelemetry.cs`
- `src/Helper.Api/Backend/ControlPlane/BackendControlPlane.cs`
