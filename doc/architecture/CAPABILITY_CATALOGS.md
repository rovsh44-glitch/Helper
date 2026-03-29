# Capability Catalogs

Status: `active`
Updated: `2026-03-16`

Helper exposes capability truth through one machine-readable snapshot instead of scattering it across source and certification logs.

## Model Catalog

Each model route declares:

1. route key
2. model class
3. intended use
4. latency tier
5. streaming support
6. tool-use support
7. vision support
8. fallback class
9. configured fallback model
10. currently resolved model
11. whether that model is visible in the active gateway snapshot

## Declared Capability Catalog

Declared capability coverage is tracked for:

1. templates
2. tools
3. extensions

Each entry carries:

1. stable capability id
2. owning surface and owner id
3. declared capability
4. current status
5. owning gate, when one exists
6. evidence type and evidence ref
7. certification relevance and current certification enablement

## Certification Linkage

Template capability failures now use the same stable capability identifiers as the operator catalog.

This allows certification output to reference concrete capability ids instead of only free-form text.
