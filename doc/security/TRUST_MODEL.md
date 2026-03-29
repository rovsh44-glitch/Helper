# HELPER Trust Model

Date: `2026-03-16`
Status: `active`

## Purpose

This document defines how the browser acquires backend capability and where elevation is allowed.

## Principles

1. The browser starts with the smallest useful session, not with operator-wide capability.
2. Elevated actions are bound to named UI surfaces, not to one broad bootstrap token.
3. Local bootstrap is a development convenience only and must not remain enabled outside local development.
4. A dedicated session signing secret is required for non-local startup.
5. UI status cards must reflect backend truth or clearly state that they are informational only.

## Bootstrap Session

The `/api/auth/session` bootstrap path issues a scoped browser session.

Default surface:

- `conversation`

Default scopes:

- `chat:read`
- `chat:write`
- `feedback:write`

This means a newly loaded browser session does not automatically receive:

- `metrics:read`
- `tools:execute`
- `build:run`
- `fs:write`
- `evolution:control`

## Surface Sessions

Named surfaces are allowed to exchange the bootstrap session for a more specific scoped session.

Surface bundles:

1. `conversation`
   Scopes: `chat:read`, `chat:write`, `feedback:write`
2. `runtime_console`
   Scopes: `metrics:read`
3. `builder`
   Scopes: `chat:read`, `chat:write`, `tools:execute`, `build:run`, `fs:write`
4. `evolution`
   Scopes: `evolution:control`, `metrics:read`

Requested scopes are intersected with the allowed bundle for the selected surface. Unknown surfaces are rejected.

## Non-Local Startup Rules

The backend must refuse startup outside local development when either of the following is true:

1. session signing falls back to an API-key-derived secret
2. local bootstrap remains enabled

Operational requirement:

- set `HELPER_SESSION_SIGNING_KEY`
- set `HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP=false`

## Operator Expectations

1. Entering `Builder` or `Evolution` should trigger explicit surface-specific elevation.
2. Control-plane views should use runtime telemetry instead of hardcoded product claims.
3. Documentation and release claims must reference the active gate snapshot, not historical summaries.
