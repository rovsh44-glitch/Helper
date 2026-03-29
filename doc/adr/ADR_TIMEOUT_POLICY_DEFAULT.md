# ADR: Generation Stage Timeout Defaults

Date: 2026-03-01
Status: Accepted

## Context

`GenerationStageTimeoutPolicy` currently uses `HELPER_CREATE_TIMEOUT_SEC` as the global creation timeout and uses that same value as default synthesis budget when `HELPER_CREATE_TIMEOUT_SYNTHESIS_SEC` is not set.

A test was asserting an outdated 300-second default synthesis timeout, while runtime behavior has been 900 seconds by default.

## Decision

We keep the runtime contract:

- `HELPER_CREATE_TIMEOUT_SEC` default: `900` seconds.
- `HELPER_CREATE_TIMEOUT_SYNTHESIS_SEC` default: global timeout value.
- Stage-specific timeout is clamped by the global timeout.

Test coverage is aligned with this contract.

## Consequences

- CI and test expectations now match runtime behavior.
- Configuration remains explicit: users can still override synthesis via `HELPER_CREATE_TIMEOUT_SYNTHESIS_SEC`.
- Future contract changes must update both code and tests in one PR.
