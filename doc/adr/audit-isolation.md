# Audit Isolation

- Status: accepted
- Date: 2026-03-06
- Canonical ADR: `doc/adr/ADR_ASYNC_AUDIT_ISOLATION.md`

## Decision

- Async audit is scheduled from the orchestration boundary, not from HTTP transport.
- Audit runs through a bounded queue, worker retry policy and dead-letter storage.
- Audit pressure may degrade or skip low-priority audit instead of impacting chat latency.

## Implementation

- `src/Helper.Api/Backend/Application/PostTurnAuditScheduler.cs`
- `src/Helper.Api/Conversation/PostTurnAuditQueue.cs`
- `src/Helper.Api/Conversation/PostTurnAuditWorker.cs`
