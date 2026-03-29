# Telemetry Vocabulary

Status: `active`
Updated: `2026-03-16`
Schema: `runtime-log-dto-v2 / route-telemetry-v2`

## Purpose

This document defines the stable telemetry vocabulary that backend surfaces emit for runtime review and operator tooling.

Use this vocabulary as the source of truth for:

1. runtime log DTO semantics
2. route telemetry snapshots
3. operator UI cards and filters

## Runtime Log DTO v2

`/api/runtime/logs` now emits schema version `2` and attaches structured semantics to each entry.

Fields:

1. `severity`: `error | warn | info | debug | neutral`
2. `scope`: `boot | control | api | model | storage | security | bus | network | exception | misc`
3. `domain`: `readiness | gateway | persistence | auth | generation | telemetry | transport | runtime | unknown`
4. `operationKind`: stable classifier for the dominant activity in the line
5. `summary`: compact human-readable sentence derived from the raw line
6. `route`: route or endpoint when one is extractable
7. `correlationId`: request, trace, or conversation correlation key when present
8. `latencyMs`: parsed latency when one is extractable
9. `latencyBucket`: one of `sub_100ms | 100_500ms | 500_1500ms | 1500_5000ms | 5000_plus`
10. `degradationReason`: explicit backend-derived reason when the line indicates degraded or failed state
11. `markers`: compact tags for quick filtering without regex reconstruction

## Route Telemetry v2

Route telemetry is emitted through the backend control-plane snapshot and uses one shared event model for chat and generation routing.

Stable dimensions:

1. `channel`: `chat | generation`
2. `operationKind`: `chat_turn | template_routing | generation_run`
3. `routeKey`: selected intent, template id, or model-route identifier
4. `quality`: `high | medium | low | degraded | failed | blocked | unknown`
5. `outcome`: `selected | completed | clarification | degraded | failed | blocked`

Optional metadata:

1. `confidence`
2. `modelRoute`
3. `intentSource`
4. `executionMode`
5. `budgetProfile`
6. `workloadClass`
7. `degradationReason`
8. `routeMatched`
9. `requiresClarification`
10. `budgetExceeded`
11. `compileGatePassed`
12. `artifactValidationPassed`
13. `smokePassed`
14. `goldenTemplateEligible`
15. `goldenTemplateMatched`

## Quality Semantics

Quality levels mean:

1. `high`: routing confidence or outcome is strong and not degraded
2. `medium`: routing is acceptable but not top-tier confidence
3. `low`: routing succeeded with weak confidence or weak outcome quality
4. `degraded`: routing completed with explicit fallback, clarification, or degraded execution
5. `failed`: routing or execution failed
6. `blocked`: safety or policy intentionally blocked execution
7. `unknown`: backend cannot honestly classify the route

## Operator Rule

Frontend surfaces should consume these fields directly whenever they exist.

Free-text inference is allowed only as a compatibility fallback for older payloads or historical evidence.
