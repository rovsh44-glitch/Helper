# HELPER Extensions

Status: `active`
Updated: `2026-03-16`

## Purpose

This document is the canonical entry point for HELPER's extension platform.

The checked-in extension surface is manifest-driven. `mcp_config/servers.json` is now legacy-only and no longer the authoritative configuration source.

## Source Of Truth

1. [Extension manifest schema](../../mcp_config/extension-manifest.schema.json)
2. [Built-in local tools manifest](../../mcp_config/extensions/helper.local-tools.json)
3. [Internal Helper actions manifest](../../mcp_config/extensions/helper.internal-actions.json)
4. [Sample external provider](../../mcp_config/extensions/sample.brave-search.external.json)
5. [Sample local experimental provider](../../mcp_config/extensions/sample.playwright.experimental.json)

## Manifest Model

Each manifest declares:

1. `schemaVersion`
2. `id`
3. `category`
4. `providerType`
5. `transport`
6. `command` and `args` when transport is `stdio`
7. `requiredEnv`
8. `capabilities`
9. `trustLevel`
10. `defaultEnabled`
11. `disabledInCertificationMode`
12. `quietWhenUnavailable`
13. optional `declaredTools` or `toolPolicy`

## Categories

1. `built_in`: code-backed local tools such as `dotnet_test`, `read_file`, `write_file`
2. `internal`: higher-level Helper actions published via the MCP host
3. `external`: optional trusted MCP providers
4. `experimental`: optional providers that require explicit experimental enablement

## Activation Rules

1. Checked-in sample providers are disabled by default.
2. Enable optional providers with `HELPER_EXTENSION_ENABLED_IDS=<id1,id2>`.
3. Disable providers explicitly with `HELPER_EXTENSION_DISABLED_IDS=<id1,id2>`.
4. Experimental providers additionally require `HELPER_ENABLE_EXPERIMENTAL_EXTENSIONS=true` unless explicitly enabled.
5. In certification/eval mode (`HELPER_CERT_MODE` or `HELPER_EVAL_MODE`), manifests marked `disabledInCertificationMode=true` stay silent and do not bootstrap.

## Trust And Permit Policy

1. Trust is declared in the manifest and can be further restricted with `HELPER_MCP_TRUSTED_SERVERS`.
2. Tool-level MCP permissions come from `toolPolicy.allowedTools` or the legacy env overlay `HELPER_MCP_PERMITTED_TOOLS`.
3. `HELPER_MCP_ALLOW_ANY_TRUSTED_TOOL=true` keeps the old wildcard behavior, but manifest policy is preferred.
4. See [Trust Model](../security/TRUST_MODEL.md) for the broader browser/runtime trust boundary.

## Authoring Constraints

1. Checked-in manifests must be portable: no absolute machine-specific paths in `command` or `args`.
2. Secrets are not stored in manifests; use `requiredEnv` instead.
3. If a provider is optional, set `quietWhenUnavailable=true`.
4. If a provider should never appear in counted certification, set `disabledInCertificationMode=true`.

## Operator Workflow

1. Author or edit a manifest under `mcp_config/extensions/`.
2. Validate the registry with `powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_runtime_cli.ps1 extension-registry`.
3. Keep checked-in samples disabled by default.
4. Enable the provider only in the operator environment that actually needs it.
