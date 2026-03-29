[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [switch]$RunReset,
    [switch]$RunFullReindex,
    [switch]$ClearExistingV2Collections,
    [string]$WorkspaceRoot = ".",
    [string]$ArchiveManifestPath = "",
    [string]$ValidationReportPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:HELPER_API_KEY
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "HELPER_API_KEY is required."
}

$env:HELPER_INDEX_PIPELINE_VERSION = "v2"
$env:HELPER_RAG_ALLOW_V1_FALLBACK = "false"

$paths = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$envFile = Join-Path $paths.HelperRoot ".env.local"

function Set-EnvFileValue {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $lines = if (Test-Path $Path) { @(Get-Content $Path) } else { @() }
    $prefix = "$Name="
    $updated = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match ("^{0}=" -f [regex]::Escape($Name))) {
            $lines[$i] = $prefix + $Value
            $updated = $true
            break
        }
    }

    if (-not $updated) {
        $lines += ($prefix + $Value)
    }

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

Set-EnvFileValue -Path $envFile -Name "HELPER_INDEX_PIPELINE_VERSION" -Value "v2"
Set-EnvFileValue -Path $envFile -Name "HELPER_RAG_ALLOW_V1_FALLBACK" -Value "false"

Write-Host "[Cutover] HELPER_INDEX_PIPELINE_VERSION=v2"
Write-Host "[Cutover] HELPER_RAG_ALLOW_V1_FALLBACK=false"
Write-Host "[Cutover] Default indexing and retrieval now target v2."

if (-not [string]::IsNullOrWhiteSpace($ArchiveManifestPath)) {
    $archiveScript = Join-Path $PSScriptRoot "archive_v1_collections.ps1"
    & $archiveScript -OutputPath $ArchiveManifestPath
}

if ($ClearExistingV2Collections.IsPresent) {
    $clearScript = Join-Path $PSScriptRoot "clear_pipeline_collections.ps1"
    & $clearScript -PipelineVersion "v2"
}

if ($RunReset) {
    Invoke-RestMethod -Method Post -Uri ($ApiBaseUrl.TrimEnd("/") + "/api/indexing/reset") -Headers @{ "X-API-KEY" = $ApiKey } -Body "{}" -ContentType "application/json" -TimeoutSec 120 | Out-Null
    Write-Host "[Cutover] Indexing reset requested."
}

if ($RunFullReindex) {
    $script = Join-Path $PSScriptRoot "run_ordered_library_indexing.ps1"
    & $script -ApiBaseUrl $ApiBaseUrl -ApiKey $ApiKey -PipelineVersion "v2"
}

if (-not [string]::IsNullOrWhiteSpace($ValidationReportPath)) {
    $validationScript = Join-Path $PSScriptRoot "write_chunking_post_cutover_validation.ps1"
    & $validationScript -OutputPath $ValidationReportPath
}
