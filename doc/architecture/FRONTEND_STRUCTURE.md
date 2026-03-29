# Frontend Structure

Status: `active`
Updated: `2026-03-17`

## Purpose

This document fixes the intended frontend boundary so the UI can keep evolving without collapsing back into monolithic views or generated-client leakage.

## Layering

1. `services/generatedApiClient.ts`
   Generated transport and DTO layer. Replaceable from OpenAPI output.
2. `services/api/*.ts` and `services/*Api.ts`
   Hand-written adapters and convenience wrappers around generated transport.
3. `hooks/*` and `contexts/*`
   UI state, polling, synchronization, and workflow composition.
4. `components/*` and `components/views/*`
   Presentation and interaction surfaces only.

## Rules

1. `components`, `hooks`, and `contexts` must not import `generatedApiClient` directly.
2. `components`, `hooks`, and `contexts` must not call `helperApi.*` directly.
3. Browser transport stays inside sanctioned API modules.
4. Oversized route views must be decomposed before they pass review.
5. Route-specific styling should prefer route-scoped CSS modules over growing the global Tailwind surface when that styling does not need to be shared application-wide.

## Current Boundaries

Generated transport stays in:

- [generatedApiClient.ts](../../services/generatedApiClient.ts)
- [httpClient.ts](../../services/httpClient.ts)

Hand-written wrappers currently include:

- [conversationApi.ts](../../services/conversationApi.ts)
- [planningApi.ts](../../services/planningApi.ts)
- [evolutionOperationsApi.ts](../../services/evolutionOperationsApi.ts)
- [goalsApi.ts](../../services/goalsApi.ts)
- [runtimeApi.ts](../../services/api/runtimeApi.ts)

## Fitness Gates

Use these gates together:

1. `npm run frontend:check`
   Enforces view-file size limits and the generated-client boundary.
2. `powershell -ExecutionPolicy Bypass -File scripts/check_bundle_budget.ps1`
   Enforces `mainJs`, `mainCss`, and lazy chunk budgets from [performance_budgets.json](../../scripts/performance_budgets.json).
3. `npm run build`
   Produces the measured bundle surface used by the budget gate.

## W3-PR04 Outcome

`RuntimeConsoleView` is now a route shell rather than a monolithic screen. Runtime log intelligence, presentation primitives, lazy operator panels, and route-scoped CSS are separated into bounded files:

- [RuntimeConsoleView.tsx](../../components/views/RuntimeConsoleView.tsx)
- [RuntimeConsoleLogsPanel.tsx](../../components/runtime-console/RuntimeConsoleLogsPanel.tsx)
- [RuntimeConsoleSidebar.tsx](../../components/runtime-console/RuntimeConsoleSidebar.tsx)
- [RuntimeConsoleTelemetryDeck.tsx](../../components/runtime-console/RuntimeConsoleTelemetryDeck.tsx)
- [RuntimeConsolePresentation.tsx](../../components/runtime-console/RuntimeConsolePresentation.tsx)
- [runtimeConsole.module.css](../../components/runtime-console/runtimeConsole.module.css)

This is the reference shape for future large frontend surfaces.
