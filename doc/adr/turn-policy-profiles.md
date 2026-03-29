# Turn Policy Profiles

- Status: accepted
- Date: 2026-03-06

## Decision

- Turn execution is policy-driven through budget profiles, runtime policies and stage gating.
- Mandatory, optional synchronous and optional asynchronous stages are separated explicitly.
- Research, critic, grounding and async audit are enabled only when policy allows them.

## Implementation

- `src/Helper.Api/Conversation/LatencyBudgetPolicy.cs`
- `src/Helper.Api/Conversation/TurnStagePolicy.cs`
- `src/Helper.Api/Backend/Configuration/BackendOptions.cs`
- `src/Helper.Api/Backend/Application/TurnExecutionStateMachine.cs`
