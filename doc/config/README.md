# Config

Status: `active`
Updated: `2026-03-16`

## Purpose

This folder is the canonical entry point for governed configuration in HELPER.

## Read First

1. [Environment Reference](ENV_REFERENCE.md)
2. [Trust Model](../security/TRUST_MODEL.md)
3. [Repo Hygiene And Runtime Artifact Governance](../security/REPO_HYGIENE_AND_RUNTIME_ARTIFACT_GOVERNANCE.md)

## Operational Rules

1. `doc/config/ENV_REFERENCE.md` and `.env.local.example` are generated from `src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs`.
2. Refresh generated artifacts with `powershell -ExecutionPolicy Bypass -File scripts/generate_env_reference.ps1`.
3. Validate generated artifacts and governed script usage with `powershell -ExecutionPolicy Bypass -File scripts/check_env_governance.ps1`.
4. Add new governed variables to `BackendEnvironmentInventory` before adding them to `.env.local.example` or the governed scripts list.

