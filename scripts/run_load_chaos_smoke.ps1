param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Write-Host "[LoadChaos] Running load/chaos suite..."
& (Join-Path $PSScriptRoot "load_streaming_chaos.ps1") -Configuration $Configuration -NoBuild:$NoBuild -NoRestore:$NoRestore
if ($LASTEXITCODE -ne 0) {
    throw "[LoadChaos] Load/chaos suite failed."
}

Write-Host "[LoadChaos] Passed."
