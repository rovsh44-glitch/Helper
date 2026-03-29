# Capability Catalogs

Status: `active`
Updated: `2026-03-16`

## Purpose

This is the canonical operator entry point for capability truth.

## Surfaces

1. `GET /api/capabilities/catalog`
2. `Settings -> Capability Coverage`
3. `Runtime Console -> Operator Capability Coverage`

## Identifier Scheme

1. model route: `model-route:<route-key>`
2. template capability: `template:<template-id>:capability:<capability>`
3. tool: `tool:<tool-name>`
4. extension capability: `extension:<extension-id>:capability:<capability>`

## Evidence Rules

1. templates are owned by the `capability-contract` smoke scenario
2. tools and extensions stay visibly unmapped until a real certification owner exists
3. capability ids are intended to be stable enough for operator and certification references
