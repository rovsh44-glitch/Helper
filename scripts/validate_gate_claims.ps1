param(
    [string]$SnapshotPath = "doc/certification/active/CURRENT_GATE_SNAPSHOT.json",
    [string]$CurrentStatePath = "doc/CURRENT_STATE.md",
    [string]$CurrentCertStatePath = "doc/certification/active/CURRENT_CERT_STATE.md"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common\CertificationSummaryRenderer.ps1")

if (-not (Test-Path $SnapshotPath)) {
    throw "[GateClaims] Snapshot not found: $SnapshotPath"
}

$snapshot = Get-Content -Path $SnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
$currentState = Get-Content -Path $CurrentStatePath -Raw -Encoding UTF8
$currentCertState = Get-Content -Path $CurrentCertStatePath -Raw -Encoding UTF8
$expectedCurrentState = ConvertTo-CurrentStateMarkdown -Snapshot $snapshot -SnapshotPath $SnapshotPath
$expectedCurrentCertState = ConvertTo-CurrentCertStateMarkdown -Snapshot $snapshot -SnapshotPath $SnapshotPath

function Normalize-Markdown {
    param([Parameter(Mandatory = $true)][string]$Content)

    return (($Content -replace "`r`n", "`n").Trim())
}

if ((Normalize-Markdown -Content $currentState) -ne (Normalize-Markdown -Content $expectedCurrentState)) {
    throw "[GateClaims] CURRENT_STATE.md is not the canonical rendering of CURRENT_GATE_SNAPSHOT.json."
}

if ((Normalize-Markdown -Content $currentCertState) -ne (Normalize-Markdown -Content $expectedCurrentCertState)) {
    throw "[GateClaims] CURRENT_CERT_STATE.md is not the canonical rendering of CURRENT_GATE_SNAPSHOT.json."
}

Write-Host "[GateClaims] Snapshot and active markdown claims are aligned." -ForegroundColor Green
