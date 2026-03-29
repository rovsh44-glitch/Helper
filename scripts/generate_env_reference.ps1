param(
    [string]$EnvReferencePath = "doc/config/ENV_REFERENCE.md",
    [string]$EnvInventoryJsonPath = "doc/config/ENV_INVENTORY.json",
    [string]$EnvExamplePath = ".env.local.example"
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "env_inventory_common.ps1")

$repoRoot = Get-HelperRepoRoot
$resolvedEnvReferencePath = Join-Path $repoRoot $EnvReferencePath
$resolvedEnvInventoryJsonPath = Join-Path $repoRoot $EnvInventoryJsonPath
$resolvedEnvExamplePath = Join-Path $repoRoot $EnvExamplePath

foreach ($path in @($resolvedEnvReferencePath, $resolvedEnvInventoryJsonPath, $resolvedEnvExamplePath)) {
    $directory = Split-Path -Parent $path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
}

Set-Content -Path $resolvedEnvReferencePath -Value (Get-BackendEnvReferenceMarkdown) -Encoding UTF8
Set-Content -Path $resolvedEnvInventoryJsonPath -Value (Get-BackendEnvInventoryJson) -Encoding UTF8
Set-Content -Path $resolvedEnvExamplePath -Value (Get-BackendLocalEnvTemplate) -Encoding UTF8

Write-Host "[Config] Generated $EnvReferencePath, $EnvInventoryJsonPath, and $EnvExamplePath." -ForegroundColor Green
