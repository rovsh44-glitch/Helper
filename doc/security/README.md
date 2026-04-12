# HELPER Security Docs

Status: `active`
Updated: `2026-04-11`

## Purpose

This directory contains the canonical security and trust-boundary documentation for HELPER.

## Read In Order

1. [Trust Model](TRUST_MODEL.md)
2. [Repo Hygiene And Runtime Artifact Governance](REPO_HYGIENE_AND_RUNTIME_ARTIFACT_GOVERNANCE.md)
3. [Public Showcase Boundary Policy](PUBLIC_SHOWCASE_BOUNDARY_POLICY_2026-03-24.md)
4. [Public Visibility Decision](PUBLIC_VISIBILITY_DECISION_2026-04-11.md)
5. [GitHub Private Repo Security Automation Status](GITHUB_PRIVATE_REPO_SECURITY_AUTOMATION_STATUS_2026-04-12.md)
6. [Public Launch Disclosure Review](PUBLIC_LAUNCH_DISCLOSURE_REVIEW_2026-03-24.md)
7. [Public Launch Disclosure Review Bundle](public_launch_review_2026-04-11/PUBLIC_LAUNCH_DISCLOSURE_REVIEW.md)
8. [Public Showcase Export Note](PUBLIC_SHOWCASE_EXPORT_NOTE_2026-03-24.md)
9. [ADR Browser Auth Session Bootstrap](../adr/ADR_BROWSER_AUTH_SESSION_BOOTSTRAP.md)
10. [ADR Frontend API Transport Policy](../adr/ADR_FRONTEND_API_TRANSPORT_POLICY.md)
11. [ADR Data Root Separation](../adr/ADR_DATA_ROOT_SEPARATION.md)

## Stable Security Rules

1. browser code uses scoped backend sessions instead of raw long-lived secrets
2. runtime auth artifacts live under `HELPER_DATA_ROOT`, not under `src/`
3. runtime data roots are separated from source surfaces
4. hygiene gates are part of the operator trust boundary, not optional cleanup
5. GitHub launch uses `default deny` with a curated public-showcase allowlist
